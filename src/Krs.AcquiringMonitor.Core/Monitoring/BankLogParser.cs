using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Krs.AcquiringMonitor.Core.Monitoring
{
    public sealed class BankLogParser
    {
        private static readonly Regex StartPattern = new Regex(
            @"card_authorize14:.*TRType=(?<type>\d+).*Amount=(?<amount>\d+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex CommandPattern = new Regex(
            @"Command\s*=\s*(?<command>4000|4002).*Department\s*=\s*(?<department>\d+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex ResultPattern = new Regex(
            @"card_authorize14:\s*result=(?<result>-?\d+),\s*RC=(?<rc>[^,\s]+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex CloseResultPattern = new Regex(
            @"close_day:\s*result=(?<result>-?\d+),\s*RC=(?<rc>[^,\s]+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly Dictionary<int, long> _totals = new Dictionary<int, long>();
        private readonly HashSet<int> _expectedDepartments;
        private PendingOperation _pendingOperation;
        private bool _pendingClose;
        private int _successfulCloses;
        private bool _isStale;

        public BankLogParser()
            : this(null)
        {
        }

        public BankLogParser(IEnumerable<int> expectedDepartments)
        {
            _expectedDepartments = new HashSet<int>();
            if (expectedDepartments == null)
            {
                return;
            }

            foreach (int department in expectedDepartments)
            {
                if (department > 0)
                {
                    _expectedDepartments.Add(department);
                }
            }
        }

        public BankLogSnapshot Snapshot
        {
            get
            {
                return new BankLogSnapshot(
                    _totals,
                    _isStale,
                    HasPendingOperation);
            }
        }

        public bool HasPendingOperation
        {
            get { return _pendingOperation != null || _pendingClose; }
        }

        public long ActivityVersion { get; private set; }

        public void ProcessLine(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            Match start = StartPattern.Match(line);
            if (start.Success)
            {
                ActivityVersion++;
                _pendingOperation = new PendingOperation(
                    ParseInt(start.Groups["type"].Value),
                    ParseLong(start.Groups["amount"].Value));
                _pendingClose = false;
                _successfulCloses = 0;
                return;
            }

            if (line.IndexOf("PILOT: close_day.", StringComparison.Ordinal) >= 0)
            {
                ActivityVersion++;
                _pendingOperation = null;
                _pendingClose = true;
                return;
            }

            Match command = CommandPattern.Match(line);
            if (command.Success && _pendingOperation != null)
            {
                int department = ParseInt(command.Groups["department"].Value);
                int commandCode = ParseInt(command.Groups["command"].Value);
                _pendingOperation.Department = department;
                _pendingOperation.CommandMatchesType =
                    (_pendingOperation.TransactionType == 1 && commandCode == 4000) ||
                    (_pendingOperation.TransactionType == 3 && commandCode == 4002);
                EnsureDepartment(department);
                return;
            }

            if (_pendingClose &&
                line.IndexOf("Command = 6000", StringComparison.Ordinal) >= 0)
            {
                return;
            }

            Match result = ResultPattern.Match(line);
            if (result.Success && _pendingOperation != null)
            {
                CompleteOperation(
                    ParseInt(result.Groups["result"].Value),
                    result.Groups["rc"].Value);
                return;
            }

            Match closeResult = CloseResultPattern.Match(line);
            if (closeResult.Success && _pendingClose)
            {
                CompleteClose(
                    ParseInt(closeResult.Groups["result"].Value),
                    closeResult.Groups["rc"].Value);
            }
        }

        public bool TryReplaceTotals(IReadOnlyDictionary<int, long> totals)
        {
            if (totals == null ||
                totals.Count < 1 ||
                totals.Count > 2 ||
                HasPendingOperation ||
                _successfulCloses > 0)
            {
                return false;
            }

            foreach (KeyValuePair<int, long> item in totals)
            {
                if (item.Key <= 0)
                {
                    return false;
                }
            }

            if (_totals.Count > 0)
            {
                if (_totals.Count > totals.Count)
                {
                    return false;
                }

                foreach (int department in _totals.Keys)
                {
                    if (!totals.ContainsKey(department))
                    {
                        return false;
                    }
                }
            }

            _totals.Clear();
            foreach (KeyValuePair<int, long> item in totals)
            {
                _totals.Add(item.Key, item.Value);
            }

            _successfulCloses = 0;
            _isStale = false;
            return true;
        }

        private void CompleteOperation(int result, string responseCode)
        {
            PendingOperation operation = _pendingOperation;
            _pendingOperation = null;

            if (operation.Department <= 0 || !operation.CommandMatchesType)
            {
                return;
            }

            if (result != 0 || !IsSuccessCode(responseCode))
            {
                return;
            }

            long direction = operation.TransactionType == 3 ? -1L : 1L;
            _totals[operation.Department] =
                checked(_totals[operation.Department] + direction * operation.AmountKopeks);
        }

        private void CompleteClose(int result, string responseCode)
        {
            _pendingClose = false;

            if (result != 0 || !IsSuccessCode(responseCode))
            {
                _isStale = _successfulCloses > 0;
                return;
            }

            int requiredCloses = Math.Max(
                _totals.Count,
                _expectedDepartments.Count);
            if (requiredCloses == 0)
            {
                _isStale = true;
                return;
            }

            _successfulCloses++;
            if (_successfulCloses < requiredCloses)
            {
                _isStale = true;
                return;
            }

            var departments = new HashSet<int>(_expectedDepartments);
            foreach (int department in _totals.Keys)
            {
                departments.Add(department);
            }

            foreach (int department in departments)
            {
                _totals[department] = 0L;
            }

            _successfulCloses = 0;
            _isStale = false;
        }

        private void EnsureDepartment(int department)
        {
            if (department > 0 && !_totals.ContainsKey(department))
            {
                _totals.Add(department, 0L);
            }
        }

        private static bool IsSuccessCode(string value)
        {
            return string.Equals(value, "0", StringComparison.Ordinal) ||
                   string.Equals(value, "00", StringComparison.Ordinal);
        }

        private static int ParseInt(string value)
        {
            return int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private static long ParseLong(string value)
        {
            return long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private sealed class PendingOperation
        {
            public PendingOperation(int transactionType, long amountKopeks)
            {
                TransactionType = transactionType;
                AmountKopeks = amountKopeks;
            }

            public int TransactionType { get; private set; }

            public long AmountKopeks { get; private set; }

            public int Department { get; set; }

            public bool CommandMatchesType { get; set; }
        }
    }
}
