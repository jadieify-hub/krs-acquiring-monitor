using System;
using System.IO;
using System.Runtime.Serialization.Json;

namespace Krs.AcquiringMonitor.Configuration
{
    public sealed class SettingsStore
    {
        private readonly string _baseDirectory;

        public SettingsStore()
            : this(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KRS",
                "AcquiringMonitor"))
        {
        }

        public SettingsStore(string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                throw new ArgumentException("Не указан каталог настроек.", "baseDirectory");
            }

            _baseDirectory = baseDirectory;
        }

        public string BaseDirectory
        {
            get { return _baseDirectory; }
        }

        public AppSettings LoadSettings()
        {
            AppSettings settings = Load<AppSettings>(Path.Combine(_baseDirectory, "settings.json"));
            if (settings == null)
            {
                return AppSettings.CreateDefault();
            }

            if (settings.Organizations == null)
            {
                settings.Organizations = new System.Collections.Generic.List<OrganizationSetting>();
            }

            settings.UposDirectory = settings.UposDirectory ?? string.Empty;
            settings.OverlayWidth = AppSettings.NormalizeOverlayWidth(settings.OverlayWidth);
            settings.OverlayFontSize = AppSettings.NormalizeOverlayFontSize(settings.OverlayFontSize);
            settings.OverlayFontFamily = AppSettings.NormalizeOverlayFontFamily(settings.OverlayFontFamily);
            settings.OverlayAmountsBold = settings.OverlayAmountsBold ?? true;
            settings.OverlayTextColorArgb = AppSettings.NormalizeOverlayColor(
                settings.OverlayTextColorArgb, AppSettings.DefaultOverlayTextColor).ToArgb();
            settings.OverlayAttentionColorArgb = AppSettings.NormalizeOverlayColor(
                settings.OverlayAttentionColorArgb, AppSettings.DefaultOverlayAttentionColor).ToArgb();
            return settings;
        }

        public void SaveSettings(AppSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            Save(Path.Combine(_baseDirectory, "settings.json"), settings);
        }

        public RuntimeState LoadRuntimeState()
        {
            return Load<RuntimeState>(Path.Combine(_baseDirectory, "state.json"));
        }

        public void SaveRuntimeState(RuntimeState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException("state");
            }

            Save(Path.Combine(_baseDirectory, "state.json"), state);
        }

        private static T Load<T>(string path) where T : class
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                using (var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read))
                {
                    return new DataContractJsonSerializer(typeof(T)).ReadObject(stream) as T;
                }
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
            catch (System.Runtime.Serialization.SerializationException)
            {
                return null;
            }
        }

        private static void Save<T>(string path, T value)
        {
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            string temporaryPath = path + ".tmp";
            string backupPath = path + ".bak";

            using (var stream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                stream.Flush(true);
            }

            if (!File.Exists(path))
            {
                File.Move(temporaryPath, path);
                return;
            }

            try
            {
                File.Replace(temporaryPath, path, backupPath, true);
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }
            }
            catch (PlatformNotSupportedException)
            {
                File.Copy(temporaryPath, path, true);
                File.Delete(temporaryPath);
            }
        }
    }
}
