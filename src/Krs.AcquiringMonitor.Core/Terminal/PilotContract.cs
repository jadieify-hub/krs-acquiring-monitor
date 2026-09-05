using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Krs.AcquiringMonitor.Core.Terminal
{
    public static class PilotContract
    {
        public const string ExportName = "_get_statistics";
        public const int ShortReportType = 0;
        public const int AuthAnswerSize32 = 35;
        public const ushort X86Machine = 0x014c;

        public static bool IsSafeToInvoke(
            string exportName,
            int pointerSize,
            int structureSize,
            ushort peMachine)
        {
            return string.Equals(exportName, ExportName, StringComparison.Ordinal) &&
                   pointerSize == 4 &&
                   structureSize == AuthAnswerSize32 &&
                   peMachine == X86Machine;
        }

        public static bool TryReadPeMachine(Stream stream, out ushort machine)
        {
            machine = 0;
            if (stream == null || !stream.CanRead || !stream.CanSeek || stream.Length < 0x40)
            {
                return false;
            }

            long originalPosition = stream.Position;
            try
            {
                stream.Position = 0;
                if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
                {
                    return false;
                }

                stream.Position = 0x3c;
                var offsetBytes = new byte[4];
                if (stream.Read(offsetBytes, 0, offsetBytes.Length) != offsetBytes.Length)
                {
                    return false;
                }

                int peOffset = BitConverter.ToInt32(offsetBytes, 0);
                if (peOffset < 0 || peOffset > stream.Length - 6)
                {
                    return false;
                }

                stream.Position = peOffset;
                if (stream.ReadByte() != 'P' ||
                    stream.ReadByte() != 'E' ||
                    stream.ReadByte() != 0 ||
                    stream.ReadByte() != 0)
                {
                    return false;
                }

                int low = stream.ReadByte();
                int high = stream.ReadByte();
                if (low < 0 || high < 0)
                {
                    return false;
                }

                machine = (ushort)(low | (high << 8));
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }
            finally
            {
                stream.Position = originalPosition;
            }
        }
    }

    public static class TerminalReceiptDecoder
    {
        public static string Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return string.Empty;
            }

            int length = Array.IndexOf(bytes, (byte)0);
            if (length < 0)
            {
                length = bytes.Length;
            }

            string windowsText = Encoding.GetEncoding(1251).GetString(bytes, 0, length);
            // Выбираем по русским меткам отчёта, а не по псевдографике разделителей.
            return Regex.IsMatch(
                windowsText,
                @"\b(?:ПАО|ООО|АО|ИП|СБЕРБАНК|ОТДЕЛ|ИТОГО)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                ? windowsText
                : Encoding.GetEncoding(866).GetString(bytes, 0, length);
        }
    }
}
