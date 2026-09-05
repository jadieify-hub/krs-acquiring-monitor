using System.Reflection;

namespace Krs.AcquiringMonitor
{
    internal static class AppConstants
    {
        private static readonly System.Version CurrentVersion = ReadVersion();

        public const string ApplicationName = "KRS Эквайринг Монитор";
        public const string Description =
            "Настраиваемый оверлей сумм эквайринга по организациям для Frontol 6 и Сбербанк UPOS.";
        public const string Developer = "Руслан Керусов";
        public const string Publisher = "KRS";
        public const string SupportUrl = "https://pay.cloudtips.ru/p/2f23e8c9";
        public const string AutoStartValueName = "KRS Acquiring Monitor";

        public static string Version
        {
            get { return CurrentVersion.ToString(3); }
        }

        public static System.Version ApplicationVersion
        {
            get { return CurrentVersion; }
        }

        private static System.Version ReadVersion()
        {
            System.Version version = Assembly
                .GetExecutingAssembly()
                .GetName()
                .Version;
            return new System.Version(
                version.Major,
                version.Minor,
                System.Math.Max(0, version.Build));
        }
    }
}
