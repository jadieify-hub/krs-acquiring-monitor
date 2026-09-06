using System;
using System.Collections.Generic;

namespace Krs.AcquiringMonitor.Tests
{
    internal static class Program
    {
        private static readonly List<string> Failures = new List<string>();

        [STAThread]
        private static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            if (args.Length == 1 && args[0] == "--measure-idle")
            {
                return RuntimeDiagnostics.MeasureIdle();
            }
            if (args.Length == 2 && args[0] == "--render-preview")
            {
                return RuntimeDiagnostics.RenderPreview(args[1]);
            }
            if (args.Length == 2 && args[0] == "--render-settings")
            {
                return RuntimeDiagnostics.RenderSettingsPreview(args[1]);
            }
            Run("успешная покупка отдела 1", BankLogParserTests.SuccessfulPurchaseDepartment1);
            Run("успешная покупка отдела 2", BankLogParserTests.SuccessfulPurchaseDepartment2);
            Run("возврат уменьшает итог", BankLogParserTests.SuccessfulRefundSubtracts);
            Run("ошибка банка не меняет итог", BankLogParserTests.FailedTransactionIsIgnored);
            Run("незавершённая операция остаётся ожидающей", BankLogParserTests.IncompleteTransactionStaysPending);
            Run("одна организация обнуляется одним закрытием", BankLogParserTests.OneDepartmentCloseResets);
            Run("две организации ждут второе закрытие", BankLogParserTests.TwoDepartmentsWaitForSecondClose);
            Run("отдел без операций всё равно ждёт закрытия", BankLogParserTests.ConfiguredSecondDepartmentWithoutTransactionsStillNeedsSecondClose);
            Run("неполное закрытие сохраняет суммы", BankLogParserTests.IncompleteCloseKeepsTotalsStale);
            Run("статистика не завершает прерванное закрытие", BankLogParserTests.StatisticsDoesNotCompleteInterruptedClose);
            Run("ручной снимок становится новой базой", BankLogParserTests.AuthoritativeSnapshotBecomesNewBaseline);
            Run("полный снимок добавляет второй отдел", BankLogParserTests.AuthoritativeSnapshotCanAddSecondDepartment);
            Run("сверка между закрытиями отклоняется", BankLogParserTests.AuthoritativeSnapshotIsRejectedBetweenDepartmentCloses);
            Run("сокращается имя ИП", StatisticsReportParserTests.ShortensEntrepreneurName);
            Run("сокращается имя ООО", StatisticsReportParserTests.ShortensCompanyName);
            Run("разбирается итог одной организации", StatisticsReportParserTests.ParsesSingleOrganizationTotal);
            Run("разбираются две организации", StatisticsReportParserTests.ParsesTwoOrganizations);
            Run("контрольная лента UPOS декодируется и разбирается", StatisticsReportParserTests.ParsesControlTapeReceipt);
            Run("неполные и повторные итоги ленты отклоняются", StatisticsReportParserTests.RejectsIncompleteOrDuplicateControlTapeSummary);
            Run("непроверенные отмены ленты не меняют суммы", StatisticsReportParserTests.RejectsControlTapeWithUnverifiedCancellations);
            Run("итог вычисляется из оплаты и возврата", StatisticsReportParserTests.CalculatesTotalWhenExplicitTotalIsMissing);
            Run("неоднозначный итог отклоняется", StatisticsReportParserTests.RejectsConflictingTotals);
            Run("ошибка одной секции отклоняет весь отчёт", StatisticsReportParserTests.RejectsWholeReportWhenASectionIsInvalid);
            Run("заголовок ПАО СБЕРБАНК не считается организацией", StatisticsReportParserTests.IgnoresSberbankLegalHeader);
            Run("две суммы в строке итога отклоняются", StatisticsReportParserTests.RejectsTotalLineWithTwoAmounts);
            Run("повторённая строка итога отклоняется", StatisticsReportParserTests.RejectsRepeatedEqualTotalLines);
            Run("обнаруженное имя возвращается для запоминания", StatisticsReportParserTests.ReturnsDetectedNameForLearning);
            Run("отчёт сопоставляется по названиям", StatisticsReportParserTests.MatchesDepartmentsByConfiguredNames);
            Run("частичный отчёт не применяется", StatisticsReportParserTests.RejectsPartialReportAtomically);
            Run("одна неизвестная секция не задаёт состав организаций", StatisticsReportParserTests.UnknownSingleReportDoesNotDefineExpectedOrganizationCount);
            Run("две полные секции обнаруживают оба отдела", StatisticsReportParserTests.CompleteTwoSectionReportCanDiscoverBothDepartments);
            Run("переход месяца сохраняет итоги", MonthRolloverTests.PreservesTotalsAcrossMonthlyFiles);
            Run("перезапуск восстанавливает те же итоги", MonthRolloverTests.RestartRebuildsSameTotals);
            Run("недоступная папка возвращает устаревшее значение", MonthRolloverTests.MissingDirectoryUsesStaleFallback);
            Run("настройки сохраняются в профиле приложения", MonthRolloverTests.SettingsRoundTrip);
            Run("старые настройки сохраняют привычный размер", MonthRolloverTests.OldSettingsKeepDefaultAppearance);
            Run("банковское имя отделено от подписи", MonthRolloverTests.KeepsBankIdentitySeparateFromDisplayName);
            Run("нестабильное состояние не затирает контрольную точку", MonthRolloverTests.RejectsUnstableRuntimeCheckpoint);
            Run("устаревший ручной снимок не перезаписывает оплату", MonthRolloverTests.RejectsManualSnapshotAfterLogChanged);
            Run("нулевое изменение журнала инвалидирует ручной снимок", MonthRolloverTests.RejectsManualSnapshotAfterNetZeroLogActivity);
            Run("служебные строки статистики не отклоняют её отчёт", MonthRolloverTests.AllowsManualSnapshotAfterStatisticsOnlyLogActivity);
            Run("непрочитанные строки блокируют ручной снимок", MonthRolloverTests.RejectsManualSnapshotWhenLogHasUnreadBytes);
            Run("ручной снимок без журнала отклоняется", MonthRolloverTests.RejectsManualSnapshotWithoutLogAnchor);
            Run("ручная база продолжается с сохранённой позиции", MonthRolloverTests.ManualSnapshotResumesFromSavedOffset);
            Run("перезапуск между закрытиями завершает обнуление", MonthRolloverTests.RestartBetweenDepartmentClosesCompletesReset);
            Run("состояние привязано к каталогу UPOS", MonthRolloverTests.RuntimeStateIsBoundToUposDirectory);
            Run("исчезновение активного журнала не повторяет старый месяц", MonthRolloverTests.MissingActiveLogKeepsLastSnapshotStale);
            Run("заменённый активный журнал перечитывается", MonthRolloverTests.ReplacedActiveLogIsRebuilt);
            Run("разрешён только проверенный контракт статистики", PilotContractTests.AllowsOnlyVerifiedStatisticsContract);
            Run("распознаётся x86 PE-файл", PilotContractTests.ReadsX86PeMachine);
            Run("повреждённый PE-файл отклоняется", PilotContractTests.RejectsMalformedPeFile);
            Run("чек CP866 декодируется в Unicode", PilotContractTests.DecodesCp866Receipt);
            Run("чек Windows-1251 декодируется в Unicode", PilotContractTests.DecodesWindows1251Receipt);
            Run("путь helper-а не искажается", TerminalStatisticsClientTests.QuotesWindowsPathWithoutChangingSeparators);
            Run("остатки временных отчётов удаляются", TerminalStatisticsClientTests.RemovesStaleTemporaryReports);
            Run("автоимена запрашиваются не чаще десяти минут", AutomaticNameRefreshPolicyTests.WaitsUntilDueAndRetriesAfterTenMinutes);
            Run("новая версия выбирается с ожидаемым установщиком", UpdateManifestTests.SelectsNewerVersionAndDerivesInstaller);
            Run("невалидные манифесты обновления отклоняются", UpdateManifestTests.RejectsInvalidAndNonNewerManifests);
            Run("манифест содержит ровно два поля", UpdateManifestTests.RequiresExactTwoFieldObject);
            Run("SHA-256 установщика проверяется", UpdateManifestTests.VerifiesInstallerSha256);
            Run("изменённый ожидающий установщик не запускается", UpdateManifestTests.RejectsInstallerChangedWhileWaiting);
            Run("обновления повторяются и ждут безопасной паузы", UpdateScheduleTests.RepeatsChecksAndWaitsForIdle);
            Run("сумма оверлея имеет две копейки", OverlayPresentationTests.FormatsCurrencyWithTwoDecimals);
            Run("оверлей показывает обнаруженные организации", OverlayPresentationTests.BuildsRowsForDiscoveredDepartments);
            Run("неизвестная сумма показывается прочерком", OverlayPresentationTests.UnknownAmountUsesDash);
            Run("распознаётся только окно Frontol", OverlayPresentationTests.RecognizesFrontolWindowIdentity);
            Run("оверлей привязан к самой большой форме Frontol", OverlayPresentationTests.UsesLargestVisibleFrontolSurfaceForPlacement);
            Run("рабочая поверхность с владельцем не теряется", FrontolWindowTrackerTests.KeepsOwnedRegistrationSurface);
            Run("поиск скрывает оверлей и не меняет привязку после закрытия", FrontolWindowTrackerTests.HidesForSearchAndReturnsToSameAnchor);
            Run("сглаженный текст имеет прозрачный фон без цветной каймы", OverlayPresentationTests.RendersSmoothTextOnTransparentSurface);
            Run("названия организаций отображаются обычным шрифтом", OverlayPresentationTests.UsesRegularOrganizationFont);
            Run("полная сумма не обрезается и не перекрывает край", OverlayPresentationTests.AmountDoesNotOverlapResizeGrip);
            Run("ошибка сверки остаётся доступной после запроса", OverlayPresentationTests.RefreshFailureRemainsVisible);
            Run("результат отчёта доступен без всплывающего UI", OverlayPresentationTests.ReportResultsDoNotNeedPopupUi);
            Run("ширина меняется без изменения шрифта", OverlayAppearanceTests.WidthChangesWithoutChangingFont);
            Run("обновление только двойным щелчком по сумме", OverlayAppearanceTests.RefreshesOnlyOnAmountDoubleClick);
            Run("отмена прекращает незаконченный жест без сохранения", OverlayAppearanceTests.CancelStopsUnfinishedDrag);
            Run("предпросмотр не сохраняет настройки до подтверждения", OverlayAppearanceTests.PreviewDoesNotChangeSettingsUntilSaved);
            Run("выбранные цвета сохраняются при обновлении оверлея", OverlayAppearanceTests.CustomColorsSurviveRefreshAndResize);
            Run("шрифт и жирность сохраняются только по подтверждению", OverlayAppearanceTests.FontPreviewSavesOnlyOnConfirmation);
            Run("предпросмотр цветов сохраняется только по подтверждению", OverlayAppearanceTests.ColorPreviewSavesOnlyOnConfirmation);
            Run("старые и невидимые цвета заменяются прежними цветами", OverlayAppearanceTests.OldOrInvisibleColorsUseVisibleDefaults);
            Run("форма показывает утверждённую страницу CloudTips", SupportConfigurationTests.DisplaysApprovedCloudTipsPage);

            if (Failures.Count == 0)
            {
                Console.WriteLine("Все тесты пройдены.");
                return 0;
            }

            Console.Error.WriteLine("Провалено тестов: " + Failures.Count);
            return 1;
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine("PASS  " + name);
            }
            catch (Exception exception)
            {
                Failures.Add(name);
                Console.Error.WriteLine("FAIL  " + name + ": " + exception);
            }
        }
    }
}
