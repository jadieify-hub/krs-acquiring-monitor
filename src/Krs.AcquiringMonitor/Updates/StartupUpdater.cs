using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Krs.AcquiringMonitor.Diagnostics;

namespace Krs.AcquiringMonitor.Updates
{
    public sealed class StartupUpdater
    {
        private const string ManifestUrl =
            "https://github.com/jadieify-hub/krs-acquiring-monitor/releases/latest/download/update.json";
        private const string InstallerArguments =
            "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /SP-";
        private readonly SafeLogger _logger;

        public StartupUpdater(SafeLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException("logger");
        }

        public async Task<bool> CheckAndInstallAsync(
            Version currentVersion,
            CancellationToken cancellationToken)
        {
            if (currentVersion == null ||
                !File.Exists(Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "unins000.exe")))
            {
                return false;
            }

            string installerPath = null;
            bool started = false;
            try
            {
                string manifestJson;
                using (var client = new HttpClient())
                using (var manifestCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    client.Timeout = TimeSpan.FromMinutes(5);
                    manifestCancellation.CancelAfter(TimeSpan.FromSeconds(15));
                    using (HttpResponseMessage response = await client.GetAsync(
                        ManifestUrl,
                        HttpCompletionOption.ResponseContentRead,
                        manifestCancellation.Token))
                    {
                        if (response.StatusCode == HttpStatusCode.NotFound)
                        {
                            return false;
                        }

                        if (!response.IsSuccessStatusCode)
                        {
                            _logger.Write(
                                SafeLogEvent.UpdateCheckFailed,
                                "manifest-http-" + (int)response.StatusCode,
                                null);
                            return false;
                        }

                        manifestJson = await response.Content.ReadAsStringAsync();
                    }

                    ValidatedUpdate update;
                    if (!UpdateManifest.TrySelect(
                            manifestJson,
                            currentVersion,
                            out update))
                    {
                        _logger.Write(
                            SafeLogEvent.UpdateCheckFailed,
                            "manifest-not-selected",
                            null);
                        return false;
                    }

                    string updateDirectory = Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.LocalApplicationData),
                        "KRS",
                        "AcquiringMonitor",
                        "updates");
                    Directory.CreateDirectory(updateDirectory);
                    installerPath = Path.Combine(updateDirectory, update.FileName);
                    TryDeleteFile(installerPath);

                    using (var downloadCancellation =
                        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        downloadCancellation.CancelAfter(TimeSpan.FromMinutes(5));
                        using (HttpResponseMessage response = await client.GetAsync(
                            update.DownloadUrl,
                            HttpCompletionOption.ResponseHeadersRead,
                            downloadCancellation.Token))
                        {
                            if (!response.IsSuccessStatusCode)
                            {
                                _logger.Write(
                                    SafeLogEvent.UpdateCheckFailed,
                                    "installer-http-" + (int)response.StatusCode,
                                    null);
                                return false;
                            }

                            using (Stream source =
                                await response.Content.ReadAsStreamAsync())
                            using (var destination = new FileStream(
                                installerPath,
                                FileMode.CreateNew,
                                FileAccess.Write,
                                FileShare.None,
                                81920,
                                true))
                            {
                                await source.CopyToAsync(
                                    destination,
                                    81920,
                                    downloadCancellation.Token);
                            }
                        }
                    }

                    if (!UpdateManifest.HashMatches(installerPath, update.Sha256))
                    {
                        _logger.Write(
                            SafeLogEvent.UpdateCheckFailed,
                            "installer-hash",
                            null);
                        return false;
                    }
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = InstallerArguments,
                    WorkingDirectory = Path.GetDirectoryName(installerPath),
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        _logger.Write(
                            SafeLogEvent.UpdateCheckFailed,
                            "installer-start",
                            null);
                        return false;
                    }
                }

                started = true;
                _logger.Write(
                    SafeLogEvent.UpdateInstallerStarted,
                    "verified",
                    null);
                return true;
            }
            catch (OperationCanceledException exception)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    _logger.Write(
                        SafeLogEvent.UpdateCheckFailed,
                        "timeout",
                        exception);
                }

                return false;
            }
            catch (HttpRequestException exception)
            {
                _logger.Write(
                    SafeLogEvent.UpdateCheckFailed,
                    "network",
                    exception);
                return false;
            }
            catch (IOException exception)
            {
                _logger.Write(
                    SafeLogEvent.UpdateCheckFailed,
                    "file",
                    exception);
                return false;
            }
            catch (UnauthorizedAccessException exception)
            {
                _logger.Write(
                    SafeLogEvent.UpdateCheckFailed,
                    "access",
                    exception);
                return false;
            }
            catch (Win32Exception exception)
            {
                _logger.Write(
                    SafeLogEvent.UpdateCheckFailed,
                    "process",
                    exception);
                return false;
            }
            catch (Exception exception)
            {
                _logger.Write(
                    SafeLogEvent.UpdateCheckFailed,
                    "unexpected",
                    exception);
                return false;
            }
            finally
            {
                if (!started)
                {
                    TryDeleteFile(installerPath);
                }
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
