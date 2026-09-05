using System.IO;
using System.Text;
using Krs.AcquiringMonitor.Core.Terminal;

namespace Krs.AcquiringMonitor.Tests
{
    internal static class PilotContractTests
    {
        public static void AllowsOnlyVerifiedStatisticsContract()
        {
            TestAssert.True(
                PilotContract.IsSafeToInvoke("_get_statistics", 4, 35, 0x014c),
                "Точный x86-контракт статистики должен быть разрешён.");
            TestAssert.False(
                PilotContract.IsSafeToInvoke("_close_day", 4, 35, 0x014c),
                "Функция закрытия смены должна быть запрещена.");
            TestAssert.False(
                PilotContract.IsSafeToInvoke("_get_statistics", 8, 39, 0x8664),
                "64-разрядная DLL не должна загружаться x86-помощником.");
            TestAssert.False(
                PilotContract.IsSafeToInvoke("_get_statistics", 4, 36, 0x014c),
                "Несовпадающий размер структуры должен запрещать вызов.");
        }

        public static void ReadsX86PeMachine()
        {
            byte[] image = MinimalPeImage(0x014c);
            ushort machine;

            bool read = PilotContract.TryReadPeMachine(new MemoryStream(image), out machine);

            TestAssert.True(read, "Корректный PE-заголовок должен распознаваться.");
            TestAssert.Equal((ushort)0x014c, machine);
        }

        public static void RejectsMalformedPeFile()
        {
            ushort machine;
            bool read = PilotContract.TryReadPeMachine(
                new MemoryStream(Encoding.ASCII.GetBytes("not a dll")),
                out machine);

            TestAssert.False(read, "Произвольный файл не должен приниматься за DLL.");
        }

        public static void DecodesCp866Receipt()
        {
            byte[] encoded = Encoding.GetEncoding(866).GetBytes(
                "ООО Колокольчик\r\nИТОГО 1 250,00");

            string decoded = TerminalReceiptDecoder.Decode(encoded);

            TestAssert.Equal("ООО Колокольчик\r\nИТОГО 1 250,00", decoded);
        }

        public static void DecodesWindows1251Receipt()
        {
            byte[] encoded = Encoding.GetEncoding(1251).GetBytes(
                "ПАО СБЕРБАНК\r\nОтдел: ИП ПРИМЕРОВ\r\nИТОГО 125,50\0не часть отчёта");

            TestAssert.Equal(
                "ПАО СБЕРБАНК\r\nОтдел: ИП ПРИМЕРОВ\r\nИТОГО 125,50",
                TerminalReceiptDecoder.Decode(encoded));
        }

        private static byte[] MinimalPeImage(ushort machine)
        {
            var bytes = new byte[512];
            bytes[0] = (byte)'M';
            bytes[1] = (byte)'Z';
            bytes[0x3c] = 0x80;
            bytes[0x80] = (byte)'P';
            bytes[0x81] = (byte)'E';
            bytes[0x82] = 0;
            bytes[0x83] = 0;
            bytes[0x84] = (byte)(machine & 0xff);
            bytes[0x85] = (byte)(machine >> 8);
            return bytes;
        }
    }
}
