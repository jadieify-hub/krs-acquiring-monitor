using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Krs.AcquiringMonitor.Core.Terminal;

namespace Krs.AcquiringMonitor.TerminalQuery
{
    internal sealed class PilotQueryResult
    {
        private PilotQueryResult(bool success, int exitCode, string report, string error)
        {
            Success = success;
            ExitCode = exitCode;
            Report = report ?? string.Empty;
            Error = error ?? string.Empty;
        }

        public bool Success { get; private set; }

        public int ExitCode { get; private set; }

        public string Report { get; private set; }

        public string Error { get; private set; }

        public static PilotQueryResult Ok(string report)
        {
            return new PilotQueryResult(true, 0, report, string.Empty);
        }

        public static PilotQueryResult Fail(int exitCode, string error)
        {
            return new PilotQueryResult(false, exitCode, string.Empty, error);
        }
    }

    internal static class PilotNtInterop
    {
        private const int MaxReportBytes = 1024 * 1024;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct AuthAnswer
        {
            public int TType;
            public uint Amount;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public byte[] RCode;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] AMessage;

            public int CType;
            public IntPtr Check;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetStatisticsDelegate(ref AuthAnswer answer);

        public static PilotQueryResult Query(string directory)
        {
            string dllPath = Path.Combine(directory, "pilot_nt.dll");
            if (!File.Exists(dllPath))
            {
                return PilotQueryResult.Fail(3, "pilot_nt.dll не найден в выбранной папке.");
            }

            ushort machine;
            using (var stream = new FileStream(
                dllPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                if (!PilotContract.TryReadPeMachine(stream, out machine))
                {
                    return PilotQueryResult.Fail(4, "Файл pilot_nt.dll имеет некорректный формат.");
                }
            }

            int structureSize = Marshal.SizeOf(typeof(AuthAnswer));
            if (!PilotContract.IsSafeToInvoke(
                    PilotContract.ExportName,
                    IntPtr.Size,
                    structureSize,
                    machine))
            {
                return PilotQueryResult.Fail(
                    4,
                    "Разрядность или контракт pilot_nt.dll не совпадает с безопасным x86-контрактом.");
            }

            IntPtr library = NativeMethods.LoadLibrary(dllPath);
            if (library == IntPtr.Zero)
            {
                return PilotQueryResult.Fail(
                    5,
                    "Не удалось загрузить pilot_nt.dll. Код Windows: " +
                    Marshal.GetLastWin32Error().ToString());
            }

            var answer = new AuthAnswer
            {
                TType = PilotContract.ShortReportType,
                Amount = 0,
                RCode = new byte[3],
                AMessage = new byte[16],
                CType = 0,
                Check = IntPtr.Zero
            };

            try
            {
                IntPtr address = NativeMethods.GetProcAddress(
                    library,
                    PilotContract.ExportName);
                if (address == IntPtr.Zero)
                {
                    return PilotQueryResult.Fail(
                        6,
                        "В pilot_nt.dll отсутствует безопасная функция _get_statistics.");
                }

                var function = (GetStatisticsDelegate)Marshal.GetDelegateForFunctionPointer(
                    address,
                    typeof(GetStatisticsDelegate));
                int result = function(ref answer);
                if (result != 0)
                {
                    return PilotQueryResult.Fail(
                        7,
                        "Терминал вернул ошибку " + result.ToString() + ".");
                }

                byte[] reportBytes = CopyNullTerminated(answer.Check);
                string report = TerminalReceiptDecoder.Decode(reportBytes).TrimEnd('\0');
                if (string.IsNullOrWhiteSpace(report))
                {
                    return PilotQueryResult.Fail(8, "Терминал не вернул текст текущего отчёта.");
                }

                return PilotQueryResult.Ok(report);
            }
            catch (SEHException)
            {
                return PilotQueryResult.Fail(
                    9,
                    "Компонент Сбербанка аварийно завершил запрос.");
            }
            catch (AccessViolationException)
            {
                return PilotQueryResult.Fail(
                    9,
                    "Компонент Сбербанка вернул некорректные данные.");
            }
            finally
            {
                if (answer.Check != IntPtr.Zero)
                {
                    NativeMethods.GlobalFree(answer.Check);
                }

                NativeMethods.FreeLibrary(library);
            }
        }

        private static byte[] CopyNullTerminated(IntPtr pointer)
        {
            if (pointer == IntPtr.Zero)
            {
                return new byte[0];
            }

            int length = 0;
            while (length < MaxReportBytes && Marshal.ReadByte(pointer, length) != 0)
            {
                length++;
            }

            if (length == MaxReportBytes)
            {
                throw new InvalidDataException("Отчёт превышает допустимый размер.");
            }

            var bytes = new byte[length];
            if (length > 0)
            {
                Marshal.Copy(pointer, bytes, 0, length);
            }

            return bytes;
        }

        private static class NativeMethods
        {
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern IntPtr LoadLibrary(string fileName);

            [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
            internal static extern IntPtr GetProcAddress(IntPtr module, string procedureName);

            [DllImport("kernel32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool FreeLibrary(IntPtr module);

            [DllImport("kernel32.dll")]
            internal static extern IntPtr GlobalFree(IntPtr memory);
        }
    }
}
