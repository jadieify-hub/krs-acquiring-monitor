using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Krs.AcquiringMonitor.Core.Monitoring;

namespace Krs.AcquiringMonitor.UI
{
    public sealed class OverlayRow
    {
        public OverlayRow(
            int department,
            string organizationName,
            string amountText,
            bool isStale)
        {
            Department = department;
            OrganizationName = organizationName;
            AmountText = amountText;
            IsStale = isStale;
        }

        public int Department { get; private set; }

        public string OrganizationName { get; private set; }

        public string AmountText { get; private set; }

        public bool IsStale { get; private set; }
    }

    public static class OverlayPresentation
    {
        private static readonly CultureInfo RussianCulture =
            CultureInfo.GetCultureInfo("ru-RU");

        public static string FormatAmount(long amountKopeks)
        {
            return (amountKopeks / 100m)
                .ToString("N2", RussianCulture)
                .Replace('\u00A0', ' ')
                .Replace('\u202F', ' ') + " ₽";
        }

        public static IReadOnlyList<OverlayRow> BuildRows(
            BankLogSnapshot snapshot,
            IReadOnlyDictionary<int, string> configuredNames)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException("snapshot");
            }

            configuredNames = configuredNames ??
                              new Dictionary<int, string>();

            int[] departments = snapshot.Departments
                .Concat(configuredNames.Keys)
                .Where(value => value > 0)
                .Distinct()
                .OrderBy(value => value)
                .Take(2)
                .ToArray();
            if (departments.Length == 0)
            {
                departments = new[] { 1 };
            }

            var rows = new List<OverlayRow>();
            foreach (int department in departments)
            {
                long amount;
                bool isKnown = snapshot.Totals.TryGetValue(department, out amount);
                string configuredName;
                string name =
                    configuredNames.TryGetValue(department, out configuredName) &&
                    !string.IsNullOrWhiteSpace(configuredName)
                        ? configuredName.Trim()
                        : departments.Length == 1
                            ? "Организация"
                            : "Организация " + department.ToString(CultureInfo.InvariantCulture);

                rows.Add(
                    new OverlayRow(
                        department,
                        name,
                        isKnown ? FormatAmount(amount) : "—",
                        snapshot.IsStale || !isKnown));
            }

            return rows.AsReadOnly();
        }
    }
}
