namespace Krs.AcquiringMonitor.Core.Monitoring
{
    public sealed class DepartmentTotal
    {
        public DepartmentTotal(
            int department,
            long amountKopeks,
            string organizationName,
            bool isKnown,
            bool isStale)
        {
            Department = department;
            AmountKopeks = amountKopeks;
            OrganizationName = organizationName ?? string.Empty;
            IsKnown = isKnown;
            IsStale = isStale;
        }

        public int Department { get; private set; }

        public long AmountKopeks { get; private set; }

        public string OrganizationName { get; private set; }

        public bool IsKnown { get; private set; }

        public bool IsStale { get; private set; }
    }
}
