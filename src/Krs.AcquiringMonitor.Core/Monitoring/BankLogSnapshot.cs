using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Krs.AcquiringMonitor.Core.Monitoring
{
    public sealed class BankLogSnapshot
    {
        internal BankLogSnapshot(
            IDictionary<int, long> totals,
            bool isStale,
            bool hasPendingOperation)
        {
            Totals = new ReadOnlyDictionary<int, long>(
                new Dictionary<int, long>(totals));
            Departments = totals.Keys.OrderBy(value => value).ToArray();
            IsStale = isStale;
            HasPendingOperation = hasPendingOperation;
        }

        public IReadOnlyDictionary<int, long> Totals { get; private set; }

        public IReadOnlyList<int> Departments { get; private set; }

        public bool IsStale { get; private set; }

        public bool HasPendingOperation { get; private set; }

        public static BankLogSnapshot FromTotals(
            IDictionary<int, long> totals,
            bool isStale)
        {
            return new BankLogSnapshot(
                totals ?? new Dictionary<int, long>(),
                isStale,
                false);
        }

        public BankLogSnapshot AsStale()
        {
            return new BankLogSnapshot(
                Totals.ToDictionary(item => item.Key, item => item.Value),
                true,
                HasPendingOperation);
        }
    }
}
