using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Krs.AcquiringMonitor.Configuration
{
    [DataContract]
    public sealed class AppSettings
    {
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

        public static AppSettings CreateDefault()
        {
            return new AppSettings
            {
                OverlayOffsetX = 620,
                OverlayOffsetY = 55,
                HasCustomPosition = false,
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
