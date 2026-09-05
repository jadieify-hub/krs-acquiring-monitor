using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Krs.AcquiringMonitor.Configuration
{
    public static class UposLocator
    {
        public static string Find(string savedDirectory)
        {
            foreach (string candidate in Candidates(savedDirectory))
            {
                if (IsUposDirectory(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }

            return string.Empty;
        }

        public static bool IsUposDirectory(string directory)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(directory) &&
                       Directory.Exists(directory) &&
                       (File.Exists(Path.Combine(directory, "pilot_nt.dll")) ||
                        Directory.EnumerateFiles(directory, "sbkernel????.log").Any());
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static IEnumerable<string> Candidates(string savedDirectory)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] direct =
            {
                savedDirectory,
                AppDomain.CurrentDomain.BaseDirectory,
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Sberbank",
                    "Pilot_nt"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Sberbank",
                    "Pilot_nt")
            };

            foreach (string value in direct)
            {
                if (!string.IsNullOrWhiteSpace(value) && seen.Add(value))
                {
                    yield return value;
                }
            }

            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed)
                {
                    continue;
                }

                string candidate = Path.Combine(drive.RootDirectory.FullName, "SC552");
                if (seen.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }
    }
}
