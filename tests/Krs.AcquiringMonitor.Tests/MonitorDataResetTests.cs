using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Krs.AcquiringMonitor.Configuration;
using Krs.AcquiringMonitor.Diagnostics;
using Krs.AcquiringMonitor.Monitoring;
using Krs.AcquiringMonitor.UI;

namespace Krs.AcquiringMonitor.Tests
{
    internal static class MonitorDataResetTests
    {
        public static void ResetsDataWithoutChangingAppearance(bool lockState)
        {
            Type contextType = typeof(OverlayPresentation).Assembly.GetType(
                "Krs.AcquiringMonitor.MonitorApplicationContext", true);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            MethodInfo reset = contextType.GetMethod("ResetMonitorData", flags, null, Type.EmptyTypes, null);
            TestAssert.True(reset != null, "Нужен сброс данных монитора без сброса оформления.");

            using (var directory = new MonthRolloverTests.TemporaryDirectory())
            using (var overlay = OverlayAppearanceTests.CreateOverlay())
            {
                string bankDirectory = Path.Combine(directory.Path, "SC552");
                Directory.CreateDirectory(bankDirectory);
                string logPath = Path.Combine(bankDirectory, "sbkernel2609.log");
                const string log =
                    "07.09 10:00:00.000 PILOT: card_authorize14: track2=(null), TRType=1, CType=0, Amount=12345\r\n" +
                    "07.09 10:00:00.010 SBKRNL: Command = 4000, Amount = 0.00, Department = 1\r\n" +
                    "07.09 10:00:05.000 PILOT: card_authorize14: result=0, RC=0, cheque=Yes, vas=0\r\n";
                File.WriteAllText(logPath, log);
                var store = new SettingsStore(Path.Combine(directory.Path, "monitor"));
                var settings = AppSettings.CreateDefault();
                settings.UposDirectory = bankDirectory;
                settings.AutoStart = false;
                settings.HasCustomPosition = true;
                settings.OverlayOffsetX = 120;
                settings.OverlayOffsetY = 75;
                settings.OverlayWidth = 610;
                settings.OverlayFontSize = 20f;
                settings.OverlayFontFamily = "Tahoma";
                settings.OverlayNamesBold = true;
                settings.OverlayAmountsBold = false;
                settings.OverlayTextColorArgb = Color.Black.ToArgb();
                settings.OverlayAttentionColorArgb = Color.DarkRed.ToArgb();
                settings.Organizations.Add(new OrganizationSetting
                    { Department = 1, DisplayName = "Моя подпись", BankName = "ИП Пример", IsManual = true });
                settings.Organizations.Add(new OrganizationSetting
                    { Department = 2, DisplayName = "ООО Чужая касса", BankName = "ООО Чужая касса" });
                store.SaveSettings(settings);

                var originalMonitor = new BankLogMonitor(bankDirectory, null, null);
                originalMonitor.RefreshNow();
                TestAssert.True(originalMonitor.TryApplyAuthoritativeTotals(
                    new Dictionary<int, long> { { 1, 99900L }, { 2, 10000L } }, originalMonitor.CaptureRevision()),
                    "Тест начинается со старой контрольной точки двух организаций.");
                string fileName, prefixHash;
                long offset;
                var snapshot = originalMonitor.CaptureCheckpoint(out fileName, out offset, out prefixHash);
                store.SaveRuntimeState(RuntimeState.FromSnapshot(snapshot, fileName, offset, prefixHash, bankDirectory));

                // Bypass the constructor: no autostart, real UPOS, update checks or tray icon.
                object context = FormatterServices.GetUninitializedObject(contextType);
                contextType.GetField("_settingsStore", flags).SetValue(context, store);
                contextType.GetField("_settings", flags).SetValue(context, settings);
                contextType.GetField("_logger", flags).SetValue(context, new SafeLogger(store.BaseDirectory));
                contextType.GetField("_overlay", flags).SetValue(context, overlay);
                FieldInfo monitorField = contextType.GetField("_logMonitor", flags);
                monitorField.SetValue(context, originalMonitor);
                IntPtr unusedHandle = overlay.Handle;
                try
                {
                    Exception failure = null;
                    using (FileStream locked = lockState
                        ? File.Open(Path.Combine(store.BaseDirectory, "state.json"), FileMode.Open,
                            FileAccess.Read, FileShare.None)
                        : null)
                    {
                        try { reset.Invoke(context, null); }
                        catch (TargetInvocationException exception) { failure = exception.InnerException; }
                    }

                    TestAssert.Equal(lockState, failure is IOException);
                    if (!lockState)
                        TestAssert.True(failure == null, "Успешный сброс не должен выдавать ошибку: " + failure);
                    var monitor = (BankLogMonitor)monitorField.GetValue(context);
                    monitor.RefreshNow();
                    contextType.GetMethod("LogSnapshotChanged", flags).Invoke(context, new object[] { monitor, null });
                    TestAssert.Equal(lockState ? 2 : 1, monitor.CurrentSnapshot.Totals.Count);
                    TestAssert.Equal(lockState ? 99900L : 12345L, monitor.CurrentSnapshot.Totals[1]);
                    TestAssert.Equal(lockState ? 2 : 0, settings.Organizations.Count);
                    TestAssert.Equal(lockState ? 2 : 1,
                        OverlayPresentation.BuildRows(monitor.CurrentSnapshot, settings.GetOrganizationNames()).Count);

                    // A callback already queued by the old monitor must not restore its checkpoint.
                    contextType.GetMethod("LogSnapshotChanged", flags).Invoke(context, new object[] { originalMonitor, null });
                    TestAssert.Equal(lockState ? 2 : 1, store.LoadRuntimeState().Departments.Count);
                    AppSettings loaded = store.LoadSettings();
                    TestAssert.Equal(lockState ? 2 : 0, loaded.Organizations.Count);
                    if (lockState) TestAssert.Equal("Моя подпись", loaded.Organizations[0].DisplayName);
                    TestAssert.Equal(bankDirectory, loaded.UposDirectory);
                    TestAssert.False(loaded.AutoStart, "Сброс не включает автозапуск.");
                    TestAssert.True(loaded.HasCustomPosition, "Положение остаётся пользовательским.");
                    TestAssert.Equal(120, loaded.OverlayOffsetX);
                    TestAssert.Equal(75, loaded.OverlayOffsetY);
                    TestAssert.Equal(610, loaded.OverlayWidth);
                    TestAssert.Equal(20f, loaded.OverlayFontSize);
                    TestAssert.Equal("Tahoma", loaded.OverlayFontFamily);
                    TestAssert.True(loaded.OverlayNamesBold, "Жирность названий сохраняется.");
                    TestAssert.Equal((bool?)false, loaded.OverlayAmountsBold);
                    TestAssert.Equal(Color.Black.ToArgb(), loaded.OverlayTextColorArgb);
                    TestAssert.Equal(Color.DarkRed.ToArgb(), loaded.OverlayAttentionColorArgb);
                    TestAssert.Equal(log, File.ReadAllText(logPath));
                }
                finally
                {
                    ((BankLogMonitor)monitorField.GetValue(context)).Dispose();
                    originalMonitor.Dispose();
                }
            }
        }
    }
}
