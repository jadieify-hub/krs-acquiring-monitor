using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Krs.AcquiringMonitor.Terminal
{
    public sealed class TerminalStatisticsResult
    {
        private TerminalStatisticsResult(bool success, string reportText, string errorMessage)
        {
            Success = success;
            ReportText = reportText ?? string.Empty;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public bool Success { get; private set; }

        public string ReportText { get; private set; }

        public string ErrorMessage { get; private set; }

        public static TerminalStatisticsResult Ok(string reportText)
        {
            return new TerminalStatisticsResult(true, reportText, string.Empty);
        }

        public static TerminalStatisticsResult Fail(string errorMessage)
        {
            return new TerminalStatisticsResult(false, string.Empty, errorMessage);
        }
    }

    public sealed class TerminalStatisticsClient
    {
        private readonly object _activeSync = new object();
        private readonly string _helperPath;
        private Process _activeProcess;
        private string _activeOutputPath;
        private string _activeQueryDirectory;

        public TerminalStatisticsClient()
            : this(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Krs.AcquiringMonitor.TerminalQuery.exe"))
        {
        }

        public TerminalStatisticsClient(string helperPath)
        {
            _helperPath = helperPath;
            CleanupStaleReports(GetTemporaryRoot());
        }

        public async Task<TerminalStatisticsResult> QueryAsync(
            string uposDirectory,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (!Directory.Exists(uposDirectory))
            {
                return TerminalStatisticsResult.Fail("Папка Сбербанка недоступна.");
            }

            if (!File.Exists(_helperPath))
            {
                return TerminalStatisticsResult.Fail("Не найден модуль запроса статистики.");
            }

            string temporaryRoot = GetTemporaryRoot();
            string queryDirectory = null;
            string outputPath = null;

            try
            {
                Directory.CreateDirectory(temporaryRoot);
                CleanupStaleReports(temporaryRoot);
                queryDirectory = Path.Combine(
                    temporaryRoot,
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(queryDirectory);
                outputPath = Path.Combine(queryDirectory, "report.txt");

                var startInfo = new ProcessStartInfo
                {
                    FileName = _helperPath,
                    Arguments =
                        "--directory " + QuoteArgument(uposDirectory) +
                        " --output " + QuoteArgument(outputPath),
                    WorkingDirectory = uposDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    StandardErrorEncoding = Encoding.UTF8,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        return TerminalStatisticsResult.Fail(
                            "Не удалось запустить модуль запроса статистики.");
                    }

                    RegisterActiveProcess(process, outputPath, queryDirectory);
                    try
                    {
                        Task<string> errorTask = process.StandardError.ReadToEndAsync();
                        Task exitTask = Task.Run(() => process.WaitForExit());
                        Task delayTask = Task.Delay(timeout, cancellationToken);
                        Task completed = await Task.WhenAny(exitTask, delayTask).ConfigureAwait(false);
                        if (completed != exitTask)
                        {
                            TryKill(process);
                            return TerminalStatisticsResult.Fail(
                                cancellationToken.IsCancellationRequested
                                    ? "Запрос отменён."
                                    : "Терминал не ответил за отведённое время.");
                        }

                        await exitTask.ConfigureAwait(false);
                        string error = (await errorTask.ConfigureAwait(false)).Trim();
                        if (process.ExitCode != 0)
                        {
                            return TerminalStatisticsResult.Fail(
                                string.IsNullOrWhiteSpace(error)
                                    ? "Не удалось получить текущие итоги."
                                    : error);
                        }
                    }
                    finally
                    {
                        ClearActiveProcess(process);
                    }
                }

                if (!File.Exists(outputPath))
                {
                    return TerminalStatisticsResult.Fail(
                        "Модуль статистики не создал отчёт.");
                }

                string report = File.ReadAllText(outputPath, Encoding.UTF8);
                return string.IsNullOrWhiteSpace(report)
                    ? TerminalStatisticsResult.Fail("Терминал вернул пустой отчёт.")
                    : TerminalStatisticsResult.Ok(report);
            }
            catch (IOException)
            {
                return TerminalStatisticsResult.Fail(
                    "Ошибка доступа к модулю или временному файлу.");
            }
            catch (UnauthorizedAccessException)
            {
                return TerminalStatisticsResult.Fail(
                    "Недостаточно прав для запроса текущих итогов.");
            }
            finally
            {
                TryDeleteFile(outputPath);
                TryDeleteEmptyDirectory(queryDirectory);
                ClearActiveArtifacts(outputPath, queryDirectory);
            }
        }

        public void CancelActiveQuery()
        {
            Process process;
            string outputPath;
            string queryDirectory;
            lock (_activeSync)
            {
                process = _activeProcess;
                outputPath = _activeOutputPath;
                queryDirectory = _activeQueryDirectory;
                _activeProcess = null;
                _activeOutputPath = null;
                _activeQueryDirectory = null;
            }

            if (process != null)
            {
                TryKill(process);
            }

            TryDeleteFile(outputPath);
            TryDeleteEmptyDirectory(queryDirectory);
        }

        private void RegisterActiveProcess(
            Process process,
            string outputPath,
            string queryDirectory)
        {
            lock (_activeSync)
            {
                _activeProcess = process;
                _activeOutputPath = outputPath;
                _activeQueryDirectory = queryDirectory;
            }
        }

        private void ClearActiveProcess(Process process)
        {
            lock (_activeSync)
            {
                if (!ReferenceEquals(_activeProcess, process))
                {
                    return;
                }

                _activeProcess = null;
            }
        }

        private void ClearActiveArtifacts(
            string outputPath,
            string queryDirectory)
        {
            lock (_activeSync)
            {
                if (!string.Equals(
                        _activeOutputPath,
                        outputPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        _activeQueryDirectory,
                        queryDirectory,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _activeOutputPath = null;
                _activeQueryDirectory = null;
            }
        }

        private static string QuoteArgument(string value)
        {
            string argument = value ?? string.Empty;
            var quoted = new StringBuilder(argument.Length + 2);
            quoted.Append('"');

            int backslashes = 0;
            foreach (char character in argument)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (character == '"')
                {
                    quoted.Append('\\', backslashes * 2 + 1);
                    quoted.Append('"');
                    backslashes = 0;
                    continue;
                }

                quoted.Append('\\', backslashes);
                quoted.Append(character);
                backslashes = 0;
            }

            quoted.Append('\\', backslashes * 2);
            quoted.Append('"');
            return quoted.ToString();
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
        }

        private static string GetTemporaryRoot()
        {
            return Path.Combine(
                Path.GetTempPath(),
                "KRS-AcquiringMonitor");
        }

        private static void CleanupStaleReports(string temporaryRoot)
        {
            if (string.IsNullOrWhiteSpace(temporaryRoot) ||
                !Directory.Exists(temporaryRoot))
            {
                return;
            }

            try
            {
                foreach (string file in Directory.EnumerateFiles(
                    temporaryRoot,
                    "*.txt",
                    SearchOption.TopDirectoryOnly))
                {
                    TryDeleteFile(file);
                }

                foreach (string directory in Directory.EnumerateDirectories(
                    temporaryRoot,
                    "*",
                    SearchOption.TopDirectoryOnly))
                {
                    TryDeleteFile(Path.Combine(directory, "report.txt"));
                    TryDeleteEmptyDirectory(directory);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
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

        private static void TryDeleteEmptyDirectory(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    Directory.Delete(path, false);
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
