using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Krs.AcquiringMonitor.Configuration;
using Krs.AcquiringMonitor.Frontol;
using Krs.AcquiringMonitor.Monitoring;
using Krs.AcquiringMonitor.UI;

namespace Krs.AcquiringMonitor.Tests
{
    internal static class RuntimeDiagnostics
    {
        public static int RenderPreview(string path)
        {
            using (Form overlay = OverlayAppearanceTests.CreateOverlay())
            using (var bitmap = new Bitmap(700, 480))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                overlay.GetType().GetMethod("SetRows").Invoke(overlay, new object[]
                {
                    new[]
                    {
                        new OverlayRow(1, "ИП Иванов", "1 475,00 ₽", false),
                        new OverlayRow(2, "ООО Колокольчик", "237,00 ₽", false)
                    }
                });
                for (int theme = 0; theme < 2; theme++)
                {
                    using (var background = new SolidBrush(theme == 0 ? Color.FromArgb(70, 82, 90) : Color.White))
                        graphics.FillRectangle(background, 0, theme * 240, 700, 240);
                    overlay.GetType().GetMethod("SetColors").Invoke(overlay, new object[]
                    {
                        (theme == 0 ? AppSettings.DefaultOverlayTextColor : Color.Black).ToArgb(),
                        (theme == 0 ? AppSettings.DefaultOverlayAttentionColor : Color.DarkRed).ToArgb(), false
                    });
                    overlay.GetType().GetMethod("SetRefreshStatus").Invoke(overlay,
                        new object[] { "", false });
                    overlay.GetType().GetMethod("SetAppearance").Invoke(overlay,
                        new object[] { 340, 13f });
                    DrawForm(overlay, graphics, new Point(20, theme * 240 + 20));
                    overlay.GetType().GetMethod("SetRefreshStatus").Invoke(overlay,
                        new object[] { "Демонстрация предупреждения", true });
                    overlay.GetType().GetMethod("SetAppearance").Invoke(overlay,
                        new object[] { 650, 20f });
                    DrawForm(overlay, graphics, new Point(20, theme * 240 + 125));
                }

                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                bitmap.Save(path, ImageFormat.Png);
            }
            Console.WriteLine(Path.GetFullPath(path));
            return 0;
        }

        public static int RenderSettingsPreview(string path)
        {
            using (Form overlay = OverlayAppearanceTests.CreateOverlay())
            using (Form settings = OverlayAppearanceTests.CreateSettingsEditor(AppSettings.CreateDefault(), overlay))
            using (var bitmap = new Bitmap(settings.Width, settings.Height))
            {
                CreateHandles(settings);
                settings.DrawToBitmap(bitmap, new Rectangle(Point.Empty, settings.Size));
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                bitmap.Save(path, ImageFormat.Png);
            }
            Console.WriteLine(Path.GetFullPath(path));
            return 0;
        }

        private static void DrawForm(Form form, Graphics graphics, Point location)
        {
            using (var bitmap = (Bitmap)form.GetType().GetMethod("RenderBitmap", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(form, null))
                graphics.DrawImageUnscaled(bitmap, location);
        }

        private static void CreateHandles(Control control)
        {
            IntPtr unused = control.Handle;
            foreach (Control child in control.Controls) CreateHandles(child);
        }

        public static int MeasureIdle()
        {
            uint foregroundProcessId;
            GetWindowThreadProcessId(GetForegroundWindow(), out foregroundProcessId);
            Console.WriteLine("CLR={0}; logical CPUs={1}", Environment.Version, Environment.ProcessorCount);
            Measure("foreground-process-name", () =>
            {
                using (Process process = Process.GetProcessById((int)foregroundProcessId))
                    GC.KeepAlive(process.ProcessName);
            });
            Measure("foreground-main-title", () =>
            {
                using (Process process = Process.GetProcessById((int)foregroundProcessId))
                    GC.KeepAlive(process.MainWindowTitle);
            });
            var tracker = new FrontolWindowTracker();
            Measure("frontol-tracker", () =>
            {
                FrontolWindowInfo info;
                tracker.TryGetActive(out info);
            });
            using (Form overlay = OverlayAppearanceTests.CreateOverlay())
            {
                MethodInfo render = overlay.GetType().GetMethod("RenderSurface", BindingFlags.Instance | BindingFlags.NonPublic);
                Measure("overlay-alpha-render", () => render.Invoke(overlay, null));
            }

            string directory = Path.Combine(Path.GetTempPath(),
                "KRS-idle-probe-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                File.WriteAllText(Path.Combine(directory, "sbkernel2609.log"),
                    new string(' ', 4096) + "\r\n" +
                    "PILOT: card_authorize14: TRType=1, Amount=10000\r\n" +
                    "Command = 4000, Department = 1\r\n" +
                    "PILOT: card_authorize14: result=0, RC=0\r\n");
                using (var monitor = new BankLogMonitor(directory, null, null))
                {
                    monitor.RefreshNow();
                    Measure("unchanged-log", monitor.RefreshNow);
                }
            }
            finally
            {
                Directory.Delete(directory, true);
            }
            return 0;
        }

        private static void Measure(string name, Action action)
        {
            const int count = 200;
            for (int i = 0; i < 5; i++) action();
            using (Process process = Process.GetCurrentProcess())
            {
                TimeSpan cpuStart = process.TotalProcessorTime;
                var timer = Stopwatch.StartNew();
                for (int i = 0; i < count; i++) action();
                double cpuMs = (process.TotalProcessorTime - cpuStart).TotalMilliseconds;
                Console.WriteLine("{0}: calls={1}; wall={2:F1}ms; CPU={3:F1}ms; CPU/call={4:F3}ms",
                    name, count, timer.Elapsed.TotalMilliseconds, cpuMs, cpuMs / count);
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    }
}
