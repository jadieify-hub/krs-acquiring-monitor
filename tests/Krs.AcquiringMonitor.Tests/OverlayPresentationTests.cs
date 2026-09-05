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

        public static void RendersWhiteTextWithoutColorKeyFringe()
        {
            Type labelType = typeof(OverlayPresentation).Assembly.GetType(
                "Krs.AcquiringMonitor.UI.OverlayTextLabel",
                false);
            TestAssert.True(
                labelType != null,
                "Оверлей должен использовать рендер текста без сглаживания по цветному ключу.");
            MethodInfo drawText = labelType.GetMethod(
                "DrawText",
                BindingFlags.Static | BindingFlags.NonPublic);
            TestAssert.True(drawText != null, "Не найден метод безопасной отрисовки текста.");

            using (var bitmap = new Bitmap(240, 40))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (var font = new Font("Segoe UI Semibold", 16f, FontStyle.Regular))
            {
                graphics.Clear(Color.Fuchsia);
                graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                drawText.Invoke(
                    null,
                    new object[]
                    {
                        graphics,
                        "Организация",
                        font,
                        Color.White,
                        new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                        ContentAlignment.MiddleLeft
                    });

                bool hasWhitePixel = false;
                for (int y = 0; y < bitmap.Height; y++)
                {
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        Color pixel = bitmap.GetPixel(x, y);
                        bool isBackground = pixel.ToArgb() == Color.Fuchsia.ToArgb();
                        bool isText = pixel.ToArgb() == Color.White.ToArgb();
                        hasWhitePixel = hasWhitePixel || isText;
                        TestAssert.True(
                            isBackground || isText,
                            "Белый текст не должен содержать цветные полупрозрачные пиксели.");
                    }
                }

                TestAssert.True(hasWhitePixel, "Текст должен быть отрисован.");
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
                TestAssert.Equal(268, nameWidth);
                TestAssert.Equal(276, amountLeft);
                TestAssert.Equal(160, amountWidth);
                TestAssert.True(
                    nameLeft + nameWidth <= amountLeft,
                    "Название и сумма не должны пересекаться.");
            }
            finally
            {
                ((IDisposable)form).Dispose();
            }
        }
    }
}
