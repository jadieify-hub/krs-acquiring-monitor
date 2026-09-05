using System;
using System.IO;
using System.Text;
using Krs.AcquiringMonitor.Updates;

namespace Krs.AcquiringMonitor.Tests
{
    internal static class UpdateManifestTests
    {
        private const string ValidHash =
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        public static void SelectsNewerVersionAndDerivesInstaller()
        {
            ValidatedUpdate update;
            bool selected = UpdateManifest.TrySelect(
                "{\"version\":\"0.2.0\",\"sha256\":\"" + ValidHash + "\"}",
                new Version(0, 1, 0),
                out update);

            TestAssert.True(selected, "Новая корректная версия должна выбираться.");
            TestAssert.Equal("0.2.0", update.Version.ToString(3));
            TestAssert.Equal(
                "KRS-AcquiringMonitor-0.2.0-setup.exe",
                update.FileName);
            TestAssert.Equal(
                "https://github.com/jadieify-hub/krs-acquiring-monitor/releases/download/v0.2.0/KRS-AcquiringMonitor-0.2.0-setup.exe",
                update.DownloadUrl);
        }

        public static void RejectsInvalidAndNonNewerManifests()
        {
            ValidatedUpdate update;
            TestAssert.False(
                UpdateManifest.TrySelect(
                    "{\"version\":\"0.1.0\",\"sha256\":\"" + ValidHash + "\"}",
                    new Version(0, 1, 0),
                    out update),
                "Текущая версия не должна устанавливаться повторно.");
            TestAssert.False(
                UpdateManifest.TrySelect(
                    "{\"version\":\"0.0.9\",\"sha256\":\"" + ValidHash + "\"}",
                    new Version(0, 1, 0),
                    out update),
                "Старая версия не должна выбираться.");
            TestAssert.False(
                UpdateManifest.TrySelect(
                    "{\"version\":\"0.2.0.0\",\"sha256\":\"" + ValidHash + "\"}",
                    new Version(0, 1, 0),
                    out update),
                "Версия должна состоять ровно из трёх компонентов.");
            TestAssert.False(
                UpdateManifest.TrySelect(
                    "{\"version\":\"0.2.0\",\"sha256\":\"1234\"}",
                    new Version(0, 1, 0),
                    out update),
                "Некорректный SHA-256 должен отклоняться.");
            TestAssert.False(
                UpdateManifest.TrySelect(
                    "{\"version\":\"0.2.0\",\"sha256\":\"" + ValidHash + "\",\"url\":\"https://example.test/setup.exe\"}",
                    new Version(0, 1, 0),
                    out update),
                "Лишние поля манифеста должны отклоняться.");
            TestAssert.False(
                UpdateManifest.TrySelect(
                    "{broken",
                    new Version(0, 1, 0),
                    out update),
                "Повреждённый JSON должен отклоняться.");
        }

        public static void RequiresExactTwoFieldObject()
        {
            ValidatedUpdate update;
            TestAssert.True(
                UpdateManifest.TrySelect(
                    "{\"sha256\":\"" + ValidHash + "\",\"version\":\"0.2.0\"}",
                    new Version(0, 1, 0),
                    out update),
                "Порядок двух обязательных полей не должен иметь значения.");
            TestAssert.False(
                UpdateManifest.TrySelect(
                    "{\"version\":\"0.2.0\",\"version\":\"0.3.0\",\"sha256\":\"" + ValidHash + "\"}",
                    new Version(0, 1, 0),
                    out update),
                "Повторённые поля должны отклоняться.");
            TestAssert.False(
                UpdateManifest.TrySelect(
                    "{\"__type\":\"UpdateManifest.ManifestContract:#Krs.AcquiringMonitor.Updates\",\"version\":\"0.2.0\",\"sha256\":\"" + ValidHash + "\"}",
                    new Version(0, 1, 0),
                    out update),
                "Служебное поле __type должно отклоняться.");
            TestAssert.False(
                UpdateManifest.TrySelect(
                    "[]",
                    new Version(0, 1, 0),
                    out update),
                "Манифест должен быть JSON-объектом.");
        }

        public static void VerifiesInstallerSha256()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "KRS-AcquiringMonitor-hash-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(path, "abc", Encoding.ASCII);

            try
            {
                TestAssert.True(
                    UpdateManifest.HashMatches(
                        path,
                        "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD"),
                    "Известный SHA-256 файла должен совпасть.");
                TestAssert.False(
                    UpdateManifest.HashMatches(
                        path,
                        "AA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD"),
                    "Изменённый SHA-256 должен отклоняться.");
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
