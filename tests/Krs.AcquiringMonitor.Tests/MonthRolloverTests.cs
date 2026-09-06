using System;
using System.Collections.Generic;
using System.IO;
using Krs.AcquiringMonitor.Configuration;
using Krs.AcquiringMonitor.Core.Monitoring;
using Krs.AcquiringMonitor.Monitoring;

namespace Krs.AcquiringMonitor.Tests
{
    internal static class MonthRolloverTests
    {
        public static void PreservesTotalsAcrossMonthlyFiles()
        {
            using (var directory = new TemporaryDirectory())
            {
                File.WriteAllText(
                    Path.Combine(directory.Path, "sbkernel2608.log"),
                    SuccessfulPurchase(1, 10000));
                File.WriteAllText(
                    Path.Combine(directory.Path, "sbkernel2609.log"),
                    SuccessfulPurchase(2, 20000));

                using (var monitor = new BankLogMonitor(directory.Path, null, null))
                {
                    monitor.RefreshNow();

                    TestAssert.Equal(10000L, monitor.CurrentSnapshot.Totals[1]);
                    TestAssert.Equal(20000L, monitor.CurrentSnapshot.Totals[2]);
                }
            }
        }

        public static void RestartRebuildsSameTotals()
        {
            using (var directory = new TemporaryDirectory())
            {
                File.WriteAllText(
                    Path.Combine(directory.Path, "sbkernel2609.log"),
                    SuccessfulPurchase(1, 33300));

                long first;
                using (var monitor = new BankLogMonitor(directory.Path, null, null))
                {
                    monitor.RefreshNow();
                    first = monitor.CurrentSnapshot.Totals[1];
                }

                using (var restarted = new BankLogMonitor(directory.Path, null, null))
                {
                    restarted.RefreshNow();
                    TestAssert.Equal(first, restarted.CurrentSnapshot.Totals[1]);
                }
            }
        }

        public static void MissingDirectoryUsesStaleFallback()
        {
            BankLogSnapshot fallback = BankLogSnapshot.FromTotals(
                new Dictionary<int, long> { { 1, 32100L } },
                false);
            string missing = Path.Combine(
                Path.GetTempPath(),
                "KRS-AcquiringMonitor-tests",
                Guid.NewGuid().ToString("N"));

            using (var monitor = new BankLogMonitor(missing, fallback, null))
            {
                monitor.RefreshNow();

                TestAssert.Equal(32100L, monitor.CurrentSnapshot.Totals[1]);
                TestAssert.True(
                    monitor.CurrentSnapshot.IsStale,
                    "Сохранённая сумма из недоступной папки должна быть устаревшей.");
            }
        }

        public static void SettingsRoundTrip()
        {
            using (var directory = new TemporaryDirectory())
            {
                var store = new SettingsStore(directory.Path);
                var settings = AppSettings.CreateDefault();
                settings.UposDirectory = @"C:\SC552";
                settings.OverlayOffsetX = 444;
                settings.OverlayWidth = 610;
                settings.OverlayFontSize = 18f;
                settings.Organizations.Add(
                    new OrganizationSetting
                    {
                        Department = 1,
                        BankName = "ИП Иванов",
                        DisplayName = "Главная касса",
                        IsManual = true
                    });

                store.SaveSettings(settings);
                AppSettings loaded = store.LoadSettings();

                TestAssert.Equal(@"C:\SC552", loaded.UposDirectory);
                TestAssert.Equal(444, loaded.OverlayOffsetX);
                TestAssert.Equal(610, loaded.OverlayWidth);
                TestAssert.Equal(18f, loaded.OverlayFontSize);
                TestAssert.Equal("ИП Иванов", loaded.Organizations[0].BankName);
                TestAssert.Equal("Главная касса", loaded.Organizations[0].DisplayName);
            }
        }

        public static void KeepsBankIdentitySeparateFromDisplayName()
        {
            var settings = AppSettings.CreateDefault();
            settings.Organizations.Add(
                new OrganizationSetting
                {
                    Department = 1,
                    BankName = "ООО Колокольчик",
                    DisplayName = "Главная касса",
                    IsManual = true
                });

            TestAssert.Equal(
                "ООО Колокольчик",
                settings.GetBankOrganizationNames()[1]);
            TestAssert.Equal(
                "Главная касса",
                settings.GetOrganizationNames()[1]);
        }

        public static void OldSettingsKeepDefaultAppearance()
        {
            using (var directory = new TemporaryDirectory())
            {
                File.WriteAllText(Path.Combine(directory.Path, "settings.json"),
                    "{\"OverlayOffsetX\":444,\"AutoStart\":true,\"Organizations\":[]}");
                AppSettings loaded = new SettingsStore(directory.Path).LoadSettings();
                TestAssert.Equal(444, loaded.OverlayOffsetX);
                TestAssert.True(loaded.AutoStart, "Старый автозапуск должен сохраниться.");
                TestAssert.Equal(AppSettings.DefaultOverlayWidth, loaded.OverlayWidth);
                TestAssert.Equal(AppSettings.DefaultOverlayFontSize, loaded.OverlayFontSize);
            }
        }

        public static void RejectsUnstableRuntimeCheckpoint()
        {
            BankLogSnapshot fresh = BankLogSnapshot.FromTotals(
                new Dictionary<int, long> { { 1, 10000L } },
                false);
            BankLogSnapshot stale = fresh.AsStale();
            var parser = new BankLogParser();
            parser.ProcessLine(
                "01.09 10:00:00.000 PILOT: card_authorize14: track2=(null), TRType=1, CType=0, Amount=5000");

            TestAssert.True(
                RuntimeState.CanPersistSnapshot(fresh),
                "Завершённый свежий снимок должен сохраняться.");
            TestAssert.False(
                RuntimeState.CanPersistSnapshot(stale),
                "Устаревший снимок не должен затирать рабочую контрольную точку.");
            TestAssert.False(
                RuntimeState.CanPersistSnapshot(parser.Snapshot),
                "Незавершённая операция не должна попадать в контрольную точку.");
        }

        public static void RejectsManualSnapshotAfterLogChanged()
        {
            using (var directory = new TemporaryDirectory())
            {
                string logPath = Path.Combine(directory.Path, "sbkernel2609.log");
                File.WriteAllText(logPath, SuccessfulPurchase(1, 10000));

                using (var monitor = new BankLogMonitor(directory.Path, null, null))
                {
                    monitor.RefreshNow();
                    long revision = monitor.CaptureRevision();

                    File.AppendAllText(logPath, SuccessfulPurchase(1, 5000));
                    monitor.RefreshNow();
                    bool applied = monitor.TryApplyAuthoritativeTotals(
                        new Dictionary<int, long> { { 1, 10000L } },
                        revision);

                    TestAssert.False(
                        applied,
                        "Снимок, полученный до новой оплаты, не должен перезаписывать журнал.");
                    TestAssert.Equal(15000L, monitor.CurrentSnapshot.Totals[1]);
                }
            }
        }

        public static void RejectsManualSnapshotAfterNetZeroLogActivity()
        {
            using (var directory = new TemporaryDirectory())
            {
                string logPath = Path.Combine(directory.Path, "sbkernel2609.log");
                File.WriteAllText(logPath, SuccessfulPurchase(1, 10000));

                using (var monitor = new BankLogMonitor(directory.Path, null, null))
                {
                    monitor.RefreshNow();
                    long revision = monitor.CaptureRevision();

                    File.AppendAllText(
                        logPath,
                        SuccessfulPurchase(1, 5000) +
                        SuccessfulRefund(1, 5000));
                    monitor.RefreshNow();
                    bool applied = monitor.TryApplyAuthoritativeTotals(
                        new Dictionary<int, long> { { 1, 10000L } },
                        revision);

                    TestAssert.False(
                        applied,
                        "Любая финансовая активность должна инвалидировать ранее снятый отчёт.");
                }
            }
        }

        public static void AllowsManualSnapshotAfterStatisticsOnlyLogActivity()
        {
            using (var directory = new TemporaryDirectory())
            {
                string logPath = Path.Combine(directory.Path, "sbkernel2609.log");
                File.WriteAllText(logPath, SuccessfulPurchase(1, 10000));

                using (var monitor = new BankLogMonitor(directory.Path, null, null))
                {
                    monitor.RefreshNow();
                    long revision = monitor.CaptureRevision();

                    // UPOS logs statistics (7000) through close_day, just like settlement (6000).
                    foreach (string line in new[]
                    {
                        "01.09 10:02:00.000 PILOT: close_day. Version: 34.09.03",
                        "01.09 10:02:00.010 SBKRNL: Command = 7000",
                        "01.09 10:02:00.100 PILOT: close_day: result=0, RC=0"
                    })
                    {
                        File.AppendAllText(logPath, line + "\r\n");
                        monitor.RefreshNow();
                        TestAssert.Equal(10000L, monitor.CurrentSnapshot.Totals[1]);
                        TestAssert.False(monitor.CurrentSnapshot.IsStale,
                            "Статистика не должна начинать закрытие смены.");
                        TestAssert.False(monitor.HasPendingOperation,
                            "Статистика не является финансовой операцией.");
                    }
                    bool applied = monitor.TryApplyAuthoritativeTotals(
                        new Dictionary<int, long> { { 1, 12500L } },
                        revision);

                    TestAssert.True(
                        applied,
                        "Служебные строки самого запроса статистики не должны отклонять его результат.");
                    TestAssert.Equal(12500L, monitor.CurrentSnapshot.Totals[1]);
                }
            }
        }

        public static void RejectsManualSnapshotWhenLogHasUnreadBytes()
        {
            using (var directory = new TemporaryDirectory())
            {
                string logPath = Path.Combine(directory.Path, "sbkernel2609.log");
                File.WriteAllText(logPath, SuccessfulPurchase(1, 10000));

                using (var monitor = new BankLogMonitor(directory.Path, null, null))
                {
                    monitor.RefreshNow();
                    long revision = monitor.CaptureRevision();
                    File.AppendAllText(logPath, SuccessfulPurchase(1, 5000));

                    bool applied = monitor.TryApplyAuthoritativeTotals(
                        new Dictionary<int, long> { { 1, 10000L } },
                        revision);

                    TestAssert.False(
                        applied,
                        "Непрочитанные строки журнала должны блокировать применение отчёта.");
                }
            }
        }

        public static void RejectsManualSnapshotWithoutLogAnchor()
        {
            using (var directory = new TemporaryDirectory())
            using (var monitor = new BankLogMonitor(directory.Path, null, null))
            {
                monitor.RefreshNow();

                bool applied = monitor.TryApplyAuthoritativeTotals(
                    new Dictionary<int, long> { { 1, 10000L } },
                    monitor.CaptureRevision());

                TestAssert.False(
                    applied,
                    "Ручная база без существующего журнала не сможет безопасно пережить перезапуск.");
            }
        }

        public static void ManualSnapshotResumesFromSavedOffset()
        {
            using (var directory = new TemporaryDirectory())
            {
                string logPath = Path.Combine(directory.Path, "sbkernel2609.log");
                File.WriteAllText(logPath, SuccessfulPurchase(1, 10000));

                BankLogSnapshot checkpoint;
                string checkpointFile;
                long checkpointOffset;
                string checkpointHash;
                using (var monitor = new BankLogMonitor(directory.Path, null, null))
                {
                    monitor.RefreshNow();
                    long revision = monitor.CaptureRevision();
                    bool applied = monitor.TryApplyAuthoritativeTotals(
                        new Dictionary<int, long> { { 1, 50000L } },
                        revision);

                    TestAssert.True(applied, "Ручная база должна примениться.");
                    checkpoint = monitor.CaptureCheckpoint(
                        out checkpointFile,
                        out checkpointOffset,
                        out checkpointHash);
                }

                File.AppendAllText(logPath, SuccessfulPurchase(1, 5000));

                using (var restarted = new BankLogMonitor(
                    directory.Path,
                    checkpoint,
                    null,
                    checkpointFile,
                    checkpointOffset,
                    checkpointHash))
                {
                    restarted.RefreshNow();

                    TestAssert.Equal(55000L, restarted.CurrentSnapshot.Totals[1]);
                }
            }
        }

        public static void RestartBetweenDepartmentClosesCompletesReset()
        {
            using (var directory = new TemporaryDirectory())
            {
                string logPath = Path.Combine(directory.Path, "sbkernel2609.log");
                File.WriteAllText(
                    logPath,
                    SuccessfulPurchase(1, 10000) +
                    SuccessfulPurchase(2, 20000));

                BankLogSnapshot checkpoint;
                string checkpointFile;
                long checkpointOffset;
                string checkpointHash;
                using (var monitor = new BankLogMonitor(directory.Path, null, null))
                {
                    monitor.RefreshNow();
                    checkpoint = monitor.CaptureCheckpoint(
                        out checkpointFile,
                        out checkpointOffset,
                        out checkpointHash);

                    File.AppendAllText(logPath, SuccessfulClose());
                    monitor.RefreshNow();
                    TestAssert.False(
                        RuntimeState.CanPersistSnapshot(monitor.CurrentSnapshot),
                        "Первое из двух закрытий не должно затирать предыдущую контрольную точку.");
                }

                File.AppendAllText(logPath, SuccessfulClose());
                using (var restarted = new BankLogMonitor(
                    directory.Path,
                    checkpoint,
                    null,
                    checkpointFile,
                    checkpointOffset,
                    checkpointHash))
                {
                    restarted.RefreshNow();

                    TestAssert.Equal(0L, restarted.CurrentSnapshot.Totals[1]);
                    TestAssert.Equal(0L, restarted.CurrentSnapshot.Totals[2]);
                    TestAssert.False(
                        restarted.CurrentSnapshot.IsStale,
                        "После второго закрытия обе организации должны быть обнулены.");
                }
            }
        }

        public static void RuntimeStateIsBoundToUposDirectory()
        {
            using (var directory = new TemporaryDirectory())
            {
                BankLogSnapshot snapshot = BankLogSnapshot.FromTotals(
                    new Dictionary<int, long> { { 1, 10000L } },
                    false);
                RuntimeState state = RuntimeState.FromSnapshot(
                    snapshot,
                    "sbkernel2609.log",
                    100L,
                    "hash",
                    @"C:\SC552");
                var store = new SettingsStore(directory.Path);
                store.SaveRuntimeState(state);
                RuntimeState loaded = store.LoadRuntimeState();

                TestAssert.True(
                    loaded.MatchesSourceDirectory(@"c:\SC552\"),
                    "Тот же каталог должен принимать сохранённое состояние.");
                TestAssert.False(
                    loaded.MatchesSourceDirectory(@"D:\SC552"),
                    "Другой каталог не должен получать суммы и позицию прежнего терминала.");
            }
        }

        public static void MissingActiveLogKeepsLastSnapshotStale()
        {
            using (var directory = new TemporaryDirectory())
            {
                string august = Path.Combine(directory.Path, "sbkernel2608.log");
                string september = Path.Combine(directory.Path, "sbkernel2609.log");
                File.WriteAllText(august, SuccessfulPurchase(1, 10000));
                File.WriteAllText(september, SuccessfulPurchase(2, 20000));

                using (var monitor = new BankLogMonitor(directory.Path, null, null))
                {
                    monitor.RefreshNow();
                    File.Delete(september);
                    monitor.RefreshNow();

                    TestAssert.Equal(10000L, monitor.CurrentSnapshot.Totals[1]);
                    TestAssert.Equal(20000L, monitor.CurrentSnapshot.Totals[2]);
                    TestAssert.True(
                        monitor.CurrentSnapshot.IsStale,
                        "Исчезновение активного файла не должно повторно учитывать старый месяц.");
                }
            }
        }

        public static void ReplacedActiveLogIsRebuilt()
        {
            using (var directory = new TemporaryDirectory())
            {
                string logPath = Path.Combine(directory.Path, "sbkernel2609.log");
                File.WriteAllText(logPath, SuccessfulPurchase(1, 10000));

                using (var monitor = new BankLogMonitor(directory.Path, null, null))
                {
                    monitor.RefreshNow();
                    File.WriteAllText(logPath, SuccessfulPurchase(1, 20000));
                    monitor.RefreshNow();

                    TestAssert.Equal(20000L, monitor.CurrentSnapshot.Totals[1]);
                }
            }
        }

        private static string SuccessfulPurchase(int department, long amountKopeks)
        {
            return string.Format(
                "01.09 10:00:00.000 PILOT: card_authorize14: track2=(null), TRType=1, CType=0, Amount={0}\r\n" +
                "01.09 10:00:00.010 SBKRNL: Command = 4000, Amount = 0.00, Department = {1}\r\n" +
                "01.09 10:00:05.000 PILOT: card_authorize14: result=0, RC=0, cheque=Yes, vas=0\r\n",
                amountKopeks,
                department);
        }

        private static string SuccessfulRefund(int department, long amountKopeks)
        {
            return string.Format(
                "01.09 10:01:00.000 PILOT: card_authorize14: track2=(null), TRType=3, CType=0, Amount={0}\r\n" +
                "01.09 10:01:00.010 SBKRNL: Command = 4002, Amount = 0.00, Department = {1}\r\n" +
                "01.09 10:01:05.000 PILOT: card_authorize14: result=0, RC=0, cheque=Yes, vas=0\r\n",
                amountKopeks,
                department);
        }

        private static string SuccessfulClose()
        {
            return
                "01.09 21:50:35.600 PILOT: close_day. Version: 34.09.03\r\n" +
                "01.09 21:50:35.647 SBKRNL: Command = 6000\r\n" +
                "01.09 21:50:42.030 PILOT: close_day: result=0, RC=0\r\n";
        }

        internal sealed class TemporaryDirectory : IDisposable
        {
            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "KRS-AcquiringMonitor-tests",
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public string Path { get; private set; }

            public void Dispose()
            {
                try
                {
                    Directory.Delete(Path, true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
