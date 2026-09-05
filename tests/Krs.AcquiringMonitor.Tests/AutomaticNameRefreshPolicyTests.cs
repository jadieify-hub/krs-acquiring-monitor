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
    }
}
