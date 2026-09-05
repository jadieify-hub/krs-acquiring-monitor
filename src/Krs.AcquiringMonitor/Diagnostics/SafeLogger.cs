using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Krs.AcquiringMonitor.Diagnostics
{
    public enum SafeLogEvent
    {
        ApplicationStarted,
        LogMonitorUnavailable,
        SettingsFailure,
        TerminalQueryStarted,
        TerminalQueryFailed,
        TerminalQuerySucceeded,
        UpdateCheckFailed,
        UpdateInstallerStarted
    }

    public sealed class SafeLogger
    {
        private readonly object _sync = new object();
        private readonly string _path;

        public SafeLogger(string baseDirectory)
        {
            _path = Path.Combine(baseDirectory, "diagnostics.log");
        }

        public void Write(SafeLogEvent eventCode, string safeDetail, Exception exception)
        {
            try
            {
                string detail = Sanitize(safeDetail);
                string exceptionName = exception == null
                    ? string.Empty
                    : exception.GetType().Name;
                string line = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:O}\t{1}\t{2}\t{3}{4}",
                    DateTimeOffset.Now,
                    eventCode,
                    detail,
                    exceptionName,
                    Environment.NewLine);

                lock (_sync)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path));
                    File.AppendAllText(_path, line, new UTF8Encoding(false));
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static string Sanitize(string value)
        {
            string singleLine = (value ?? string.Empty)
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace('\t', ' ');
            return singleLine.Length <= 300 ? singleLine : singleLine.Substring(0, 300);
        }
    }
}
