using System;
using System.Text.RegularExpressions;

namespace Krs.AcquiringMonitor.Core.Reports
{
    public static class OrganizationNameShortener
    {
        private static readonly Regex LegalNamePattern = new Regex(
            @"\b(?<form>ПАО|ООО|АО|ИП)\s+(?<name>.+)$",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant |
            RegexOptions.IgnoreCase);

        private static readonly Regex WhitespacePattern = new Regex(
            @"\s+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string Shorten(string value)
        {
            string normalized = Normalize(value);
            Match match = LegalNamePattern.Match(normalized);
            if (!match.Success)
            {
                return normalized;
            }

            string form = match.Groups["form"].Value.ToUpperInvariant();
            string remainder = CleanupPunctuation(match.Groups["name"].Value);
            if (remainder.Length == 0)
            {
                return form;
            }

            string[] words = remainder.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string firstWord = words.Length == 0 ? string.Empty : CleanupPunctuation(words[0]);
            return firstWord.Length == 0 ? form : form + " " + firstWord;
        }

        internal static bool TryExtractLegalName(string line, out string legalName)
        {
            string normalized = Normalize(line);
            Match match = LegalNamePattern.Match(normalized);
            if (!match.Success)
            {
                legalName = null;
                return false;
            }

            string form = match.Groups["form"].Value.ToUpperInvariant();
            string remainder = CleanupPunctuation(match.Groups["name"].Value);
            if (remainder.Length == 0)
            {
                legalName = null;
                return false;
            }

            legalName = form + " " + remainder;
            return true;
        }

        internal static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string withoutQuotes = value
                .Replace('«', ' ')
                .Replace('»', ' ')
                .Replace('"', ' ')
                .Replace('„', ' ')
                .Replace('“', ' ');
            return WhitespacePattern.Replace(withoutQuotes, " ").Trim();
        }

        private static string CleanupPunctuation(string value)
        {
            return Normalize(value).Trim(' ', ':', ';', ',', '.', '-', '—', '(', ')', '[', ']');
        }
    }
}
