using System.Collections.Generic;
using Krs.AcquiringMonitor.Core.Monitoring;
using Krs.AcquiringMonitor.Core.Reports;

namespace Krs.AcquiringMonitor.Tests
{
    internal static class StatisticsReportParserTests
    {
        public static void ShortensEntrepreneurName()
        {
            TestAssert.Equal(
                "ИП Иванов",
                OrganizationNameShortener.Shorten("ИП Иванов Иван Иванович"));
        }

        public static void ShortensCompanyName()
        {
            TestAssert.Equal(
                "ООО Колокольчик",
                OrganizationNameShortener.Shorten("ООО «Колокольчик»"));
        }

        public static void ParsesSingleOrganizationTotal()
        {
            string report =
                "СБЕРБАНК\r\n" +
                "ООО «Колокольчик»\r\n" +
                "ОПЛАТА                 12 345,67\r\n" +
                "ВОЗВРАТ                 1 100,50\r\n" +
                "ИТОГО                   11 245,17\r\n";

            IReadOnlyList<OrganizationReport> parsed = StatisticsReportParser.Parse(report);

            TestAssert.Equal(1, parsed.Count);
            TestAssert.Equal("ООО Колокольчик", parsed[0].ShortName);
            TestAssert.Equal(1124517L, parsed[0].TotalKopeks);
        }

        public static void ParsesTwoOrganizations()
        {
            string report =
                "ИП Иванов Иван Иванович\n" +
                "ИТОГО 1 250.00\n" +
                "ООО \"Колокольчик\"\n" +
                "ИТОГО 8 900,00\n";

            IReadOnlyList<OrganizationReport> parsed = StatisticsReportParser.Parse(report);

            TestAssert.Equal(2, parsed.Count);
            TestAssert.Equal("ИП Иванов", parsed[0].ShortName);
            TestAssert.Equal(125000L, parsed[0].TotalKopeks);
            TestAssert.Equal("ООО Колокольчик", parsed[1].ShortName);
            TestAssert.Equal(890000L, parsed[1].TotalKopeks);
        }

        public static void CalculatesTotalWhenExplicitTotalIsMissing()
        {
            string report =
                "ОРГАНИЗАЦИЯ: ИП Иванов Иван Иванович\n" +
                "ПОКУПКА 9 000,00\n" +
                "ВОЗВРАТ 125,50\n";

            IReadOnlyList<OrganizationReport> parsed = StatisticsReportParser.Parse(report);

            TestAssert.Equal(1, parsed.Count);
            TestAssert.Equal(887450L, parsed[0].TotalKopeks);
        }

        public static void RejectsConflictingTotals()
        {
            string report =
                "ООО Колокольчик\n" +
                "ИТОГО 10 000,00\n" +
                "ИТОГО 9 000,00\n";

            IReadOnlyList<OrganizationReport> parsed = StatisticsReportParser.Parse(report);

            TestAssert.Equal(0, parsed.Count);
        }

        public static void RejectsWholeReportWhenASectionIsInvalid()
        {
            string report =
                "ИП Иванов Иван Иванович\n" +
                "ИТОГО 1 250,00\n" +
                "ООО Колокольчик\n" +
                "ИТОГО сумма недоступна\n";

            IReadOnlyList<OrganizationReport> parsed =
                StatisticsReportParser.Parse(report);

            TestAssert.Equal(0, parsed.Count);
        }

        public static void IgnoresSberbankLegalHeader()
        {
            string report =
                "ПАО СБЕРБАНК Текущие итоги\n" +
                "Отдел: ИП Иванов Иван Иванович\n" +
                "ИТОГО 1 250,00\n";

            IReadOnlyList<OrganizationReport> parsed =
                StatisticsReportParser.Parse(report);

            TestAssert.Equal(1, parsed.Count);
            TestAssert.Equal("ИП Иванов", parsed[0].ShortName);
        }

        public static void RejectsTotalLineWithTwoAmounts()
        {
            string report =
                "ООО Колокольчик\n" +
                "ИТОГО 1 250,00 1 300,00\n";

            TestAssert.Equal(0, StatisticsReportParser.Parse(report).Count);
        }

        public static void RejectsRepeatedEqualTotalLines()
        {
            string report =
                "ООО Колокольчик\n" +
                "ИТОГО 1 250,00\n" +
                "ИТОГО 1 250,00\n";

            TestAssert.Equal(0, StatisticsReportParser.Parse(report).Count);
        }

        public static void ReturnsDetectedNameForLearning()
        {
            IReadOnlyDictionary<int, DepartmentTotal> merged;
            bool applied = StatisticsSnapshotMerger.TryMerge(
                new[] { 1 },
                new Dictionary<int, string> { { 1, "Главная касса" } },
                new[] { new OrganizationReport("ООО Колокольчик", "ООО Колокольчик", 50000L) },
                out merged);

            TestAssert.True(applied, "Полный отчёт одной организации должен применяться.");
            TestAssert.Equal("ООО Колокольчик", merged[1].OrganizationName);
            TestAssert.Equal(50000L, merged[1].AmountKopeks);
        }

        public static void MatchesDepartmentsByConfiguredNames()
        {
            IReadOnlyDictionary<int, DepartmentTotal> merged;
            bool applied = StatisticsSnapshotMerger.TryMerge(
                new[] { 1, 2 },
                new Dictionary<int, string>
                {
                    { 1, "ИП Иванов" },
                    { 2, "ООО Колокольчик" }
                },
                new[]
                {
                    new OrganizationReport("ООО Колокольчик", "ООО Колокольчик", 890000L),
                    new OrganizationReport("ИП Иванов", "ИП Иванов", 125000L)
                },
                out merged);

            TestAssert.True(applied, "Названия должны позволить сопоставить обратный порядок отчёта.");
            TestAssert.Equal(125000L, merged[1].AmountKopeks);
            TestAssert.Equal(890000L, merged[2].AmountKopeks);
        }

        public static void RejectsPartialReportAtomically()
        {
            IReadOnlyDictionary<int, DepartmentTotal> merged;
            bool applied = StatisticsSnapshotMerger.TryMerge(
                new[] { 1, 2 },
                new Dictionary<int, string>(),
                new[] { new OrganizationReport("ИП Иванов", "ИП Иванов", 125000L) },
                out merged);

            TestAssert.False(applied, "Отчёт без второй организации не должен применяться.");
            TestAssert.Equal<IReadOnlyDictionary<int, DepartmentTotal>>(null, merged);
        }

        public static void UnknownSingleReportDoesNotDefineExpectedOrganizationCount()
        {
            IReadOnlyList<int> departments =
                ExpectedDepartmentResolver.Resolve(new int[0], 1);

            TestAssert.Equal(
                0,
                departments.Count);
        }

        public static void CompleteTwoSectionReportCanDiscoverBothDepartments()
        {
            IReadOnlyList<int> departments =
                ExpectedDepartmentResolver.Resolve(new int[0], 2);

            TestAssert.Equal(2, departments.Count);
            TestAssert.Equal(1, departments[0]);
            TestAssert.Equal(2, departments[1]);
        }
    }
}
