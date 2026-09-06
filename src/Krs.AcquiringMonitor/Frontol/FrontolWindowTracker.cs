using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace Krs.AcquiringMonitor.Frontol
{
    public sealed class FrontolWindowInfo
    {
        public FrontolWindowInfo(IntPtr handle, Rectangle bounds)
        {
            Handle = handle;
            Bounds = bounds;
        }

        public IntPtr Handle { get; private set; }

        public Rectangle Bounds { get; private set; }
    }

    public sealed class FrontolWindowTracker
    {
        private IntPtr _identityWindow;
        private uint _identityProcessId;
        private string _processName = string.Empty;

        public bool TryGetActive(out FrontolWindowInfo info)
        {
            info = null;
            IntPtr foregroundWindow = NativeMethods.GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero)
            {
                return false;
            }

            uint processId;
            NativeMethods.GetWindowThreadProcessId(foregroundWindow, out processId);
            string processName = string.Empty;
            string mainWindowTitle = string.Empty;
            try
            {
                using (Process process = Process.GetProcessById((int)processId))
                {
                    // On .NET Framework ProcessName enumerates system process information.
                    // The identity cannot change while this foreground HWND/PID pair stays alive.
                    if (_identityWindow != foregroundWindow || _identityProcessId != processId)
                    {
                        _processName = process.ProcessName;
                        _identityWindow = foregroundWindow;
                        _identityProcessId = processId;
                    }
                    processName = _processName;
                    mainWindowTitle = process.MainWindowTitle;
                }
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }

            if (string.IsNullOrWhiteSpace(mainWindowTitle))
            {
                mainWindowTitle = GetWindowTitle(foregroundWindow);
            }

            if (!IsFrontolIdentity(processName, mainWindowTitle))
            {
                return false;
            }

            info = FindActiveAnchorWindow(processId, foregroundWindow);
            return info != null;
        }

        private static FrontolWindowInfo FindActiveAnchorWindow(uint processId, IntPtr foregroundWindow)
        {
            if (!NativeMethods.IsWindowVisible(foregroundWindow) ||
                !NativeMethods.IsWindowEnabled(foregroundWindow) ||
                NativeMethods.IsIconic(foregroundWindow))
            {
                return null;
            }

            var className = new StringBuilder(256);
            NativeMethods.GetClassName(foregroundWindow, className, className.Capacity);
            // Frontol's real registration window is owned by TApplication, just like its dialogs.
            // ponytail: TfrmMain is verified on Frontol 6.28.8; add other classes only after a window probe.
            if (!string.Equals(className.ToString(), "TfrmMain", StringComparison.Ordinal))
            {
                return null;
            }

            // Visibility follows TfrmMain, but placement keeps the old background anchor and saved offsets.
            return SelectAnchorWindow(FindVisibleWindows(processId));
        }

        public static FrontolWindowInfo SelectAnchorWindow(
            IReadOnlyList<FrontolWindowInfo> windows)
        {
            FrontolWindowInfo selected = null;
            long selectedArea = 0;
            if (windows == null)
            {
                return null;
            }

            foreach (FrontolWindowInfo candidate in windows)
            {
                if (candidate == null)
                {
                    continue;
                }

                long area = (long)candidate.Bounds.Width * candidate.Bounds.Height;
                if (area > selectedArea)
                {
                    selected = candidate;
                    selectedArea = area;
                }
            }

            return selected;
        }

        public static bool IsFrontolIdentity(string processName, string title)
        {
            if (string.IsNullOrWhiteSpace(title) ||
                title.IndexOf(
                    "Frontol v.",
                    StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(processName) ||
                   processName.StartsWith(
                       "Frontol",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static IReadOnlyList<FrontolWindowInfo> FindVisibleWindows(
            uint processId)
        {
            var windows = new List<FrontolWindowInfo>();
            NativeMethods.EnumWindows(
                delegate(IntPtr window, IntPtr parameter)
                {
                    uint candidateProcessId;
                    NativeMethods.GetWindowThreadProcessId(
                        window,
                        out candidateProcessId);
                    if (candidateProcessId != processId ||
                        !NativeMethods.IsWindowVisible(window) ||
                        NativeMethods.IsIconic(window))
                    {
                        return true;
                    }

                    NativeRect rectangle;
                    if (NativeMethods.GetWindowRect(window, out rectangle) &&
                        rectangle.Right > rectangle.Left &&
                        rectangle.Bottom > rectangle.Top)
                    {
                        windows.Add(
                            new FrontolWindowInfo(
                                window,
                                Rectangle.FromLTRB(
                                    rectangle.Left,
                                    rectangle.Top,
                                    rectangle.Right,
                                    rectangle.Bottom)));
                    }

                    return true;
                },
                IntPtr.Zero);
            return windows;
        }

        private static string GetWindowTitle(IntPtr window)
        {
            var titleBuffer = new StringBuilder(512);
            NativeMethods.GetWindowText(
                window,
                titleBuffer,
                titleBuffer.Capacity);
            return titleBuffer.ToString();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private static class NativeMethods
        {
            internal delegate bool EnumWindowsProc(
                IntPtr window,
                IntPtr parameter);

            [DllImport("user32.dll")]
            internal static extern IntPtr GetForegroundWindow();

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool IsIconic(IntPtr window);

            [DllImport("user32.dll")]
            internal static extern uint GetWindowThreadProcessId(
                IntPtr window,
                out uint processId);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            internal static extern int GetWindowText(
                IntPtr window,
                StringBuilder text,
                int maximumCount);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            internal static extern int GetClassName(IntPtr window, StringBuilder text, int maximumCount);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool IsWindowEnabled(IntPtr window);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetWindowRect(
                IntPtr window,
                out NativeRect rectangle);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool IsWindowVisible(IntPtr window);
        }
    }
}
