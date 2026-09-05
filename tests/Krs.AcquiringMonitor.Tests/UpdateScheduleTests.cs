using System;
using Krs.AcquiringMonitor.Updates;

namespace Krs.AcquiringMonitor.Tests
{
    internal static class UpdateScheduleTests
    {
        public static void RepeatsChecksAndWaitsForIdle()
        {
            var now = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero);
            var schedule = new UpdateSchedule(now);
            TestAssert.False(schedule.TryBeginCheck(now), "Первый запрос должен быть отложен.");
            TestAssert.True(schedule.TryBeginCheck(now.AddSeconds(3)), "Нужна проверка при запуске.");
            TestAssert.False(schedule.TryBeginCheck(now.AddHours(5)), "Не опрашиваем GitHub каждый тик.");
            TestAssert.True(schedule.TryBeginCheck(now.AddHours(6).AddSeconds(3)),
                "Касса без перезапуска должна получить следующую проверку.");

            TestAssert.False(schedule.CanInstall(now, 1, false), "Сначала ждём спокойную паузу.");
            TestAssert.False(schedule.CanInstall(now.AddSeconds(30), 1, true),
                "Во время запроса или настроек установщик запускать нельзя.");
            TestAssert.False(schedule.CanInstall(now.AddSeconds(59), 1, false), "Пауза ещё не истекла.");
            TestAssert.False(schedule.CanInstall(now.AddSeconds(60), 2, false),
                "Новая операция, даже уже завершённая, должна отложить установку.");
            TestAssert.True(schedule.CanInstall(now.AddSeconds(90), 2, false),
                "После 30 спокойных секунд можно обновить только утилиту.");
        }
    }
}
