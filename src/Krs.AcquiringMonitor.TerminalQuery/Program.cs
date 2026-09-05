using System;
using System.IO;
using System.Text;

namespace Krs.AcquiringMonitor.TerminalQuery
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);

            string directory;
            string output;
            if (!TryReadArguments(args, out directory, out output))
            {
                Console.Error.WriteLine(
                    "Использование: --directory <папка UPOS> --output <файл отчёта>.");
                return 2;
            }

            try
            {
                PilotQueryResult result = PilotNtInterop.Query(directory);
                if (!result.Success)
                {
                    Console.Error.WriteLine(result.Error);
                    return result.ExitCode;
                }

                string outputDirectory = Path.GetDirectoryName(Path.GetFullPath(output));
                Directory.CreateDirectory(outputDirectory);
                File.WriteAllText(output, result.Report, new UTF8Encoding(false));
                return 0;
            }
            catch (IOException)
            {
                Console.Error.WriteLine("Не удалось сохранить временный отчёт.");
                return 10;
            }
            catch (UnauthorizedAccessException)
            {
                Console.Error.WriteLine("Нет доступа к временному файлу отчёта.");
                return 10;
            }
        }

        private static bool TryReadArguments(
            string[] args,
            out string directory,
            out string output)
        {
            directory = null;
            output = null;
            if (args == null)
            {
                return false;
            }

            for (int index = 0; index < args.Length - 1; index += 2)
            {
                if (string.Equals(args[index], "--directory", StringComparison.Ordinal))
                {
                    directory = args[index + 1];
                }
                else if (string.Equals(args[index], "--output", StringComparison.Ordinal))
                {
                    output = args[index + 1];
                }
                else
                {
                    return false;
                }
            }

            return args.Length == 4 &&
                   !string.IsNullOrWhiteSpace(directory) &&
                   !string.IsNullOrWhiteSpace(output);
        }
    }
}
