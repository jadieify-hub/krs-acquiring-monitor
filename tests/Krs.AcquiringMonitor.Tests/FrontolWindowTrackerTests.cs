using System;
using System.Drawing;
using Krs.AcquiringMonitor.Frontol;

namespace Krs.AcquiringMonitor.Tests
{
    internal static class FrontolWindowTrackerTests
    {
        public static void SelectsForegroundMainWindow()
        {
            var mainWindow = new FrontolWindowInfo(
                new IntPtr(1),
                new Rectangle(0, 0, 1920, 1080));

            FrontolWindowInfo selected = FrontolWindowTracker.SelectActiveMainWindow(
                mainWindow.Handle,
                mainWindow,
                "Frontol6",
                "Frontol v.6.28.8 Стандарт");

            TestAssert.Equal(mainWindow.Handle, selected.Handle);
        }

        public static void RejectsForegroundPopupFromFrontolProcess()
        {
            var mainWindow = new FrontolWindowInfo(
                new IntPtr(1),
                new Rectangle(0, 0, 1920, 1080));

            FrontolWindowInfo selected = FrontolWindowTracker.SelectActiveMainWindow(
                new IntPtr(2),
                mainWindow,
                "Frontol6",
                "Frontol v.6.28.8 Стандарт");

            TestAssert.True(
                selected == null,
                "Всплывающее окно Frontol не должно разрешать показ оверлея.");
        }

        public static void RejectsMissingMainWindow()
        {
            FrontolWindowInfo selected = FrontolWindowTracker.SelectActiveMainWindow(
                new IntPtr(2),
                null,
                "Frontol6",
                "Frontol v.6.28.8 Стандарт");

            TestAssert.True(
                selected == null,
                "Без главного окна Frontol оверлей должен оставаться скрытым.");
        }
    }
}
