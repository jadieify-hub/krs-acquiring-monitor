using System;
using System.Threading;
using System.Windows.Forms;

namespace Krs.AcquiringMonitor
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool created;
            using (var mutex = new Mutex(
                true,
                @"Local\KRS.AcquiringMonitor.Singleton",
                out created))
            {
                if (!created)
                {
                    return;
                }

                Application.SetUnhandledExceptionMode(
                    UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate
                {
                    MessageBox.Show(
                        "Произошла техническая ошибка. Перезапустите программу. " +
                        "Предыдущие суммы останутся в локальном состоянии.",
                        AppConstants.ApplicationName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                };
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MonitorApplicationContext());
                mutex.ReleaseMutex();
            }
        }
    }
}
