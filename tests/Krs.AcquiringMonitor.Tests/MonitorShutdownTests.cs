using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Krs.AcquiringMonitor.Core.Monitoring;
using Krs.AcquiringMonitor.Monitoring;

namespace Krs.AcquiringMonitor.Tests
{
    internal static class MonitorShutdownTests
    {
        public static void HiddenOverlayHandlesSessionShutdown()
        {
            using (var overlay = OverlayAppearanceTests.CreateOverlay())
            {
                bool closed = false;
                overlay.HandleDestroyed += delegate { if (!overlay.RecreatingHandle) closed = true; };
                IntPtr window = overlay.Handle;
                TestAssert.False(overlay.Visible, "Проверяем ещё ни разу не показанный оверлей.");
                typeof(System.Windows.Forms.Control).GetMethod("RecreateHandle",
                    BindingFlags.Instance | BindingFlags.NonPublic).Invoke(overlay, null);
                TestAssert.False(closed, "Пересоздание HWND при настройке не завершает приложение.");
                window = overlay.Handle;
                TestAssert.Equal(new IntPtr(1), SendMessage(window, 0x11, IntPtr.Zero, new IntPtr(1)));
                TestAssert.False(closed, "Запрос готовности не должен закрывать приложение.");
                SendMessage(window, 0x16, IntPtr.Zero, new IntPtr(1));
                TestAssert.False(closed, "Отменённое закрытие не должно закрывать приложение.");
                SendMessage(window, 0x16, new IntPtr(1), new IntPtr(1));
                TestAssert.True(closed, "Подтверждённый WM_ENDSESSION должен запускать закрытие скрытого оверлея.");
            }
            using (var overlay = OverlayAppearanceTests.CreateOverlay())
            {
                bool closed = false, shutdown = false;
                overlay.FormClosed += delegate { closed = true; };
                overlay.HandleDestroyed += delegate { if (!overlay.RecreatingHandle) shutdown = true; };
                IntPtr window = overlay.Handle;
                IntPtr owner = GetWindow(window, 4);
                TestAssert.True(owner != IntPtr.Zero, "У оверлея без кнопки в панели задач есть скрытый владелец.");
                SendMessage(owner, 0x10, IntPtr.Zero, IntPtr.Zero);
                TestAssert.False(closed, "Windows уничтожает HWND через владельца без FormClosed — причина зависания установщика.");
                TestAssert.True(shutdown, "Уничтожение HWND через владельца должно запускать завершение приложения.");
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr window, uint command);

        public static void ShutdownKeepsSafeCheckpointWithoutWaitingForReader()
        {
            using (var directory = new MonthRolloverTests.TemporaryDirectory())
            using (var monitor = new BankLogMonitor(directory.Path, null, null))
            using (var started = new ManualResetEventSlim())
            {
                string path = Path.Combine(directory.Path, "sbkernel2609.log");
                File.WriteAllText(path, "PILOT: card_authorize14: TRType=1, Amount=10000\r\n" +
                    "Command = 4000, Department = 1\r\nPILOT: card_authorize14: result=0, RC=0\r\n");
                monitor.RefreshNow();
                TestAssert.True(monitor.TryApplyAuthoritativeTotals(
                    new Dictionary<int, long> { { 1, 50000L } }, monitor.CaptureRevision()),
                    "Ручная сверка должна стать безопасной базой.");
                string savedFile, savedHash;
                long savedOffset;
                monitor.CaptureCheckpoint(out savedFile, out savedOffset, out savedHash);

                File.AppendAllText(path, "PILOT: card_authorize14: TRType=1, Amount=5000\r\n" +
                    "Command = 4000, Department = 1\r\n");
                monitor.RefreshNow();
                TestAssert.True(monitor.CurrentSnapshot.HasPendingOperation, "Операция ещё не закончилась.");
                File.Move(path, path + ".held");

                object sync = typeof(BankLogMonitor).GetField("_sync",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(monitor);
                string file = null, hash = null;
                long offset = 0;
                BankLogSnapshot checkpoint = null;
                Task stopped = null;
                Monitor.Enter(sync);
                try
                {
                    stopped = Task.Run(() =>
                    {
                        started.Set();
                        checkpoint = monitor.CaptureCheckpoint(out file, out offset, out hash);
                        monitor.Dispose();
                    });
                    TestAssert.True(started.Wait(2000), "Проверка остановки должна запуститься.");
                    TestAssert.True(stopped.Wait(2000), "Выход не должен ждать блокировку читателя журнала.");
                }
                finally
                {
                    Monitor.Exit(sync);
                    if (stopped != null) stopped.Wait();
                }

                TestAssert.Equal(savedFile, file);
                TestAssert.Equal(savedOffset, offset);
                TestAssert.Equal(savedHash, hash);
                TestAssert.Equal(50000L, checkpoint.Totals[1]);
                TestAssert.False(checkpoint.HasPendingOperation, "Сохраняется только безопасная база.");
                File.Move(path + ".held", path);
                File.AppendAllText(path, "PILOT: card_authorize14: result=0, RC=0\r\n");
                using (var restarted = new BankLogMonitor(directory.Path, checkpoint, null, file, offset, hash))
                {
                    restarted.RefreshNow();
                    TestAssert.Equal(55000L, restarted.CurrentSnapshot.Totals[1]);
                }
            }
        }
    }
}
