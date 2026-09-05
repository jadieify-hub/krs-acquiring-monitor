using System.Collections.Generic;
using System.Linq;

namespace Krs.AcquiringMonitor.Core.Reports
{
    public static class ExpectedDepartmentResolver
    {
        public static IReadOnlyList<int> Resolve(
            IEnumerable<int> knownDepartments,
            int reportCount)
        {
            int[] known = (knownDepartments ?? new int[0])
                .Where(value => value > 0)
                .Distinct()
                .OrderBy(value => value)
                .Take(2)
                .ToArray();

            if (known.Length == 0)
            {
                return reportCount == 2
                    ? new[] { 1, 2 }
                    : new int[0];
            }

            if (reportCount == 2 &&
                known.Length == 1 &&
                (known[0] == 1 || known[0] == 2))
            {
                return new[] { 1, 2 };
            }

            return known;
        }
    }
}
