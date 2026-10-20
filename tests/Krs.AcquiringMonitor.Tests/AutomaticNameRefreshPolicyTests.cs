using System;
using System.Collections.Generic;
using Krs.AcquiringMonitor.Configuration;
using Krs.AcquiringMonitor.Core.Monitoring;
using Krs.AcquiringMonitor.Terminal;

namespace Krs.AcquiringMonitor.Tests
{
    internal static class AutomaticNameRefreshPolicyTests
    {
        public static void WaitsUntilDueAndRetriesAfterTenMinutes()
        {
            var firstAttempt = new DateTimeOffset(
                2026,
                9,
                4,
                12,
                0,
                30,
                TimeSpan.Zero);
            var settings = AppSettings.CreateDefault();
            settings.Organizations.Add(
                new OrganizationSetting
                {
                    Department = 1,
                    DisplayName = "Касса ИП",
                    BankName = string.Empty,
                    IsManual = true
                });
            BankLogSnapshot snapshot = BankLogSnapshot.FromTotals(
                new Dictionary<int, long> { { 1, 10000L } },
                false);
            var policy = new AutomaticNameRefreshPolicy(firstAttempt);

            TestAssert.False(
                policy.ShouldAttempt(
                    settings,
                    snapshot,
                    firstAttempt.AddSeconds(-1)),
                "До начального срока автоматический запрос не нужен.");
            TestAssert.True(
                policy.ShouldAttempt(settings, snapshot, firstAttempt),
                "Отсутствующее банковское имя нужно запросить в назначенный срок.");

            policy.RecordAttempt(firstAttempt);
            TestAssert.False(
                policy.ShouldAttempt(
                    settings,
                    snapshot,
                    firstAttempt.AddMinutes(10).AddSeconds(-1)),
                "Повторный запрос не должен запускаться раньше десяти минут.");
            TestAssert.True(
                policy.ShouldAttempt(
                    settings,
                    snapshot,
                    firstAttempt.AddMinutes(10)),
                "Через десять минут незаполненное имя можно запросить снова.");

            settings.Organizations[0].BankName = "ИП Иванов";
            TestAssert.False(
                policy.ShouldAttempt(
                    settings,
                    snapshot,
                    firstAttempt.AddMinutes(10)),
                "После заполнения банковского имени новые запросы не нужны.");
            TestAssert.Equal("Касса ИП", settings.Organizations[0].DisplayName);
        }

        public static void AcceptedReportStopsAskingTheTerminal()
        {
            var now = new DateTimeOffset(2026, 9, 4, 12, 0, 30, TimeSpan.Zero);
            AppSettings settings;
            BankLogSnapshot snapshot;
            var policy = CreatePolicyForMissingName(now, out settings, out snapshot);

            TestAssert.True(policy.ShouldAttempt(settings, snapshot, now),
                "Пустое банковское имя нужно запросить.");

            // Отчёт перечисляет все организации терминала, поэтому второе имя
            // не появится и повторять запрос бессмысленно до конца работы процесса.
            policy.RecordAccepted();
            TestAssert.False(policy.ShouldAttempt(settings, snapshot, now.AddHours(8)),
                "После принятого отчёта фоновые запросы не возобновляются.");
        }

        public static void ThreeFailuresStopAutomaticNaming()
        {
            var now = new DateTimeOffset(2026, 9, 4, 12, 0, 30, TimeSpan.Zero);
            AppSettings settings;
            BankLogSnapshot snapshot;
            var policy = CreatePolicyForMissingName(now, out settings, out snapshot);

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                policy.RecordAttempt(now);
                policy.RecordFailure();
                now = now.AddMinutes(10);
                TestAssert.True(policy.ShouldAttempt(settings, snapshot, now),
                    "До исчерпания лимита неудач запрос повторяется.");
            }

            policy.RecordAttempt(now);
            policy.RecordFailure();
            TestAssert.False(
                policy.ShouldAttempt(settings, snapshot, now.AddHours(8)),
                "После трёх неудач терминал не опрашивается автоматически; остаётся ручной запрос.");
        }

        private static AutomaticNameRefreshPolicy CreatePolicyForMissingName(
            DateTimeOffset firstAttempt,
            out AppSettings settings,
            out BankLogSnapshot snapshot)
        {
            settings = AppSettings.CreateDefault();
            settings.Organizations.Add(
                new OrganizationSetting
                {
                    Department = 1,
                    DisplayName = "Касса ИП",
                    BankName = string.Empty,
                    IsManual = true
                });
            snapshot = BankLogSnapshot.FromTotals(
                new Dictionary<int, long> { { 1, 10000L } },
                false);
            return new AutomaticNameRefreshPolicy(firstAttempt);
        }
    }
}
