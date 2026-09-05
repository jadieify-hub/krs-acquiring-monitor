using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Krs.AcquiringMonitor.Core.Reports
{
    public static class StatisticsReportParser
    {
        private static readonly Regex MoneyPattern = new Regex(
            @"(?<!\d)(?<value>-?(?:\d{1,3}(?:[ \u00A0]\d{3})+|\d+)[,.]\d{2})(?!\d)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex SummaryHeaderPattern = new Regex(
            @"^\s*Количество\s+(?<kind>оплат|отмен|возвратов)\s*:\s*(?<count>\d+)\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex SummaryAmountPattern = new Regex(
            @"^\s*На сумму:\s*" + MoneyPattern.ToString() + @"\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public static IReadOnlyList<OrganizationReport> Parse(string reportText)
        {
            if (string.IsNullOrWhiteSpace(reportText))
            {
                return new OrganizationReport[0];
            }

            var reports = new List<OrganizationReport>();
            Section current = null;
            string[] lines = reportText
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n');

            foreach (string line in lines)
            {
                string legalName;
                if (OrganizationNameShortener.TryExtractLegalName(line, out legalName))
                {
                    if (IsSberbankHeader(legalName))
                    {
                        continue;
                    }

                    if (!TryAddCompletedSection(current, reports))
                    {
                        return new OrganizationReport[0];
                    }

                    current = new Section(legalName);
                }
                else if (current != null)
                {
                    current.Lines.Add(line ?? string.Empty);
                }
            }

            if (!TryAddCompletedSection(current, reports))
            {
                return new OrganizationReport[0];
            }

            return reports.AsReadOnly();
        }

        private static bool TryAddCompletedSection(
            Section section,
            ICollection<OrganizationReport> reports)
        {
            if (section == null)
            {
                return true;
            }

            long total;
            if (!TryGetTotal(section.Lines, out total))
            {
                return false;
            }

            reports.Add(
                new OrganizationReport(
                    section.RawName,
                    OrganizationNameShortener.Shorten(section.RawName),
                    total));
            return true;
        }

        private static bool IsSberbankHeader(string legalName)
        {
            return OrganizationNameShortener.Normalize(legalName)
                .StartsWith("ПАО СБЕРБАНК", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetTotal(IEnumerable<string> lines, out long total)
        {
            List<string> allLines = lines.ToList();
            List<string> explicitTotalLines = allLines
                .Where(line => Contains(line, "ИТОГО"))
                .ToList();
            if (explicitTotalLines.Count == 1 &&
                TryReadSingleAmount(explicitTotalLines[0], out total))
            {
                return true;
            }

            if (explicitTotalLines.Count > 0)
            {
                total = 0L;
                return false;
            }

            if (allLines.Any(line => SummaryHeaderPattern.IsMatch(line)))
            {
                return TryGetControlTapeTotal(allLines, out total);
            }

            List<string> purchaseLines = allLines
                .Where(line => Contains(line, "ОПЛАТ") || Contains(line, "ПОКУП"))
                .ToList();
            List<string> refundLines = allLines
                .Where(line => Contains(line, "ВОЗВРАТ"))
                .ToList();
            long purchase;
            if (purchaseLines.Count != 1 ||
                refundLines.Count > 1 ||
                !TryReadSingleAmount(purchaseLines[0], out purchase))
            {
                total = 0L;
                return false;
            }

            long refund = 0L;
            if (refundLines.Count == 1)
            {
                if (!TryReadSingleAmount(refundLines[0], out refund))
                {
                    total = 0L;
                    return false;
                }

                refund = Math.Abs(refund);
            }

            total = checked(Math.Abs(purchase) - refund);
            return true;
        }

        private static bool TryGetControlTapeTotal(IReadOnlyList<string> lines, out long total)
        {
            total = 0L;
            var amounts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < lines.Count; index++)
            {
                if (!Contains(lines[index], "Количество"))
                {
                    continue;
                }

                Match header = SummaryHeaderPattern.Match(lines[index]);
                int count;
                if (!header.Success || !int.TryParse(
                    header.Groups["count"].Value, NumberStyles.None,
                    CultureInfo.InvariantCulture, out count))
                {
                    return false;
                }

                string kind = header.Groups["kind"].Value;
                do
                {
                    index++;
                }
                while (index < lines.Count && string.IsNullOrWhiteSpace(lines[index]));

                long amount;
                if (index >= lines.Count ||
                    !SummaryAmountPattern.IsMatch(lines[index]) ||
                    !TryReadSingleAmount(lines[index], out amount) || amount < 0L ||
                    (count == 0 && amount != 0L) || amounts.ContainsKey(kind))
                {
                    return false;
                }

                // ponytail: ненулевые отмены требуют проверенного примера их учёта в ленте.
                if (string.Equals(kind, "отмен", StringComparison.OrdinalIgnoreCase) && count != 0)
                {
                    return false;
                }

                amounts.Add(kind, amount);
            }

            if (amounts.Count != 3)
            {
                return false;
            }

            total = checked(amounts["оплат"] - amounts["возвратов"]);
            return true;
        }

        private static bool TryReadSingleAmount(string line, out long amountKopeks)
        {
            MatchCollection matches = MoneyPattern.Matches(line ?? string.Empty);
            if (matches.Count != 1)
            {
                amountKopeks = 0L;
                return false;
            }

            string value = matches[0].Groups["value"].Value
                .Replace(" ", string.Empty)
                .Replace("\u00A0", string.Empty)
                .Replace(',', '.');

            decimal amount;
            if (!decimal.TryParse(
                    value,
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out amount))
            {
                amountKopeks = 0L;
                return false;
            }

            decimal kopeks = amount * 100m;
            if (decimal.Truncate(kopeks) != kopeks ||
                kopeks > long.MaxValue ||
                kopeks < long.MinValue)
            {
                amountKopeks = 0L;
                return false;
            }

            amountKopeks = decimal.ToInt64(kopeks);
            return true;
        }

        private static bool Contains(string source, string value)
        {
            return (source ?? string.Empty).IndexOf(
                       value,
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private sealed class Section
        {
            public Section(string rawName)
            {
                RawName = rawName;
                Lines = new List<string>();
            }

            public string RawName { get; private set; }

            public List<string> Lines { get; private set; }
        }
    }
}
