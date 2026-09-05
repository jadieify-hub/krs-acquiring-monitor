using System;
using Microsoft.Win32;

namespace Krs.AcquiringMonitor.Configuration
{
    internal static class AutoStartManager
    {
        private const string RunKeyPath =
            @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static void Apply(bool enabled, string executablePath)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                RunKeyPath,
                true))
            {
                if (key == null)
                {
                    throw new InvalidOperationException(
                        "Не удалось открыть раздел автозапуска текущего пользователя.");
                }

                if (enabled)
                {
                    key.SetValue(
                        AppConstants.AutoStartValueName,
                        "\"" + executablePath + "\"",
                        RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue(AppConstants.AutoStartValueName, false);
                }
            }
        }
    }
}
