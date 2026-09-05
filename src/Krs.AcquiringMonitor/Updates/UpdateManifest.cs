using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace Krs.AcquiringMonitor.Updates
{
    public sealed class ValidatedUpdate
    {
        internal ValidatedUpdate(
            Version version,
            string sha256,
            string fileName,
            string downloadUrl)
        {
            Version = version;
            Sha256 = sha256;
            FileName = fileName;
            DownloadUrl = downloadUrl;
        }

        public Version Version { get; private set; }

        public string Sha256 { get; private set; }

        public string FileName { get; private set; }

        public string DownloadUrl { get; private set; }
    }

    public static class UpdateManifest
    {
        private const string InstallerPrefix = "KRS-AcquiringMonitor-";
        private const string InstallerSuffix = "-setup.exe";
        private const string ReleaseBaseUrl =
            "https://github.com/jadieify-hub/krs-acquiring-monitor/releases/download/v";

        public static bool TrySelect(
            string json,
            Version currentVersion,
            out ValidatedUpdate update)
        {
            update = null;
            if (string.IsNullOrWhiteSpace(json) || currentVersion == null)
            {
                return false;
            }

            ManifestContract manifest;
            try
            {
                if (!HasExactContractFields(json))
                {
                    return false;
                }

                var serializer = new DataContractJsonSerializer(
                    typeof(ManifestContract));
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    manifest = serializer.ReadObject(stream) as ManifestContract;
                }
            }
            catch (SerializationException)
            {
                return false;
            }
            catch (XmlException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }

            Version version;
            if (manifest == null ||
                !Version.TryParse(manifest.Version, out version) ||
                version.Build < 0 ||
                version.Revision >= 0 ||
                !string.Equals(
                    manifest.Version,
                    version.ToString(3),
                    StringComparison.Ordinal) ||
                !IsValidSha256(manifest.Sha256))
            {
                return false;
            }

            var normalizedCurrent = new Version(
                currentVersion.Major,
                currentVersion.Minor,
                Math.Max(0, currentVersion.Build));
            if (version.CompareTo(normalizedCurrent) <= 0)
            {
                return false;
            }

            string versionText = version.ToString(3);
            string fileName = InstallerPrefix + versionText + InstallerSuffix;
            update = new ValidatedUpdate(
                version,
                manifest.Sha256.ToLowerInvariant(),
                fileName,
                ReleaseBaseUrl + versionText + "/" + fileName);
            return true;
        }

        public static bool HashMatches(string path, string expectedSha256)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !IsValidSha256(expectedSha256) ||
                !File.Exists(path))
            {
                return false;
            }

            try
            {
                byte[] hash;
                using (var stream = File.OpenRead(path))
                using (SHA256 algorithm = SHA256.Create())
                {
                    hash = algorithm.ComputeHash(stream);
                }

                var text = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                {
                    text.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                }

                return string.Equals(
                    text.ToString(),
                    expectedSha256,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        private static bool HasExactContractFields(string json)
        {
            bool hasVersion = false;
            bool hasSha256 = false;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            using (XmlDictionaryReader reader =
                JsonReaderWriterFactory.CreateJsonReader(
                    stream,
                    XmlDictionaryReaderQuotas.Max))
            {
                if (!reader.Read() ||
                    reader.NodeType != XmlNodeType.Element ||
                    reader.LocalName != "root" ||
                    reader.AttributeCount != 1 ||
                    reader.GetAttribute("type") != "object")
                {
                    return false;
                }

                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element ||
                        reader.Depth != 1)
                    {
                        continue;
                    }

                    if (reader.LocalName == "version" && !hasVersion)
                    {
                        hasVersion = true;
                    }
                    else if (reader.LocalName == "sha256" && !hasSha256)
                    {
                        hasSha256 = true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            return hasVersion && hasSha256;
        }

        private static bool IsValidSha256(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64)
            {
                return false;
            }

            foreach (char character in value)
            {
                if (!Uri.IsHexDigit(character))
                {
                    return false;
                }
            }

            return true;
        }

        [DataContract]
        private sealed class ManifestContract
        {
            [DataMember(Name = "version", IsRequired = true)]
            public string Version { get; set; }

            [DataMember(Name = "sha256", IsRequired = true)]
            public string Sha256 { get; set; }
        }
    }
}
