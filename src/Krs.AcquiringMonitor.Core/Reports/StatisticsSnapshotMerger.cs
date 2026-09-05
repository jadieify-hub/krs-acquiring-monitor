using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Krs.AcquiringMonitor.Core.Monitoring;

namespace Krs.AcquiringMonitor.Core.Reports
{
    public static class StatisticsSnapshotMerger
    {
        public static bool TryMerge(
            IReadOnlyList<int> expectedDepartments,
            IReadOnlyDictionary<int, string> knownBankNames,
            IReadOnlyList<OrganizationReport> reports,
            out IReadOnlyDictionary<int, DepartmentTotal> merged)
        {
            merged = null;
            if (expectedDepartments == null ||
                knownBankNames == null ||
                reports == null)
            {
                return false;
            }

            int[] departments = expectedDepartments
                .Where(value => value > 0)
                .Distinct()
                .OrderBy(value => value)
                .ToArray();

            if (departments.Length < 1 ||
                departments.Length > 2 ||
                reports.Count != departments.Length)
            {
                return false;
            }

            if (reports.Any(report => report == null || string.IsNullOrWhiteSpace(report.ShortName)) ||
                reports.Select(report => report.ShortName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() != reports.Count)
            {
                return false;
            }

            var assignments = new Dictionary<int, OrganizationReport>();
            var unusedReports = new List<OrganizationReport>(reports);

            foreach (int department in departments)
            {
                string knownBankName;
                if (!knownBankNames.TryGetValue(department, out knownBankName) ||
                    string.IsNullOrWhiteSpace(knownBankName))
                {
                    continue;
                }

                string shortConfigured = OrganizationNameShortener.Shorten(knownBankName);
                OrganizationReport match = unusedReports.SingleOrDefault(
                    report => string.Equals(
                        report.ShortName,
                        shortConfigured,
                        StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    assignments.Add(department, match);
                    unusedReports.Remove(match);
                }
            }

            var remainingDepartments = departments
                .Where(department => !assignments.ContainsKey(department))
                .ToArray();
            if (remainingDepartments.Length != unusedReports.Count)
            {
                return false;
            }

            for (int index = 0; index < remainingDepartments.Length; index++)
            {
                assignments.Add(remainingDepartments[index], unusedReports[index]);
            }

            var values = new Dictionary<int, DepartmentTotal>();
            foreach (int department in departments)
            {
                OrganizationReport report = assignments[department];
                values.Add(
                    department,
                    new DepartmentTotal(
                        department,
                        report.TotalKopeks,
                        report.ShortName,
                        true,
                        false));
            }

            merged = new ReadOnlyDictionary<int, DepartmentTotal>(values);
            return true;
        }
    }
}
