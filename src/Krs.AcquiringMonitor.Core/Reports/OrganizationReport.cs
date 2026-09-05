namespace Krs.AcquiringMonitor.Core.Reports
{
    public sealed class OrganizationReport
    {
        public OrganizationReport(string rawName, string shortName, long totalKopeks)
        {
            RawName = rawName ?? string.Empty;
            ShortName = shortName ?? string.Empty;
            TotalKopeks = totalKopeks;
        }

        public string RawName { get; private set; }

        public string ShortName { get; private set; }

        public long TotalKopeks { get; private set; }
    }
}
