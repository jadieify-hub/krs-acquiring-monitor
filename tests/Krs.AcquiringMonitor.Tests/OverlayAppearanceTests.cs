using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Krs.AcquiringMonitor.Configuration;
using Krs.AcquiringMonitor.UI;

namespace Krs.AcquiringMonitor.Tests
{
    internal static class OverlayAppearanceTests
    {
        public static void WidthChangesWithoutChangingFont()
        {
            using (Form overlay = CreateOverlay())
            {
                MethodInfo setAppearance = overlay.GetType().GetMethod("SetAppearance");
                float fontSize = overlay.Controls[0].Font.Size;
                var grip = overlay.Controls[overlay.Controls.Count - 1];
                int originalWidth = overlay.Width;
                Mouse(grip, "OnMouseDown", 5);
                Mouse(grip, "OnMouseMove", 125);
                Mouse(grip, "OnMouseUp", 5);
                TestAssert.Equal(originalWidth + 120, overlay.Width);
                TestAssert.Equal(fontSize, overlay.Controls[0].Font.Size);
                setAppearance.Invoke(overlay, new object[] { 350, fontSize });
                TestAssert.True(overlay.Width < originalWidth, "Ширина должна уменьшаться.");
                TestAssert.Equal(fontSize, overlay.Controls[0].Font.Size);
                TestAssert.True(overlay.Controls[0].Right <= overlay.Controls[1].Left,
                    "Название не перекрывает сумму при сужении.");
                setAppearance.Invoke(overlay, new object[] { 650, AppSettings.MaximumOverlayFontSize });
                using (Graphics graphics = overlay.CreateGraphics())
                {
                    TestAssert.True(graphics.MeasureString(grip.Text, grip.Font).Width <= grip.Width,
                        "Край изменения ширины остаётся целым при максимальном шрифте.");
                }
                setAppearance.Invoke(overlay, new object[] { 250, 13f });
                TestAssert.Equal(250, overlay.Width);
                using (Graphics graphics = overlay.CreateGraphics())
                {
                    Control amount = overlay.Controls[1];
                    float textRight = amount.Left + graphics.MeasureString(amount.Text, amount.Font).Width;
                    TestAssert.True(grip.Left - textRight <= 12 * graphics.DpiX / 96f,
                        "После суммы нет пустой колонки или отдельной кнопки обновления.");
                }
            }
        }

        public static void RefreshesOnlyOnAmountDoubleClick()
        {
            using (Form overlay = CreateOverlay())
            {
                overlay.GetType().GetMethod("SetRows").Invoke(overlay, new object[]
                {
                    new List<OverlayRow>
                    {
                        new OverlayRow(1, "ИП Иванов", "1 475,00 ₽", false),
                        new OverlayRow(2, "ООО Пример", "237,00 ₽", false)
                    }
                });
                int requests = 0;
                overlay.GetType().GetEvent("RefreshRequested").AddEventHandler(
                    overlay, new EventHandler((sender, args) => requests++));
                Mouse(overlay.Controls[1], "OnMouseClick", 5);
                Mouse(overlay.Controls[0], "OnMouseDoubleClick", 5);
                Mouse(overlay.Controls[1], "OnMouseDoubleClick", 5, MouseButtons.Right);
                TestAssert.Equal(0, requests);
                Mouse(overlay.Controls[1], "OnMouseDoubleClick", 5);
                Mouse(overlay.Controls[3], "OnMouseDoubleClick", 5);
                TestAssert.Equal(2, requests);
                overlay.GetType().GetMethod("SetRefreshing").Invoke(overlay, new object[] { true });
                Mouse(overlay.Controls[1], "OnMouseDoubleClick", 5);
                TestAssert.Equal(2, requests);
                TestAssert.Equal("1 475,00 ₽", overlay.Controls[1].Text);
                TestAssert.Equal(Cursors.WaitCursor, overlay.Controls[1].Cursor);
                overlay.GetType().GetMethod("SetRefreshing").Invoke(overlay, new object[] { false });
                Mouse(overlay.Controls[1], "OnMouseDoubleClick", 5);
                TestAssert.Equal(3, requests);
            }
        }

        public static void CancelStopsUnfinishedDrag()
        {
            using (Form overlay = CreateOverlay())
            {
                int commits = 0;
                overlay.GetType().GetEvent("PositionCommitted").AddEventHandler(
                    overlay, new EventHandler((sender, args) => commits++));
                var name = overlay.Controls[0];
                Mouse(name, "OnMouseDown", 5);
                overlay.Location = new Point(123, 80);
                overlay.GetType().GetMethod("CancelDrag").Invoke(overlay, null);
                overlay.GetType().GetMethod("PlaceRelativeTo").Invoke(overlay,
                    new object[] { new Rectangle(0, 0, 1000, 700), 10, 20 });
                Mouse(name, "OnMouseUp", 5);
                TestAssert.Equal(new Point(10, 20), overlay.Location);
                TestAssert.Equal(0, commits);
            }
        }

        private static void Mouse(Control control, string method, int x, MouseButtons button = MouseButtons.Left)
        {
            typeof(Control).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(control, new object[] { new MouseEventArgs(button,
                    method == "OnMouseDoubleClick" ? 2 : 1, x, 10, 0) });
        }

        public static void PreviewDoesNotChangeSettingsUntilSaved()
        {
            var settings = AppSettings.CreateDefault();
            using (Form overlay = CreateOverlay())
            {
                Type settingsType = typeof(OverlayPresentation).Assembly.GetType(
                    "Krs.AcquiringMonitor.UI.SettingsForm", true);
                using (var editor = (Form)Activator.CreateInstance(settingsType,
                    BindingFlags.Instance | BindingFlags.NonPublic, null,
                    new object[] { settings, new int[0], overlay }, null))
                {
                    var width = (NumericUpDown)editor.Controls["OverlayWidth"];
                    var font = (NumericUpDown)editor.Controls["OverlayFontSize"];
                    width.Value = 650;
                    font.Value = 20;
                    TestAssert.Equal(20f, overlay.Controls[0].Font.Size);
                    TestAssert.Equal(470, settings.OverlayWidth);
                    TestAssert.Equal(15.5f, settings.OverlayFontSize);
                    Button save = (Button)editor.AcceptButton;
                    typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(save, new object[] { EventArgs.Empty });
                    TestAssert.Equal(650, settings.OverlayWidth);
                    TestAssert.Equal(20f, settings.OverlayFontSize);
                }
            }
        }

        internal static Form CreateOverlay()
        {
            Type type = typeof(OverlayPresentation).Assembly.GetType(
                "Krs.AcquiringMonitor.UI.OverlayForm", true);
            var overlay = (Form)Activator.CreateInstance(type);
            type.GetMethod("SetRows").Invoke(overlay, new object[]
            {
                new List<OverlayRow> { new OverlayRow(1, "ИП Иванов", "1 475,00 ₽", false) }
            });
            return overlay;
        }
    }
}
