using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Reflection;
using Krs.AcquiringMonitor.Core.Monitoring;
using Krs.AcquiringMonitor.Frontol;
using Krs.AcquiringMonitor.UI;

namespace Krs.AcquiringMonitor.Tests
{
    internal static class OverlayPresentationTests
    {
        public static void FormatsCurrencyWithTwoDecimals()
        {
            TestAssert.Equal("12 345,67 ₽", OverlayPresentation.FormatAmount(1234567L));
            TestAssert.Equal("0,00 ₽", OverlayPresentation.FormatAmount(0L));
        }

        public static void BuildsRowsForDiscoveredDepartments()
        {
            BankLogSnapshot snapshot = BankLogSnapshot.FromTotals(
                new Dictionary<int, long>
                {
                    { 1, 125000L },
                    { 2, 890000L }
                },
                false);
            IReadOnlyList<OverlayRow> rows = OverlayPresentation.BuildRows(
                snapshot,
                new Dictionary<int, string>
                {
                    { 1, "ИП Иванов" },
                    { 2, "ООО Колокольчик" }
                });

            TestAssert.Equal(2, rows.Count);
            TestAssert.Equal("ИП Иванов", rows[0].OrganizationName);
            TestAssert.Equal("1 250,00 ₽", rows[0].AmountText);
            TestAssert.Equal("ООО Колокольчик", rows[1].OrganizationName);
            TestAssert.Equal("8 900,00 ₽", rows[1].AmountText);
        }

        public static void UnknownAmountUsesDash()
        {
            IReadOnlyList<OverlayRow> rows = OverlayPresentation.BuildRows(
                BankLogSnapshot.FromTotals(new Dictionary<int, long>(), true),
                new Dictionary<int, string>());

            TestAssert.Equal(1, rows.Count);
            TestAssert.Equal("Организация", rows[0].OrganizationName);
            TestAssert.Equal("—", rows[0].AmountText);
            TestAssert.True(rows[0].IsStale, "Неизвестная сумма должна считаться устаревшей.");
        }

        public static void RecognizesFrontolWindowIdentity()
        {
            TestAssert.True(
                FrontolWindowTracker.IsFrontolIdentity("Frontol_Demo", "Frontol v.6.28.8 Демо"),
                "Окно Frontol должно распознаваться.");
            TestAssert.True(
                FrontolWindowTracker.IsFrontolIdentity(
                    string.Empty,
                    "АТОЛ, Frontol v.6.28.8 Стандарт"),
                "Заголовок Frontol должен быть резервным признаком при недоступном имени процесса.");
            TestAssert.False(
                FrontolWindowTracker.IsFrontolIdentity(
                    "Frontol",
                    "Frontol Администратор. Версия 6.28.8.87"),
                "Frontol Администратор не должен считаться кассой.");
            TestAssert.False(
                FrontolWindowTracker.IsFrontolIdentity("frontol6", "Касса"),
                "Одного имени процесса недостаточно для распознавания кассы.");
            TestAssert.False(
                FrontolWindowTracker.IsFrontolIdentity("explorer", "Frontol — инструкция"),
                "Постороннее окно со словом Frontol не должно считаться кассой.");
        }

        public static void UsesLargestVisibleFrontolSurfaceForPlacement()
        {
            var candidates = new List<FrontolWindowInfo>
            {
                new FrontolWindowInfo(new IntPtr(1), Rectangle.Empty),
                new FrontolWindowInfo(new IntPtr(2), new Rectangle(500, 250, 600, 500)),
                new FrontolWindowInfo(new IntPtr(3), new Rectangle(0, 28, 1920, 1012))
            };
            FrontolWindowInfo selected = FrontolWindowTracker.SelectAnchorWindow(candidates);

            TestAssert.Equal(new IntPtr(3), selected.Handle);
            TestAssert.Equal(new Rectangle(0, 28, 1920, 1012), selected.Bounds);
        }

        public static void RendersSmoothTextOnTransparentSurface()
        {
            using (var form = OverlayAppearanceTests.CreateOverlay())
            {
                MethodInfo render = form.GetType().GetMethod("RenderBitmap", BindingFlags.Instance | BindingFlags.NonPublic);
                TestAssert.True(render != null, "Оверлей должен рисовать сглаженную поверхность с альфа-каналом.");
                foreach (Color color in new[] { Color.White, Color.Black, Color.Fuchsia })
                {
                    form.GetType().GetMethod("SetColors").Invoke(form,
                        new object[] { color.ToArgb(), Color.DarkRed.ToArgb(), false });
                    using (var bitmap = (Bitmap)render.Invoke(form, null))
                    {
                        TestAssert.Equal(0, (int)bitmap.GetPixel(0, 0).A);
                        int smoothPixels = 0;
                        int opaquePixels = 0;
                        for (int y = 0; y < bitmap.Height; y++)
                        for (int x = 0; x < bitmap.Width; x++)
                        {
                            Color pixel = bitmap.GetPixel(x, y);
                            if (pixel.A == 0) continue;
                            if (pixel.A < 255) smoothPixels++;
                            else opaquePixels++;
                            // GetPixel unpremultiplies with one-level channel rounding (255 can read as 254).
                            TestAssert.True(Math.Abs(pixel.R - color.R) <= 1 &&
                                Math.Abs(pixel.G - color.G) <= 1 && Math.Abs(pixel.B - color.B) <= 1,
                                "Края текста имеют выбранный цвет: " + color + ", получен " + pixel + ".");
                        }
                        TestAssert.True(smoothPixels > 0, "Нужны полупрозрачные сглаженные края букв.");
                        TestAssert.True(opaquePixels > 0, "Основная часть букв должна оставаться непрозрачной.");
                    }
                }
                // Present the real surface to our own hidden HWND: no cash register or screen capture.
                form.GetType().GetMethod("RenderSurface", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(form, null);
            }
        }

        public static void UsesRegularOrganizationFont()
        {
            Type formType = typeof(OverlayPresentation).Assembly.GetType(
                "Krs.AcquiringMonitor.UI.OverlayForm",
                false);
            TestAssert.True(formType != null, "Не найдена форма оверлея.");

            object form = Activator.CreateInstance(formType);
            try
            {
                object controls = formType.GetProperty("Controls").GetValue(form, null);
                object nameControl = controls.GetType()
                    .GetProperty("Item", new[] { typeof(int) })
                    .GetValue(controls, new object[] { 0 });
                object amountControl = controls.GetType()
                    .GetProperty("Item", new[] { typeof(int) })
                    .GetValue(controls, new object[] { 1 });
                Font font = (Font)nameControl.GetType()
                    .GetProperty("Font")
                    .GetValue(nameControl, null);
                int nameLeft = (int)nameControl.GetType()
                    .GetProperty("Left")
                    .GetValue(nameControl, null);
                int nameWidth = (int)nameControl.GetType()
                    .GetProperty("Width")
                    .GetValue(nameControl, null);
                int amountLeft = (int)amountControl.GetType()
                    .GetProperty("Left")
                    .GetValue(amountControl, null);
                int amountWidth = (int)amountControl.GetType()
                    .GetProperty("Width")
                    .GetValue(amountControl, null);

                TestAssert.Equal("Segoe UI", font.Name);
                TestAssert.Equal(FontStyle.Regular, font.Style);
                TestAssert.Equal(470, (int)formType.GetProperty("Width").GetValue(form, null));
                TestAssert.True(
                    nameLeft + nameWidth <= amountLeft,
                    "Название и сумма не должны пересекаться.");
            }
            finally
            {
                ((IDisposable)form).Dispose();
            }
        }

        public static void AmountDoesNotOverlapResizeGrip()
        {
            Type formType = typeof(OverlayPresentation).Assembly.GetType(
                "Krs.AcquiringMonitor.UI.OverlayForm", true);
            using (var form = (System.Windows.Forms.Form)Activator.CreateInstance(formType))
            {
                formType.GetMethod("SetRows").Invoke(form, new object[]
                {
                    OverlayPresentation.BuildRows(
                        BankLogSnapshot.FromTotals(
                            new Dictionary<int, long> { { 1, -999999999L } }, false),
                        new Dictionary<int, string> { { 1, "ИП Иванов" } })
                });
                var amount = form.Controls[1];
                var grip = form.Controls[form.Controls.Count - 1];
                foreach (float dpi in new[] { 96f, 144f, 192f })
                using (var bitmap = new Bitmap(1200, 240))
                {
                    bitmap.SetResolution(dpi, dpi);
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                    {
                        formType.GetMethod("LayoutRows",
                            BindingFlags.Instance | BindingFlags.NonPublic,
                            null, new[] { typeof(Graphics) }, null)
                            .Invoke(form, new object[] { graphics });
                        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                        TestAssert.True(
                            graphics.MeasureString(amount.Text, amount.Font).Width <= amount.Width,
                            "Полная сумма с копейками и ₽ должна помещаться при DPI=" + dpi);
                        TestAssert.True(amount.Font.GetHeight(graphics) <= amount.Height,
                            "Сумма должна помещаться по высоте при DPI=" + dpi);
                        TestAssert.True(amount.Right < grip.Left,
                            "Сумма не должна залезать на край изменения ширины.");
                    }
                }
            }
        }

        public static void ReportResultsDoNotNeedPopupUi()
        {
            Type contextType = typeof(OverlayPresentation).Assembly.GetType("Krs.AcquiringMonitor.MonitorApplicationContext", true);
            MethodInfo report = contextType.GetMethod("SetRefreshResult", BindingFlags.Instance | BindingFlags.NonPublic);
            TestAssert.True(report != null, "Результат отчёта должен обновлять оверлей без всплывающего UI.");
            using (var overlay = OverlayAppearanceTests.CreateOverlay())
            {
                // Do not run the app constructor: it starts timers, autostart and bank monitoring.
                object context = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(contextType);
                contextType.GetField("_overlay", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(context, overlay);
                foreach (bool failed in new[] { false, true })
                {
                    report.Invoke(context, new object[] { "Результат отчёта", "Тестовое состояние", failed });
                    TestAssert.True(overlay.Controls[1].AccessibleDescription.Contains("Тестовое состояние"),
                        "Результат доступен в подсказке без объекта уведомлений трея.");
                    TestAssert.Equal(failed, overlay.Controls[1].ForeColor.ToArgb() != Color.White.ToArgb());
                    TestAssert.Equal("1 475,00 ₽", overlay.Controls[1].Text);
                }
            }
        }

        public static void RefreshFailureRemainsVisible()
        {
            Type formType = typeof(OverlayPresentation).Assembly.GetType(
                "Krs.AcquiringMonitor.UI.OverlayForm", true);
            using (var form = (System.Windows.Forms.Form)Activator.CreateInstance(formType))
            {
                var rows = new List<OverlayRow>
                {
                    new OverlayRow(1, "ИП Иванов", "1 475,00 ₽", false),
                    new OverlayRow(2, "ООО Пример", "237,00 ₽", false)
                };
                formType.GetMethod("SetRows").Invoke(form, new object[] { rows });
                formType.GetMethod("SetRefreshing").Invoke(form, new object[] { true });
                formType.GetMethod("SetRefreshStatus").Invoke(form, new object[] { "report-format", true });
                formType.GetMethod("SetRefreshing").Invoke(form, new object[] { false });
                formType.GetMethod("SetRows").Invoke(form, new object[] { rows });
                foreach (int index in new[] { 1, 3 })
                {
                    TestAssert.True((form.Controls[index].AccessibleDescription ?? "").Contains("report-format"),
                        "Причина ошибки доступна на каждой сумме после обновления строк.");
                    TestAssert.True(form.Controls[index].Enabled, "После ошибки можно повторить запрос.");
                    TestAssert.True(form.Controls[index].ForeColor != Color.White,
                        "Ошибка видна на суммах, даже если трей скрыт.");
                }
                TestAssert.Equal("1 475,00 ₽", form.Controls[1].Text);
                TestAssert.Equal("237,00 ₽", form.Controls[3].Text);
                TestAssert.Equal(Color.White, form.Controls[0].ForeColor);
                formType.GetMethod("SetRefreshStatus").Invoke(form, new object[] { "", false });
                TestAssert.Equal(Color.White, form.Controls[1].ForeColor);
                formType.GetMethod("SetDataStale").Invoke(form, new object[] { true });
                TestAssert.True(form.Controls[1].ForeColor != Color.White, "Устаревшие данные отмечены цветом.");
            }
        }
    }
}
