using Krs.AcquiringMonitor.Core.Monitoring;

namespace Krs.AcquiringMonitor.Tests
{
    internal static class BankLogParserTests
    {
        public static void SuccessfulPurchaseDepartment1()
        {
            var parser = new BankLogParser();

            Purchase(parser, 1, 10500, true);

            TestAssert.Equal(10500L, parser.Snapshot.Totals[1]);
            TestAssert.False(parser.HasPendingOperation, "Завершённая покупка не должна оставаться ожидающей.");
        }

        public static void SuccessfulPurchaseDepartment2()
        {
            var parser = new BankLogParser();

            parser.ProcessLine("04.09 11:49:07.127 PILOT: card_authorize14: track2=(null), TRType=1, CType=0, Amount=91500");
            parser.ProcessLine("04.09 11:49:07.127 SBKRNL: Command = 4000, Amount = 915.00, Department = 2");
            parser.ProcessLine("04.09 11:49:15.515 PILOT: card_authorize14: result=0, RC=0, cheque=Yes, vas=0");

            TestAssert.Equal(91500L, parser.Snapshot.Totals[2]);
        }

        public static void SuccessfulRefundSubtracts()
        {
            var parser = new BankLogParser();
            Purchase(parser, 1, 50000, true);

            parser.ProcessLine("04.09 12:00:00.000 PILOT: card_authorize14: track2=(null), TRType=3, CType=0, Amount=12500");
            parser.ProcessLine("04.09 12:00:00.010 SBKRNL: Command = 4002, Amount = 125.00, Department = 1");
            parser.ProcessLine("04.09 12:00:04.000 PILOT: card_authorize14: result=0, RC=00, cheque=Yes, vas=0");

            TestAssert.Equal(37500L, parser.Snapshot.Totals[1]);
        }

        public static void FailedTransactionIsIgnored()
        {
            var parser = new BankLogParser();

            parser.ProcessLine("04.09 13:41:25.192 PILOT: card_authorize14: track2=(null), TRType=1, CType=0, Amount=21500");
            parser.ProcessLine("04.09 13:41:25.208 SBKRNL: Command = 4000, Amount = 215.00, Department = 1");
            parser.ProcessLine("04.09 13:41:37.704 PILOT: card_authorize14: result=4451, RC=51, cheque=No, vas=0");

            TestAssert.Equal(0L, parser.Snapshot.Totals[1]);
        }

        public static void IncompleteTransactionStaysPending()
        {
            var parser = new BankLogParser();

            parser.ProcessLine("04.09 14:00:00.000 PILOT: card_authorize14: track2=(null), TRType=1, CType=0, Amount=7000");
            parser.ProcessLine("04.09 14:00:00.010 SBKRNL: Command = 4000, Amount = 70.00, Department = 1");

            TestAssert.True(parser.HasPendingOperation, "Операция без финального результата должна оставаться ожидающей.");
            TestAssert.Equal(0L, parser.Snapshot.Totals[1]);
        }

        public static void OneDepartmentCloseResets()
        {
            var parser = new BankLogParser();
            Purchase(parser, 1, 10000, true);

            Close(parser, true);

            TestAssert.Equal(0L, parser.Snapshot.Totals[1]);
            TestAssert.False(parser.Snapshot.IsStale, "Полное закрытие одной организации должно дать актуальный ноль.");
        }

        public static void TwoDepartmentsWaitForSecondClose()
        {
            var parser = ParserWithTwoDepartments();

            Close(parser, true);
            TestAssert.Equal(10000L, parser.Snapshot.Totals[1]);
            TestAssert.Equal(20000L, parser.Snapshot.Totals[2]);
            TestAssert.True(parser.Snapshot.IsStale, "После первого из двух закрытий состояние должно быть помечено устаревшим.");

            Close(parser, true);
            TestAssert.Equal(0L, parser.Snapshot.Totals[1]);
            TestAssert.Equal(0L, parser.Snapshot.Totals[2]);
            TestAssert.False(parser.Snapshot.IsStale, "После полного закрытия нули должны быть актуальными.");
        }

        public static void ConfiguredSecondDepartmentWithoutTransactionsStillNeedsSecondClose()
        {
            var parser = new BankLogParser(new[] { 1, 2 });
            Purchase(parser, 1, 10000, true);

            Close(parser, true);

            TestAssert.Equal(10000L, parser.Snapshot.Totals[1]);
            TestAssert.True(
                parser.Snapshot.IsStale,
                "Первое из двух ожидаемых закрытий не должно обнулять смену.");

            Close(parser, true);

            TestAssert.Equal(0L, parser.Snapshot.Totals[1]);
            TestAssert.Equal(0L, parser.Snapshot.Totals[2]);
            TestAssert.False(
                parser.Snapshot.IsStale,
                "Два успешных закрытия должны обнулить оба ожидаемых отдела.");
        }

        public static void IncompleteCloseKeepsTotalsStale()
        {
            var parser = ParserWithTwoDepartments();

            Close(parser, true);
            Close(parser, false);

            TestAssert.Equal(10000L, parser.Snapshot.Totals[1]);
            TestAssert.Equal(20000L, parser.Snapshot.Totals[2]);
            TestAssert.True(parser.Snapshot.IsStale, "Неуспешное второе закрытие не должно обнулять смену.");
        }

        public static void AuthoritativeSnapshotBecomesNewBaseline()
        {
            var parser = new BankLogParser();
            Purchase(parser, 1, 10000, true);

            bool replaced = parser.TryReplaceTotals(
                new System.Collections.Generic.Dictionary<int, long>
                {
                    { 1, 50000L }
                });
            Purchase(parser, 1, 2500, true);

            TestAssert.True(replaced, "Полный ручной снимок должен заменять рассчитанную базу.");
            TestAssert.Equal(52500L, parser.Snapshot.Totals[1]);
            TestAssert.False(parser.Snapshot.IsStale, "Ручной снимок терминала должен быть актуальным.");
        }

        public static void AuthoritativeSnapshotCanAddSecondDepartment()
        {
            var parser = new BankLogParser();
            Purchase(parser, 1, 10000, true);

            bool replaced = parser.TryReplaceTotals(
                new System.Collections.Generic.Dictionary<int, long>
                {
                    { 1, 50000L },
                    { 2, 25000L }
                });

            TestAssert.True(
                replaced,
                "Полный отчёт должен уметь добавить ещё не использованный второй отдел.");
            TestAssert.Equal(50000L, parser.Snapshot.Totals[1]);
            TestAssert.Equal(25000L, parser.Snapshot.Totals[2]);
        }

        public static void AuthoritativeSnapshotIsRejectedBetweenDepartmentCloses()
        {
            var parser = ParserWithTwoDepartments();
            Close(parser, true);

            bool replaced = parser.TryReplaceTotals(
                new System.Collections.Generic.Dictionary<int, long>
                {
                    { 1, 10000L },
                    { 2, 20000L }
                });
            Close(parser, true);

            TestAssert.False(
                replaced,
                "Ручная сверка не должна сбрасывать прогресс общего закрытия.");
            TestAssert.Equal(0L, parser.Snapshot.Totals[1]);
            TestAssert.Equal(0L, parser.Snapshot.Totals[2]);
        }

        public static void StatisticsDoesNotCompleteInterruptedClose()
        {
            var parser = new BankLogParser();
            Purchase(parser, 1, 10000, true);
            parser.ProcessLine("01.09 10:00:00.000 PILOT: close_day.");
            parser.ProcessLine("01.09 10:00:00.010 SBKRNL: Command = 6000");
            long revision = parser.ActivityVersion;
            parser.ProcessLine("01.09 10:02:00.000 PILOT: close_day.");
            parser.ProcessLine("01.09 10:02:00.010 SBKRNL: Command = 7000");
            parser.ProcessLine("01.09 10:02:00.100 PILOT: close_day: result=0, RC=0");
            TestAssert.Equal(10000L, parser.Snapshot.Totals[1]);
            TestAssert.True(parser.HasPendingOperation,
                "Успешная статистика не подтверждает прерванное закрытие смены.");
            TestAssert.Equal(revision, parser.ActivityVersion);
        }

        private static BankLogParser ParserWithTwoDepartments()
        {
            var parser = new BankLogParser();
            Purchase(parser, 1, 10000, true);
            Purchase(parser, 2, 20000, true);
            return parser;
        }

        private static void Purchase(BankLogParser parser, int department, long amountKopeks, bool successful)
        {
            parser.ProcessLine(
                string.Format(
                    "04.09 10:00:00.000 PILOT: card_authorize14: track2=(null), TRType=1, CType=0, Amount={0}",
                    amountKopeks));
            parser.ProcessLine(
                string.Format(
                    "04.09 10:00:00.010 SBKRNL: Command = 4000, Amount = 0.00, Department = {0}",
                    department));
            parser.ProcessLine(
                successful
                    ? "04.09 10:00:05.000 PILOT: card_authorize14: result=0, RC=0, cheque=Yes, vas=0"
                    : "04.09 10:00:05.000 PILOT: card_authorize14: result=4451, RC=51, cheque=No, vas=0");
        }

        private static void Close(BankLogParser parser, bool successful)
        {
            parser.ProcessLine("04.09 21:50:35.600 PILOT: close_day. Version: 34.09.03");
            parser.ProcessLine("04.09 21:50:35.647 SBKRNL: Command = 6000");
            parser.ProcessLine(
                successful
                    ? "04.09 21:50:42.030 PILOT: close_day: result=0, RC=0"
                    : "04.09 21:50:42.030 PILOT: close_day: result=99, RC=99");
        }
    }
}
