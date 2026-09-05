using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using Krs.AcquiringMonitor.Core.Monitoring;

namespace Krs.AcquiringMonitor.Configuration
{
    [DataContract]
    public sealed class RuntimeState
    {
        public RuntimeState()
        {
            Departments = new List<RuntimeDepartment>();
        }

        [DataMember(Order = 1)]
        public List<RuntimeDepartment> Departments { get; set; }

        [DataMember(Order = 2)]
        public bool IsStale { get; set; }

        [DataMember(Order = 3)]
        public DateTime SavedUtc { get; set; }

        [DataMember(Order = 4)]
        public string ActiveLogFileName { get; set; }

        [DataMember(Order = 5)]
        public long ActiveLogOffset { get; set; }

        [DataMember(Order = 6)]
        public string ActiveLogPrefixHash { get; set; }

        [DataMember(Order = 7)]
        public string SourceDirectory { get; set; }

        public static bool CanPersistSnapshot(BankLogSnapshot snapshot)
        {
            return snapshot != null &&
                   !snapshot.IsStale &&
                   !snapshot.HasPendingOperation;
        }

        public static RuntimeState FromSnapshot(
            BankLogSnapshot snapshot,
            string activeLogFileName,
            long activeLogOffset,
            string activeLogPrefixHash,
            string sourceDirectory)
        {
            return new RuntimeState
            {
                Departments = snapshot.Totals
                    .OrderBy(item => item.Key)
                    .Select(item => new RuntimeDepartment
                    {
                        Department = item.Key,
                        AmountKopeks = item.Value
                    })
                    .ToList(),
                IsStale = snapshot.IsStale,
                SavedUtc = DateTime.UtcNow,
                ActiveLogFileName = activeLogFileName ?? string.Empty,
                ActiveLogOffset = Math.Max(0L, activeLogOffset),
                ActiveLogPrefixHash = activeLogPrefixHash ?? string.Empty,
                SourceDirectory = NormalizeDirectory(sourceDirectory)
            };
        }

        public bool MatchesSourceDirectory(string sourceDirectory)
        {
            string expected = NormalizeDirectory(SourceDirectory);
            string actual = NormalizeDirectory(sourceDirectory);
            return expected.Length > 0 &&
                   actual.Length > 0 &&
                   string.Equals(
                       expected,
                       actual,
                       StringComparison.OrdinalIgnoreCase);
        }

        public BankLogSnapshot ToSnapshot(bool forceStale)
        {
            var totals = Departments
                .Where(item => item != null && item.Department > 0)
                .GroupBy(item => item.Department)
                .ToDictionary(group => group.Key, group => group.Last().AmountKopeks);
            return BankLogSnapshot.FromTotals(totals, forceStale || IsStale);
        }

        private static string NormalizeDirectory(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(value.Trim())
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
            catch (NotSupportedException)
            {
                return string.Empty;
            }
            catch (PathTooLongException)
            {
                return string.Empty;
            }
        }
    }

    [DataContract]
    public sealed class RuntimeDepartment
    {
        [DataMember(Order = 1)]
        public int Department { get; set; }

        [DataMember(Order = 2)]
        public long AmountKopeks { get; set; }
    }
}
