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

        private static readonly Regex CloseCommandPattern = new Regex(
            @"Command\s*=\s*(?<command>6000|7000)\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly Dictionary<int, long> _totals = new Dictionary<int, long>();
        private readonly HashSet<int> _expectedDepartments;
        private PendingOperation _pendingOperation;
        private bool _pendingClose;
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
                // Another program writes this journal, so a corrupted or torn line must be
                // skipped here: ProcessLine runs on a timer thread where a throw kills the app.
                int transactionType;
                long amountKopeks;
                if (!TryParseInt(start.Groups["type"].Value, out transactionType) ||
                    !TryParseLong(start.Groups["amount"].Value, out amountKopeks))
                {
                    return;
                }

                ActivityVersion++;
                _pendingOperation = new PendingOperation(transactionType, amountKopeks);
                _pendingClose = false;
                return;
            }

            // close_day also wraps statistics (7000); only 6000 starts settlement. A 7000
            // ends the wait without settling anything: the terminal has answered and is idle
            // again, so leaving the wait set would block refresh, naming and updates until
            // the next card transaction happened to clear it.
            Match closeCommand = CloseCommandPattern.Match(line);
            if (closeCommand.Success)
            {
                if (closeCommand.Groups["command"].Value != "6000")
                {
                    _pendingClose = false;
                    return;
                }

                ActivityVersion++;
                _pendingOperation = null;
                _pendingClose = true;
                return;
            }

            Match command = CommandPattern.Match(line);
            if (command.Success && _pendingOperation != null)
            {
                int department;
                int commandCode;
                if (!TryParseInt(command.Groups["department"].Value, out department) ||
                    !TryParseInt(command.Groups["command"].Value, out commandCode))
                {
                    return;
                }

                _pendingOperation.Department = department;
                _pendingOperation.CommandMatchesType =
                    (_pendingOperation.TransactionType == 1 && commandCode == 4000) ||
                    (_pendingOperation.TransactionType == 3 && commandCode == 4002);
                EnsureDepartment(department);
                return;
            }

            Match result = ResultPattern.Match(line);
            if (result.Success && _pendingOperation != null)
            {
                int resultCode;
                if (!TryParseInt(result.Groups["result"].Value, out resultCode))
                {
                    // An unreadable result must not leave the operation pending for the rest
                    // of the shift, because that blocks refresh, naming and updates.
                    _pendingOperation = null;
                    return;
                }

                CompleteOperation(resultCode, result.Groups["rc"].Value);
                return;
            }

            Match closeResult = CloseResultPattern.Match(line);
            if (closeResult.Success && _pendingClose)
            {
                int closeCode;
                if (!TryParseInt(closeResult.Groups["result"].Value, out closeCode))
                {
                    _pendingClose = false;
                    return;
                }

                CompleteClose(closeCode, closeResult.Groups["rc"].Value);
            }
        }

        public bool TryReplaceTotals(IReadOnlyDictionary<int, long> totals)
        {
            if (totals == null ||
                totals.Count < 1 ||
                totals.Count > 2 ||
                HasPendingOperation)
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
            long current = _totals[operation.Department];
            // Amount comes from the journal unvalidated, so an absurd value is dropped rather
            // than allowed to overflow the running total.
            long signed = direction * operation.AmountKopeks;
            if (signed > 0
                    ? current > long.MaxValue - signed
                    : current < long.MinValue - signed)
            {
                return;
            }

            _totals[operation.Department] = current + signed;
        }

        private void CompleteClose(int result, string responseCode)
        {
            _pendingClose = false;

            if (result != 0 || !IsSuccessCode(responseCode))
            {
                return;
            }

            if (_totals.Count == 0 && _expectedDepartments.Count == 0)
            {
                _isStale = true;
                return;
            }

            // UPOS settlement (6000) closes all departments in one successful call.
            var departments = new HashSet<int>(_expectedDepartments);
            foreach (int department in _totals.Keys)
            {
                departments.Add(department);
            }

            foreach (int department in departments)
            {
                _totals[department] = 0L;
            }

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

        private static bool TryParseInt(string value, out int result)
        {
            return int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out result);
        }

        private static bool TryParseLong(string value, out long result)
        {
            return long.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out result);
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
