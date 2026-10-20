using System;
using System.Linq;
using Krs.AcquiringMonitor.Configuration;
using Krs.AcquiringMonitor.Core.Monitoring;

namespace Krs.AcquiringMonitor.Terminal
{
    public sealed class AutomaticNameRefreshPolicy
    {
        // A statistics report lists every organization configured on the terminal, not only
        // those with transactions, so one accepted report leaves nothing to ask about again.
        // Without that stop and this cap the monitor calls the bank DLL every ten minutes for
        // the rest of the shift whenever a name cannot be filled in at all.
        private const int MaximumFailures = 3;
        private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(10);
        private DateTimeOffset _nextAttempt;
        private int _failures;
        private bool _accepted;

        public AutomaticNameRefreshPolicy(DateTimeOffset firstAttempt)
        {
            _nextAttempt = firstAttempt;
        }

        public bool ShouldAttempt(
            AppSettings settings,
            BankLogSnapshot snapshot,
            DateTimeOffset now)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            if (snapshot == null)
            {
                throw new ArgumentNullException("snapshot");
            }

            if (_accepted || _failures >= MaximumFailures)
            {
                return false;
            }

            if (now < _nextAttempt)
            {
                return false;
            }

            int[] departments = snapshot.Departments
                .Concat(
                    settings.Organizations
                        .Where(item => item != null && item.Department > 0)
                        .Select(item => item.Department))
                .Where(value => value > 0)
                .Distinct()
                .OrderBy(value => value)
                .Take(2)
                .ToArray();
            var names = settings.GetBankOrganizationNames();
            return departments.Any(
                department =>
                {
                    string name;
                    return !names.TryGetValue(department, out name) ||
                           string.IsNullOrWhiteSpace(name);
                });
        }

        public void RecordAttempt(DateTimeOffset now)
        {
            _nextAttempt = now.Add(RetryInterval);
        }

        public void RecordAccepted()
        {
            _accepted = true;
        }

        // Only automatic attempts are counted: a manual refresh is the escape hatch the cap
        // leaves open, so it must not be able to close it.
        public void RecordFailure()
        {
            _failures++;
        }
    }
}
