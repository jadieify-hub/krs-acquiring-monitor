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
    public sealed class ApplicationUpdater
    {
        private const string ManifestUrl =
            "https://github.com/jadieify-hub/krs-acquiring-monitor/releases/latest/download/update.json";
        private const string InstallerArguments =
            "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCLOSEAPPLICATIONS /NORESTARTAPPLICATIONS /SP-";
        private readonly SafeLogger _logger;
        private string _installerPath;
        private string _installerSha256;

        public ApplicationUpdater(SafeLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException("logger");
        }

        public bool HasPreparedUpdate
        {
            get { return _installerPath != null; }
        }

        public string Status { get; private set; } = "Проверка ещё не выполнялась.";

        public DateTimeOffset? LastCheckUtc { get; private set; }

        public async Task CheckAndDownloadAsync(
            Version currentVersion,
            CancellationToken cancellationToken)
        {
            if (HasPreparedUpdate || currentVersion == null) return;
            if (!File.Exists(Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "unins000.exe")))
            {
                Status = "Portable-копия: обновляйте вручную из официального GitHub Release.";
                return;
            }

            LastCheckUtc = DateTimeOffset.UtcNow;
            Status = "Проверяю новую версию…";
            string installerPath = null;
            bool prepared = false;
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
                            Fail("manifest-http-404");
                            return;
                        }

                        if (!response.IsSuccessStatusCode)
                        {
                            Fail("manifest-http-" + (int)response.StatusCode);
                            return;
                        }

                        manifestJson = await response.Content.ReadAsStringAsync();
                    }

                    ValidatedUpdate update;
                    if (!UpdateManifest.TryParse(manifestJson, out update))
                    {
                        Fail("manifest-format");
                        return;
                    }
                    var normalizedCurrent = new Version(
                        currentVersion.Major, currentVersion.Minor, Math.Max(0, currentVersion.Build));
                    if (update.Version.CompareTo(normalizedCurrent) <= 0)
                    {
                        Status = "Новых версий нет. Установлена версия " + currentVersion.ToString(3) + ".";
                        _logger.Write(
                            SafeLogEvent.UpdateCheckCompleted,
                            "no-newer-valid-release",
                            null);
                        return;
                    }

                    Status = "Скачиваю версию " + update.Version.ToString(3) + "…";
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
                                Fail("installer-http-" + (int)response.StatusCode);
                                return;
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
                        Fail("installer-hash");
                        return;
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    _installerPath = installerPath;
                    _installerSha256 = update.Sha256;
                    Status = "Версия " + update.Version.ToString(3) +
                        " готова. Установка начнётся после 30 секунд без банковских операций, при доступном журнале и закрытых окнах настроек и диагностики.";
                }

                prepared = true;
                _logger.Write(SafeLogEvent.UpdatePrepared, "verified-waiting-for-idle", null);
            }
            catch (OperationCanceledException exception)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    Fail("timeout", exception);
                }
                else Status = "Проверка отменена при выходе из программы.";
            }
            catch (HttpRequestException exception)
            {
                Fail("network", exception);
            }
            catch (IOException exception)
            {
                Fail("file", exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                Fail("access", exception);
            }
            catch (Exception exception)
            {
                Fail("unexpected", exception);
            }
            finally
            {
                if (!prepared)
                {
                    TryDeleteFile(installerPath);
                }
            }
        }

        public bool TryStartInstaller()
        {
            if (!HasPreparedUpdate)
            {
                return false;
            }
            string installerPath = _installerPath;
            _installerPath = null;
            bool started = false;
            try
            {
                // The download may have waited hours; verify again immediately before execution.
                if (!UpdateManifest.HashMatches(installerPath, _installerSha256))
                {
                    Fail("installer-hash");
                    return false;
                }
                string applicationDirectory = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
                string logPath = Path.Combine(Path.GetDirectoryName(installerPath), "installer.log");
                var startInfo = new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = InstallerArguments +
                        " /WAITPID=" + Process.GetCurrentProcess().Id +
                        " /DIR=\"" + applicationDirectory + "\"" +
                        " /LOG=\"" + logPath + "\"",
                    WorkingDirectory = Path.GetDirectoryName(installerPath),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        Fail("installer-start");
                        return false;
                    }
                }

                started = true;
                Status = "Установщик запущен, монитор завершает работу.";
                _logger.Write(
                    SafeLogEvent.UpdateInstallerStarted,
                    "verified",
                    null);
                return true;
            }
            catch (IOException exception)
            {
                Fail("file", exception);
                return false;
            }
            catch (UnauthorizedAccessException exception)
            {
                Fail("access", exception);
                return false;
            }
            catch (Win32Exception exception)
            {
                Fail("process", exception);
                return false;
            }
            catch (Exception exception)
            {
                Fail("unexpected", exception);
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

        private void Fail(string code, Exception exception = null)
        {
            Status = "Не удалось обновить программу (" + code + "). Текущая версия продолжает работать.";
            _logger.Write(SafeLogEvent.UpdateCheckFailed, code, exception);
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
