using System;
using System.Reflection;
using System.Runtime.Serialization;
using Krs.AcquiringMonitor.Configuration;
using Krs.AcquiringMonitor.Diagnostics;
using Krs.AcquiringMonitor.Frontol;
using Krs.AcquiringMonitor.Monitoring;
using Krs.AcquiringMonitor.UI;
using Krs.AcquiringMonitor.Updates;

namespace Krs.AcquiringMonitor.Tests
{
    internal static class DiagnosticsTests
    {
        public static void ReportsTechnicalStateWithoutBankData()
        {
            Type type = typeof(OverlayPresentation).Assembly.GetType(
                "Krs.AcquiringMonitor.MonitorApplicationContext", true);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            MethodInfo build = type.GetMethod("BuildDiagnostics", flags);
            TestAssert.True(build != null, "Нужна техническая сводка без чтения банковского отчёта.");

            using (var directory = new MonthRolloverTests.TemporaryDirectory())
            using (var overlay = OverlayAppearanceTests.CreateOverlay())
            using (var monitor = new BankLogMonitor(directory.Path, null, null))
            {
                var settings = AppSettings.CreateDefault();
                settings.UposDirectory = directory.Path;
                settings.Organizations.Add(new OrganizationSetting
                    { Department = 1, DisplayName = "SECRET ORGANIZATION", BankName = "SECRET BANK NAME" });
                var logger = new SafeLogger(directory.Path);
                logger.Write(SafeLogEvent.TerminalQueryFailed, "report-format",
                    new InvalidOperationException("SECRET BANK REPORT 1234567890123456"));
                object context = FormatterServices.GetUninitializedObject(type);
                type.GetField("_settings", flags).SetValue(context, settings);
                type.GetField("_logger", flags).SetValue(context, logger);
                type.GetField("_updater", flags).SetValue(context, new ApplicationUpdater(logger));
                type.GetField("_logMonitor", flags).SetValue(context, monitor);
                type.GetField("_overlay", flags).SetValue(context, overlay);
                type.GetField("_frontolTracker", flags).SetValue(context, new FrontolWindowTracker());

                string text = (string)build.Invoke(context, null);
                TestAssert.True(text.Contains("report-format") && text.Contains("InvalidOperationException"),
                    "Код и тип последней ошибки нужны для разбора сбоя.");
                TestAssert.True(text.Contains("UPOS:") && text.Contains("Frontol:") && text.Contains("устарели"),
                    "Нужны источник, распознавание Frontol и достоверность данных.");
                TestAssert.False(text.Contains("SECRET") || text.Contains("1234567890123456"),
                    "Названия организаций, реквизиты и текст исключения нельзя копировать в сводку.");
                TestAssert.True((bool)type.GetMethod("IsBusyForUpdate", flags)
                    .Invoke(context, new object[] { monitor }),
                    "Недоступный журнал не считается безопасной паузой для установки.");
                using (var menu = (System.Windows.Forms.ContextMenuStrip)type
                    .GetMethod("CreateTrayMenu", flags).Invoke(context, null))
                {
                    TestAssert.Equal("Обновить суммы", menu.Items[1].Text);
                    TestAssert.Equal("Проверить обновление программы…", menu.Items[2].Text);
                }
            }
        }
    }
}
