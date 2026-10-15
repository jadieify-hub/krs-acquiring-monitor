using System;
using System.IO;
using Krs.AcquiringMonitor.Diagnostics;

namespace Krs.AcquiringMonitor.Tests
{
    internal static class SafeLoggerTests
    {
        public static void RepeatedFailureIsNotWrittenEverySecond()
        {
            using (var directory = new MonthRolloverTests.TemporaryDirectory())
            {
                var logger = new SafeLogger(directory.Path);
                string path = Path.Combine(directory.Path, "diagnostics.log");

                // An unavailable journal produces this once a second for as long as it lasts.
                for (int index = 0; index < 500; index++)
                {
                    logger.Write(
                        SafeLogEvent.LogMonitorUnavailable,
                        directory.Path,
                        new IOException("locked"));
                }

                TestAssert.Equal(1, File.ReadAllLines(path).Length);
                TestAssert.True(
                    logger.LastFailure != null &&
                    logger.LastFailure.Contains("LogMonitorUnavailable"),
                    "Сводка диагностики должна видеть текущую ошибку, даже когда запись подавлена.");

                logger.Write(SafeLogEvent.SettingsFailure, "settings", null);

                TestAssert.Equal(2, File.ReadAllLines(path).Length);
            }
        }

        public static void LogRotatesInsteadOfGrowingForever()
        {
            using (var directory = new MonthRolloverTests.TemporaryDirectory())
            {
                var logger = new SafeLogger(directory.Path);
                string path = Path.Combine(directory.Path, "diagnostics.log");
                string previous = path + ".1";

                // Distinct details defeat the repeat filter, so only rotation bounds the size.
                string padding = new string('d', 250);
                for (int index = 0; index < 5000; index++)
                {
                    logger.Write(
                        SafeLogEvent.TerminalQueryFailed,
                        index.ToString(System.Globalization.CultureInfo.InvariantCulture) + padding,
                        null);
                }

                TestAssert.True(File.Exists(previous), "Переполненный журнал должен уехать в .1.");
                TestAssert.True(new FileInfo(path).Length < 1024L * 1024L,
                    "Текущий файл диагностики не должен превышать мегабайт.");
                TestAssert.True(new FileInfo(previous).Length < 2L * 1024L * 1024L,
                    "Предыдущий файл диагностики тоже ограничен.");
                TestAssert.Equal(2, Directory.GetFiles(directory.Path, "diagnostics.log*").Length);
            }
        }
    }
}
