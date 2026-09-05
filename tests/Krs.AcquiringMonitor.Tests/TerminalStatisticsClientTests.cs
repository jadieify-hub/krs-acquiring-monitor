using System;
using System.IO;
using System.Reflection;
using Krs.AcquiringMonitor.Terminal;

namespace Krs.AcquiringMonitor.Tests
{
    internal static class TerminalStatisticsClientTests
    {
        public static void QuotesWindowsPathWithoutChangingSeparators()
        {
            MethodInfo method = typeof(TerminalStatisticsClient).GetMethod(
                "QuoteArgument",
                BindingFlags.NonPublic | BindingFlags.Static);

            string actual = (string)method.Invoke(
                null,
                new object[] { @"C:\Program Files\Sberbank\SC552" });

            TestAssert.Equal(
                "\"C:\\Program Files\\Sberbank\\SC552\"",
                actual);
        }

        public static void RemovesStaleTemporaryReports()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "KRS-AcquiringMonitor-tests",
                Guid.NewGuid().ToString("N"));
            string staleDirectory = Path.Combine(root, "stale-query");
            string staleReport = Path.Combine(staleDirectory, "report.txt");
            string legacyReport = Path.Combine(root, "legacy.txt");
            Directory.CreateDirectory(staleDirectory);
            File.WriteAllText(staleReport, "sensitive");
            File.WriteAllText(legacyReport, "sensitive");

            try
            {
                MethodInfo method = typeof(TerminalStatisticsClient).GetMethod(
                    "CleanupStaleReports",
                    BindingFlags.NonPublic | BindingFlags.Static);
                TestAssert.True(method != null, "Метод безопасной очистки должен существовать.");
                method.Invoke(null, new object[] { root });

                TestAssert.False(File.Exists(staleReport), "Старый отчёт должен удаляться.");
                TestAssert.False(File.Exists(legacyReport), "Старый файл прежней версии должен удаляться.");
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }
    }
}
