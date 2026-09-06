using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Krs.AcquiringMonitor.Frontol;

namespace Krs.AcquiringMonitor.Tests
{
    internal static class FrontolWindowTrackerTests
    {
        public static void HidesForSearchAndReturnsToSameAnchor()
        {
            using (var owner = new Form { Text = "Frontol v.6.28.8 Стандарт" })
            using (var background = new TestWindow("TfrmBackGround", owner.Handle, new Rectangle(-30000, -30000, 1000, 800)))
            using (var main = new TestWindow("TfrmMain", owner.Handle, new Rectangle(-30000, -29972, 1000, 744)))
            using (var search = new TestWindow("TfrmVisualListWare", owner.Handle, new Rectangle(-29800, -30000, 800, 772)))
            {
                // Replay the observed HWND states without ever activating a test or cash-register window.
                EnableWindow(background.Handle, false);
                ShowWindow(search.Handle, 0);
                TestAssert.Equal(background.Handle, ReadActiveAnchor(main.Handle).Handle);
                EnableWindow(main.Handle, false);
                ShowWindow(search.Handle, 4);
                TestAssert.True(ReadActiveAnchor(search.Handle) == null,
                    "Визуальный поиск должен скрывать оверлей, хотя главное окно остаётся видимым.");
                TestAssert.True(ReadActiveAnchor(main.Handle) == null,
                    "Заблокированное главное окно не должно показывать оверлей.");
                EnableWindow(main.Handle, true);
                TestAssert.True(ReadActiveAnchor(search.Handle) == null,
                    "Активный диалог скрывает оверлей даже без блокировки главного окна.");
                search.Dispose();
                FrontolWindowInfo restored = ReadActiveAnchor(main.Handle);
                TestAssert.Equal(background.Handle, restored.Handle);
                TestAssert.Equal(new Rectangle(-30000, -30000, 1000, 800), restored.Bounds);
                ShowWindow(main.Handle, 0);
                TestAssert.True(ReadActiveAnchor(main.Handle) == null,
                    "Скрытое главное окно не должно показывать оверлей.");
            }
        }

        private static FrontolWindowInfo ReadActiveAnchor(IntPtr foreground)
        {
            using (Process process = Process.GetCurrentProcess())
            {
                var findAnchor = typeof(FrontolWindowTracker).GetMethod(
                    "FindActiveAnchorWindow", BindingFlags.NonPublic | BindingFlags.Static);
                return (FrontolWindowInfo)findAnchor.Invoke(null, new object[] { (uint)process.Id, foreground });
            }
        }

        public static void KeepsOwnedRegistrationSurface()
        {
            using (var owner = new Form { Text = "Frontol v.6.28.8 Стандарт" })
            using (var surface = new NonActivatingForm
            {
                Text = "Регистрация",
                StartPosition = FormStartPosition.Manual,
                Bounds = new Rectangle(-30000, -30000, 800, 600),
                ShowInTaskbar = false,
                Opacity = 0
            })
            using (Process process = Process.GetCurrentProcess())
            {
                IntPtr ownerHandle = owner.Handle;
                surface.Show(owner);

                // Test native enumeration without taking focus from the user's Frontol.
                var findWindows = typeof(FrontolWindowTracker).GetMethod(
                    "FindVisibleWindows", BindingFlags.NonPublic | BindingFlags.Static);
                var windows = (IReadOnlyList<FrontolWindowInfo>)findWindows.Invoke(
                    null, new object[] { (uint)process.Id });

                TestAssert.True(windows.Any(window => window.Handle == surface.Handle),
                    "Рабочая поверхность с владельцем и заголовком «Регистрация» не должна исчезать из кандидатов.");
                TestAssert.True(windows.All(window => window.Handle != ownerHandle),
                    "Скрытое служебное окно не должно становиться поверхностью оверлея.");
            }
        }

        private sealed class NonActivatingForm : Form
        {
            protected override bool ShowWithoutActivation { get { return true; } }
        }

        // Actual Win32 classes/owners reproduce Delphi's windows; nothing is shown on screen or activated.
        private sealed class TestWindow : IDisposable
        {
            private readonly string _className;
            private static readonly WindowProcedure Procedure = DefWindowProc;

            public TestWindow(string className, IntPtr owner, Rectangle bounds)
            {
                _className = className;
                var windowClass = new WindowClass { Name = className, Procedure = Procedure };
                if (RegisterClass(ref windowClass) == 0) throw new System.ComponentModel.Win32Exception();
                Handle = CreateWindowEx(0x08000080, className, string.Empty, 0x90000000,
                    bounds.X, bounds.Y, bounds.Width, bounds.Height, owner, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (Handle == IntPtr.Zero)
                {
                    UnregisterClass(className, IntPtr.Zero);
                    throw new System.ComponentModel.Win32Exception();
                }
            }

            public IntPtr Handle { get; private set; }

            public void Dispose()
            {
                if (Handle == IntPtr.Zero) return;
                DestroyWindow(Handle);
                Handle = IntPtr.Zero;
                UnregisterClass(_className, IntPtr.Zero);
            }
        }

        private delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WindowClass
        {
            public uint Style;
            public WindowProcedure Procedure;
            public int ClassExtra, WindowExtra;
            public IntPtr Instance, Icon, Cursor, Background;
            public string MenuName, Name;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClass(ref WindowClass windowClass);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(uint exStyle, string className, string title, uint style,
            int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool UnregisterClass(string className, IntPtr instance);
        [DllImport("user32.dll")]
        private static extern bool EnableWindow(IntPtr window, bool enable);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int command);

    }
}
