using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Krs.AcquiringMonitor.Core.Monitoring;
using Krs.AcquiringMonitor.Diagnostics;

namespace Krs.AcquiringMonitor.Monitoring
{
    public sealed class BankLogSnapshotEventArgs : EventArgs
    {
        public BankLogSnapshotEventArgs(BankLogSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public BankLogSnapshot Snapshot { get; private set; }
    }

    public sealed class BankLogMonitor : IDisposable
    {
        private const int IdentityPrefixLength = 4096;

        private static readonly Regex LogNamePattern = new Regex(
            @"^sbkernel\d{4}\.log$",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant |
            RegexOptions.IgnoreCase);

        private readonly object _sync = new object();
        private readonly string _directory;
        private readonly BankLogSnapshot _fallback;
        private readonly SafeLogger _logger;
        private readonly string _resumeLogFileName;
        private readonly long _resumeLogOffset;
        private readonly string _resumeLogPrefixHash;
        private readonly int[] _expectedDepartments;
        private BankLogParser _parser;
        private string _activePath;
        private long _activeOffset;
        private int _activePrefixLength;
        private string _activePrefixHash = string.Empty;
        private string _partialLine = string.Empty;
        private FileSystemWatcher _watcher;
        private Timer _timer;
        private long _revision;
        private long _sourceGeneration;
        private bool _disposed;

        public BankLogMonitor(
            string uposDirectory,
            BankLogSnapshot fallback,
            SafeLogger logger)
            : this(
                uposDirectory,
                fallback,
                logger,
                string.Empty,
                0L,
                string.Empty)
        {
        }

        public BankLogMonitor(
            string uposDirectory,
            BankLogSnapshot fallback,
            SafeLogger logger,
            string resumeLogFileName,
            long resumeLogOffset,
            string resumeLogPrefixHash)
            : this(
                uposDirectory,
                fallback,
                logger,
                resumeLogFileName,
                resumeLogOffset,
                resumeLogPrefixHash,
                null)
        {
        }

        public BankLogMonitor(
            string uposDirectory,
            BankLogSnapshot fallback,
            SafeLogger logger,
            string resumeLogFileName,
            long resumeLogOffset,
            string resumeLogPrefixHash,
            IEnumerable<int> expectedDepartments)
        {
            _directory = uposDirectory ?? string.Empty;
            _fallback = fallback;
            _logger = logger;
            _resumeLogFileName = resumeLogFileName ?? string.Empty;
            _resumeLogOffset = resumeLogOffset;
            _resumeLogPrefixHash = resumeLogPrefixHash ?? string.Empty;
            _expectedDepartments = expectedDepartments == null
                ? new int[0]
                : expectedDepartments
                    .Where(value => value > 0)
                    .Distinct()
                    .Take(2)
                    .ToArray();
            _parser = new BankLogParser(_expectedDepartments);
            CurrentSnapshot = fallback == null
                ? BankLogSnapshot.FromTotals(new Dictionary<int, long>(), true)
                : fallback.AsStale();
        }

        public event EventHandler<BankLogSnapshotEventArgs> SnapshotChanged;

        public BankLogSnapshot CurrentSnapshot { get; private set; }

        public bool HasPendingOperation
        {
            get { return CurrentSnapshot.HasPendingOperation; }
        }

        public long CaptureRevision()
        {
            lock (_sync)
            {
                return _revision;
            }
        }

        public BankLogSnapshot CaptureCheckpoint(
            out string activeLogFileName,
            out long activeLogOffset,
            out string activeLogPrefixHash)
        {
            lock (_sync)
            {
                activeLogFileName = string.IsNullOrEmpty(_activePath)
                    ? string.Empty
                    : Path.GetFileName(_activePath);
                activeLogOffset = Math.Max(
                    0L,
                    _activeOffset - _partialLine.Length);
                string hash;
                activeLogPrefixHash =
                    !string.IsNullOrEmpty(_activePath) &&
                    TryComputePrefixHash(
                        _activePath,
                        (int)Math.Min(activeLogOffset, IdentityPrefixLength),
                        out hash)
                        ? hash
                        : string.Empty;
                return CurrentSnapshot;
            }
        }

        public void Start()
        {
            RefreshNow();
            lock (_sync)
            {
                if (_disposed || _timer != null)
                {
                    return;
                }

                if (Directory.Exists(_directory))
                {
                    _watcher = new FileSystemWatcher(_directory, "sbkernel*.log");
                    _watcher.NotifyFilter =
                        NotifyFilters.FileName |
                        NotifyFilters.LastWrite |
                        NotifyFilters.Size;
                    _watcher.Changed += OnFileChanged;
                    _watcher.Created += OnFileChanged;
                    _watcher.Renamed += OnFileRenamed;
                    _watcher.EnableRaisingEvents = true;
                }

                _timer = new Timer(
                    state => RefreshNow(),
                    null,
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(1));
            }
        }

        public void RefreshNow()
        {
            BankLogSnapshot changed = null;
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                BankLogSnapshot before = CurrentSnapshot;
                long parserActivityBefore = _parser.ActivityVersion;
                long sourceGenerationBefore = _sourceGeneration;
                try
                {
                    string[] files = GetNewestLogFiles();
                    if (files.Length == 0)
                    {
                        CurrentSnapshot = before.AsStale();
                    }
                    else if (string.IsNullOrEmpty(_activePath))
                    {
                        if (!TryResume(files))
                        {
                            Rebuild(files);
                        }
                    }
                    else
                    {
                        string newest = files[files.Length - 1];
                        if (!string.Equals(newest, _activePath, StringComparison.OrdinalIgnoreCase))
                        {
                            int direction = string.Compare(
                                Path.GetFileName(newest),
                                Path.GetFileName(_activePath),
                                StringComparison.OrdinalIgnoreCase);
                            if (direction > 0)
                            {
                                SwitchToNewFile(newest);
                                CurrentSnapshot = _parser.Snapshot;
                            }
                            else
                            {
                                CurrentSnapshot = before.AsStale();
                            }
                        }
                        else
                        {
                            AppendActiveFile();
                            CurrentSnapshot = _parser.Snapshot;
                        }
                    }

                    bool snapshotChanged =
                        !SnapshotEquals(before, CurrentSnapshot);
                    bool activityChanged =
                        parserActivityBefore != _parser.ActivityVersion ||
                        sourceGenerationBefore != _sourceGeneration;
                    if (snapshotChanged || activityChanged)
                    {
                        _revision++;
                    }

                    if (snapshotChanged)
                    {
                        changed = CurrentSnapshot;
                    }
                }
                catch (IOException exception)
                {
                    CurrentSnapshot = before.AsStale();
                    if (!SnapshotEquals(before, CurrentSnapshot))
                    {
                        _revision++;
                        changed = CurrentSnapshot;
                    }
                    LogUnavailable(exception);
                }
                catch (UnauthorizedAccessException exception)
                {
                    CurrentSnapshot = before.AsStale();
                    if (!SnapshotEquals(before, CurrentSnapshot))
                    {
                        _revision++;
                        changed = CurrentSnapshot;
                    }
                    LogUnavailable(exception);
                }
            }

            if (changed != null)
            {
                EventHandler<BankLogSnapshotEventArgs> handler = SnapshotChanged;
                if (handler != null)
                {
                    handler(this, new BankLogSnapshotEventArgs(changed));
                }
            }
        }

        public bool TryApplyAuthoritativeTotals(
            IReadOnlyDictionary<int, long> totals,
            long expectedRevision)
        {
            BankLogSnapshot changed;
            lock (_sync)
            {
                if (_disposed ||
                    _revision != expectedRevision ||
                    !CanApplyAuthoritativeSnapshot() ||
                    !_parser.TryReplaceTotals(totals))
                {
                    return false;
                }

                CurrentSnapshot = _parser.Snapshot;
                _revision++;
                changed = CurrentSnapshot;
            }

            EventHandler<BankLogSnapshotEventArgs> handler = SnapshotChanged;
            if (handler != null)
            {
                handler(this, new BankLogSnapshotEventArgs(changed));
            }

            return true;
        }

        private bool CanApplyAuthoritativeSnapshot()
        {
            if (CurrentSnapshot.IsStale ||
                CurrentSnapshot.HasPendingOperation ||
                string.IsNullOrEmpty(_activePath) ||
                _partialLine.Length > 0 ||
                !File.Exists(_activePath) ||
                !ActiveIdentityMatches())
            {
                return false;
            }

            try
            {
                string[] files = GetNewestLogFiles();
                if (files.Length == 0 ||
                    !string.Equals(
                        files[files.Length - 1],
                        _activePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return new FileInfo(_activePath).Length == _activeOffset;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                if (_watcher != null)
                {
                    _watcher.EnableRaisingEvents = false;
                    _watcher.Dispose();
                    _watcher = null;
                }

                if (_timer != null)
                {
                    _timer.Dispose();
                    _timer = null;
                }
            }
        }

        private string[] GetNewestLogFiles()
        {
            if (!Directory.Exists(_directory))
            {
                return new string[0];
            }

            return Directory
                .EnumerateFiles(_directory, "sbkernel*.log", SearchOption.TopDirectoryOnly)
                .Where(path => LogNamePattern.IsMatch(Path.GetFileName(path)))
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .TakeLastCompat(2)
                .ToArray();
        }

        private void Rebuild(IEnumerable<string> files)
        {
            _sourceGeneration++;
            _parser = new BankLogParser(_expectedDepartments);
            _partialLine = string.Empty;
            _activePath = null;
            _activeOffset = 0L;
            _activePrefixLength = 0;
            _activePrefixHash = string.Empty;

            string[] selected = files.ToArray();
            for (int index = 0; index < selected.Length; index++)
            {
                bool isActive = index == selected.Length - 1;
                _activePath = selected[index];
                _activeOffset = 0L;
                ReadNewBytes(isActive);
                if (!isActive && _partialLine.Length > 0)
                {
                    _parser.ProcessLine(_partialLine);
                    _partialLine = string.Empty;
                }
            }

            CurrentSnapshot = _parser.Snapshot;
        }

        private bool TryResume(string[] files)
        {
            if (_fallback == null ||
                _fallback.Totals.Count == 0 ||
                string.IsNullOrWhiteSpace(_resumeLogFileName) ||
                _resumeLogOffset < 0 ||
                string.IsNullOrWhiteSpace(_resumeLogPrefixHash))
            {
                return false;
            }

            int resumeIndex = Array.FindIndex(
                files,
                path => string.Equals(
                    Path.GetFileName(path),
                    _resumeLogFileName,
                    StringComparison.OrdinalIgnoreCase));
            if (resumeIndex < 0)
            {
                return false;
            }

            var info = new FileInfo(files[resumeIndex]);
            if (_resumeLogOffset > info.Length)
            {
                return false;
            }

            string currentPrefixHash;
            if (!TryComputePrefixHash(
                    files[resumeIndex],
                    (int)Math.Min(_resumeLogOffset, IdentityPrefixLength),
                    out currentPrefixHash) ||
                !string.Equals(
                    currentPrefixHash,
                    _resumeLogPrefixHash,
                    StringComparison.Ordinal))
            {
                return false;
            }

            var resumedParser = new BankLogParser(_expectedDepartments);
            if (!resumedParser.TryReplaceTotals(_fallback.Totals))
            {
                return false;
            }

            _parser = resumedParser;
            _sourceGeneration++;
            _partialLine = string.Empty;
            _activePath = files[resumeIndex];
            _activeOffset = _resumeLogOffset;

            for (int index = resumeIndex; index < files.Length; index++)
            {
                if (index > resumeIndex)
                {
                    if (_partialLine.Length > 0)
                    {
                        _parser.ProcessLine(_partialLine);
                        _partialLine = string.Empty;
                    }

                    _activePath = files[index];
                    _activeOffset = 0L;
                }

                ReadNewBytes(index == files.Length - 1);
            }

            CurrentSnapshot = _parser.Snapshot;
            return true;
        }

        private void SwitchToNewFile(string path)
        {
            if (_partialLine.Length > 0)
            {
                _parser.ProcessLine(_partialLine);
            }

            _partialLine = string.Empty;
            _sourceGeneration++;
            _activePath = path;
            _activeOffset = 0L;
            ReadNewBytes(true);
        }

        private void AppendActiveFile()
        {
            var info = new FileInfo(_activePath);
            if (info.Length < _activeOffset)
            {
                Rebuild(GetNewestLogFiles());
                return;
            }

            if (!ActiveIdentityMatches())
            {
                Rebuild(GetNewestLogFiles());
                return;
            }

            if (info.Length > _activeOffset)
            {
                ReadNewBytes(true);
            }
        }

        private void ReadNewBytes(bool keepPartial)
        {
            using (var stream = new FileStream(
                _activePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                stream.Position = Math.Min(_activeOffset, stream.Length);
                var buffer = new byte[64 * 1024];
                var text = new StringBuilder(_partialLine);
                _partialLine = string.Empty;

                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    text.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }

                _activeOffset = stream.Position;
                string[] lines = text.ToString().Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                int completeCount = lines.Length;
                if (keepPartial && !text.ToString().EndsWith("\n", StringComparison.Ordinal))
                {
                    completeCount--;
                    _partialLine = lines[lines.Length - 1];
                }

                for (int index = 0; index < completeCount; index++)
                {
                    _parser.ProcessLine(lines[index]);
                }

                UpdateActiveIdentity();
            }
        }

        private bool ActiveIdentityMatches()
        {
            if (string.IsNullOrEmpty(_activePath) ||
                string.IsNullOrEmpty(_activePrefixHash))
            {
                return false;
            }

            string currentHash;
            return TryComputePrefixHash(
                       _activePath,
                       _activePrefixLength,
                       out currentHash) &&
                   string.Equals(
                       currentHash,
                       _activePrefixHash,
                       StringComparison.Ordinal);
        }

        private void UpdateActiveIdentity()
        {
            int length = (int)Math.Min(_activeOffset, IdentityPrefixLength);
            string hash;
            if (TryComputePrefixHash(_activePath, length, out hash))
            {
                _activePrefixLength = length;
                _activePrefixHash = hash;
            }
        }

        private static bool TryComputePrefixHash(
            string path,
            int length,
            out string hash)
        {
            hash = string.Empty;
            if (length < 0)
            {
                return false;
            }

            try
            {
                var bytes = new byte[length];
                using (var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                {
                    if (stream.Length < length)
                    {
                        return false;
                    }

                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int read = stream.Read(bytes, offset, bytes.Length - offset);
                        if (read == 0)
                        {
                            return false;
                        }

                        offset += read;
                    }
                }

                using (SHA256 algorithm = SHA256.Create())
                {
                    hash = Convert.ToBase64String(algorithm.ComputeHash(bytes));
                    return true;
                }
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private void OnFileChanged(object sender, FileSystemEventArgs eventArgs)
        {
            ThreadPool.QueueUserWorkItem(state => RefreshNow());
        }

        private void OnFileRenamed(object sender, RenamedEventArgs eventArgs)
        {
            ThreadPool.QueueUserWorkItem(state => RefreshNow());
        }

        private void LogUnavailable(Exception exception)
        {
            if (_logger != null)
            {
                _logger.Write(
                    SafeLogEvent.LogMonitorUnavailable,
                    _directory,
                    exception);
            }
        }

        private static bool SnapshotEquals(BankLogSnapshot left, BankLogSnapshot right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null ||
                right == null ||
                left.IsStale != right.IsStale ||
                left.HasPendingOperation != right.HasPendingOperation ||
                left.Totals.Count != right.Totals.Count)
            {
                return false;
            }

            foreach (KeyValuePair<int, long> item in left.Totals)
            {
                long value;
                if (!right.Totals.TryGetValue(item.Key, out value) || value != item.Value)
                {
                    return false;
                }
            }

            return true;
        }
    }

    internal static class EnumerableCompat
    {
        public static IEnumerable<T> TakeLastCompat<T>(
            this IEnumerable<T> source,
            int count)
        {
            var queue = new Queue<T>();
            foreach (T item in source)
            {
                queue.Enqueue(item);
                if (queue.Count > count)
                {
                    queue.Dequeue();
                }
            }

            return queue;
        }
    }
}
