using System;
using System.Linq;
using Krs.AcquiringMonitor.Configuration;
using Krs.AcquiringMonitor.Core.Monitoring;

namespace Krs.AcquiringMonitor.Terminal
{
    public sealed class AutomaticNameRefreshPolicy
    {
        private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(10);
        private DateTimeOffset _nextAttempt;

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
    }
}
