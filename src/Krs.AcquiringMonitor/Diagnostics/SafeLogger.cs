using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Krs.AcquiringMonitor.Diagnostics
{
    public enum SafeLogEvent
    {
        ApplicationStarted,
        ApplicationExiting,
        LogMonitorUnavailable,
        SettingsFailure,
        TerminalQueryStarted,
        TerminalQueryDeferred,
        TerminalQueryFailed,
        TerminalQuerySucceeded,
        UpdateCheckFailed,
        UpdateCheckCompleted,
        UpdatePrepared,
        UpdateInstallerStarted
    }

    public sealed class SafeLogger
    {
        private const long MaximumFileBytes = 1024L * 1024L;
        private static readonly TimeSpan RepeatInterval = TimeSpan.FromMinutes(5);
        private readonly object _sync = new object();
        private readonly string _path;
        private volatile string _lastFailure;
        private string _lastWritten = string.Empty;
        private DateTimeOffset _lastWrittenUtc = DateTimeOffset.MinValue;

        public string LastFailure { get { return _lastFailure; } }

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

                if (eventCode == SafeLogEvent.LogMonitorUnavailable ||
                    eventCode == SafeLogEvent.SettingsFailure ||
                    eventCode == SafeLogEvent.TerminalQueryFailed ||
                    eventCode == SafeLogEvent.UpdateCheckFailed)
                    _lastFailure = line.TrimEnd();

                lock (_sync)
                {
                    // An unavailable journal repeats every second: without this the log would
                    // grow by tens of thousands of identical lines a day on a cash register.
                    string signature = eventCode + "	" + detail + "	" + exceptionName;
                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    if (string.Equals(signature, _lastWritten, StringComparison.Ordinal) &&
                        now - _lastWrittenUtc < RepeatInterval)
                    {
                        return;
                    }

                    _lastWritten = signature;
                    _lastWrittenUtc = now;
                    Directory.CreateDirectory(Path.GetDirectoryName(_path));
                    RollIfTooLarge();
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

        // Keeps at most the current file and one previous, so diagnostics cannot fill the disk
        // even when distinct failures alternate and defeat the repeat filter above.
        private void RollIfTooLarge()
        {
            var info = new FileInfo(_path);
            if (!info.Exists || info.Length < MaximumFileBytes)
            {
                return;
            }

            string previous = _path + ".1";
            if (File.Exists(previous))
            {
                File.Delete(previous);
            }

            File.Move(_path, previous);
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
