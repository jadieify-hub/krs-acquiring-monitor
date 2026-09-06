using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.Serialization;

namespace Krs.AcquiringMonitor.Configuration
{
    [DataContract]
    public sealed class AppSettings
    {
        public const int DefaultOverlayWidth = 470;
        public const int MinimumOverlayWidth = 200;
        public const int MaximumOverlayWidth = 1600;
        public const float DefaultOverlayFontSize = 15.5f;
        public const float MinimumOverlayFontSize = 8f;
        public const float MaximumOverlayFontSize = 32f;
        public const string DefaultOverlayFontFamily = "Segoe UI";
        public static readonly IReadOnlyList<string> OverlayFontFamilies =
            Array.AsReadOnly(new[] { DefaultOverlayFontFamily, "Arial", "Tahoma" });
        public static readonly Color DefaultOverlayTextColor = Color.White;
        public static readonly Color DefaultOverlayAttentionColor = Color.FromArgb(255, 190, 90);

        public AppSettings()
        {
            UposDirectory = string.Empty;
            Organizations = new List<OrganizationSetting>();
        }

        [DataMember(Order = 1)]
        public string UposDirectory { get; set; }

        [DataMember(Order = 2)]
        public int OverlayOffsetX { get; set; }

        [DataMember(Order = 3)]
        public int OverlayOffsetY { get; set; }

        [DataMember(Order = 4)]
        public bool HasCustomPosition { get; set; }

        [DataMember(Order = 5)]
        public bool AutoStart { get; set; }

        [DataMember(Order = 6)]
        public List<OrganizationSetting> Organizations { get; set; }

        [DataMember(Order = 7)]
        public int OverlayWidth { get; set; }

        [DataMember(Order = 8)]
        public float OverlayFontSize { get; set; }

        [DataMember(Order = 9)]
        public int OverlayTextColorArgb { get; set; }

        [DataMember(Order = 10)]
        public int OverlayAttentionColorArgb { get; set; }

        [DataMember(Order = 11)]
        public string OverlayFontFamily { get; set; }

        [DataMember(Order = 12)]
        public bool OverlayNamesBold { get; set; }

        [DataMember(Order = 13)]
        public bool? OverlayAmountsBold { get; set; }

        public static string NormalizeOverlayFontFamily(string family)
        {
            return OverlayFontFamilies.FirstOrDefault(value =>
                string.Equals(value, family, StringComparison.OrdinalIgnoreCase)) ?? DefaultOverlayFontFamily;
        }

        public static Color NormalizeOverlayColor(int argb, Color fallback)
        {
            Color color = Color.FromArgb(argb);
            return color.A == 255 ? color : fallback;
        }

        public static int NormalizeOverlayWidth(int value)
        {
            return value <= 0 ? DefaultOverlayWidth :
                Math.Max(MinimumOverlayWidth, Math.Min(MaximumOverlayWidth, value));
        }

        public static float NormalizeOverlayFontSize(float value)
        {
            return value <= 0 || float.IsNaN(value) || float.IsInfinity(value)
                ? DefaultOverlayFontSize
                : Math.Max(MinimumOverlayFontSize, Math.Min(MaximumOverlayFontSize, value));
        }

        public static AppSettings CreateDefault()
        {
            return new AppSettings
            {
                OverlayOffsetX = 620,
                OverlayOffsetY = 55,
                HasCustomPosition = false,
                OverlayWidth = DefaultOverlayWidth,
                OverlayFontSize = DefaultOverlayFontSize,
                OverlayFontFamily = DefaultOverlayFontFamily,
                OverlayAmountsBold = true,
                OverlayTextColorArgb = DefaultOverlayTextColor.ToArgb(),
                OverlayAttentionColorArgb = DefaultOverlayAttentionColor.ToArgb(),
                AutoStart = true
            };
        }

        public IReadOnlyDictionary<int, string> GetOrganizationNames()
        {
            return Organizations
                .Where(item => item != null && item.Department > 0)
                .GroupBy(item => item.Department)
                .ToDictionary(
                    group => group.Key,
                    group => group.Last().DisplayName ?? string.Empty);
        }

        public IReadOnlyDictionary<int, string> GetBankOrganizationNames()
        {
            return Organizations
                .Where(item => item != null && item.Department > 0)
                .GroupBy(item => item.Department)
                .ToDictionary(
                    group => group.Key,
                    group =>
                    {
                        OrganizationSetting item = group.Last();
                        if (!string.IsNullOrWhiteSpace(item.BankName))
                        {
                            return item.BankName.Trim();
                        }

                        return item.IsManual
                            ? string.Empty
                            : (item.DisplayName ?? string.Empty).Trim();
                    });
        }
    }

    [DataContract]
    public sealed class OrganizationSetting
    {
        [DataMember(Order = 1)]
        public int Department { get; set; }

        [DataMember(Order = 2)]
        public string DisplayName { get; set; }

        [DataMember(Order = 3)]
        public bool IsManual { get; set; }

        [DataMember(Order = 4)]
        public string BankName { get; set; }
    }
}
