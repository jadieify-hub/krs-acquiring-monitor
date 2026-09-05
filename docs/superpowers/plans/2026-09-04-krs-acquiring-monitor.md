# KRS Эквайринг Монитор Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Собрать готовую x86 WinForms-утилиту, которая безопасно показывает поверх Frontol текущие суммы Сбербанк-эквайринга отдельно по организациям.

**Architecture:** Чистое ядро последовательно разбирает технические строки sbkernelYYMM.log и хранит итог по отделам. WinForms-процесс наблюдает за журналом, состоянием Frontol и прозрачным оверлеем; отдельный x86-помощник изолированно вызывает только _get_statistics и возвращает текст текущего отчёта.

**Tech Stack:** C#; WinForms; .NET Framework 4.8; SDK-style projects; x86; только библиотеки BCL; PowerShell для сборки и упаковки; Inno Setup script без банковских файлов.

**Spec:** docs/superpowers/specs/2026-09-04-krs-acquiring-monitor-design.md

## Global Constraints

- Поддерживаются Windows 10/11, Frontol 6 и только Сбербанк UPOS через 32-разрядную pilot_nt.dll.
- Процессы приложения и помощника собираются как x86 под .NET Framework 4.8.
- Запрещены вызовы close_day, LoadParm.exe, оплаты, возврата, отмены и любые изменения файлов UPOS.
- SC552, банковские DLL и реальные журналы не входят в репозиторий и дистрибутив.
- Оверлей показывает только одну или две строки «организация + сумма» и кнопку ↻, без общего итога.
- Диагностический лог не содержит исходных отчётов, PAN, RRN, кодов авторизации или текста чеков.
- Ссылка поддержки хранится один раз: https://pay.cloudtips.ru/p/2f23e8c9.
- Никаких сервисов Windows, БД, web-интерфейса, телеметрии и зависимостей NuGet.

---

### Task 1: Solution skeleton and executable test harness

**Files:**
- Create: Krs.AcquiringMonitor.sln
- Create: Directory.Build.props
- Create: src/Krs.AcquiringMonitor.Core/Krs.AcquiringMonitor.Core.csproj
- Create: src/Krs.AcquiringMonitor/Krs.AcquiringMonitor.csproj
- Create: src/Krs.AcquiringMonitor.TerminalQuery/Krs.AcquiringMonitor.TerminalQuery.csproj
- Create: tests/Krs.AcquiringMonitor.Tests/Krs.AcquiringMonitor.Tests.csproj
- Create: tests/Krs.AcquiringMonitor.Tests/Program.cs
- Create: tests/Krs.AcquiringMonitor.Tests/TestAssert.cs

**Interfaces:**
- Produces four buildable projects and a dependency-free console test runner returning exit code 0 only when every registered test passes.

- [x] **Step 1: Create the solution and project files**

Set all projects to net48, deterministic builds, metadata KRS Эквайринг Монитор / KRS / Руслан Керусов / 0.1.0. Set both executable projects to PlatformTarget=x86.

- [x] **Step 2: Write the first failing test registration**

~~~csharp
Run("успешная покупка отдела 1", BankLogParserTests.SuccessfulPurchaseDepartment1);
~~~

The runner catches exceptions, prints one concise line per scenario and returns 1 when any scenario fails.

- [x] **Step 3: Run the red test**

Run: dotnet build Krs.AcquiringMonitor.sln -c Release -p:Platform=x86

Expected: FAIL because BankLogParserTests does not exist yet.

- [x] **Step 4: Commit**

~~~powershell
git add Krs.AcquiringMonitor.sln Directory.Build.props src tests
git commit -m "build: создать каркас решения и тестовый запускатель"
~~~

### Task 2: Parse sbkernel operations and shift closure

**Files:**
- Create: src/Krs.AcquiringMonitor.Core/Monitoring/DepartmentTotal.cs
- Create: src/Krs.AcquiringMonitor.Core/Monitoring/BankLogSnapshot.cs
- Create: src/Krs.AcquiringMonitor.Core/Monitoring/BankLogParser.cs
- Create: tests/Krs.AcquiringMonitor.Tests/BankLogParserTests.cs

**Interfaces:**
- Produces BankLogParser.ProcessLine(string), BankLogParser.Snapshot and BankLogParser.HasPendingOperation.
- BankLogSnapshot.Totals is a read-only copy keyed by department; amounts are signed long kopeks.
- BankLogSnapshot.IsStale becomes true after only part of the expected close sequence succeeds.

- [x] **Step 1: Write failing behavioral tests**

Use exact sanitized lines from the supplied log:

~~~csharp
parser.ProcessLine("04.09 11:49:07.127 PILOT: card_authorize14: track2=(null), TRType=1, CType=0, Amount=91500");
parser.ProcessLine("04.09 11:49:07.127 SBKRNL: Command = 4000, Amount = 915.00, Department = 2");
parser.ProcessLine("04.09 11:49:15.515 PILOT: card_authorize14: result=0, RC=0, cheque=Yes, vas=0");
TestAssert.Equal(91500L, parser.Snapshot.Totals[2]);
~~~

Add focused scenarios for departments 1 and 2, successful TRType=3 subtraction, failed result ignored, incomplete operation pending, one-department close reset, two-department close reset only after two successes, and incomplete two-department close retaining totals as stale.

- [x] **Step 2: Run tests and verify failure**

Run: dotnet build Krs.AcquiringMonitor.sln -c Release -p:Platform=x86

Expected: FAIL because parser types are absent.

- [x] **Step 3: Implement the minimal state machine**

Recognize these exact technical shapes:

~~~csharp
private static readonly Regex StartPattern = new Regex(
    @"card_authorize14:.*TRType=(?<type>\d+).*Amount=(?<amount>\d+)",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);
private static readonly Regex CommandPattern = new Regex(
    @"Command\s*=\s*(?<command>4000|4002).*Department\s*=\s*(?<department>\d+)",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);
private static readonly Regex ResultPattern = new Regex(
    @"card_authorize14:\s*result=(?<result>-?\d+),\s*RC=(?<rc>[^,\s]+)",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);
~~~

Track one pending operation because UPOS serializes terminal work. Commit an amount only for result 0 and RC 0/00. Track successful close_day calls by count; clear all totals only when the count reaches the number of departments already observed. Any card operation resets an incomplete close counter.

- [x] **Step 4: Run tests**

Run: dotnet run --project tests/Krs.AcquiringMonitor.Tests/Krs.AcquiringMonitor.Tests.csproj -c Release -p:Platform=x86

Expected: every registered parser scenario prints PASS.

- [x] **Step 5: Commit**

~~~powershell
git add src/Krs.AcquiringMonitor.Core tests/Krs.AcquiringMonitor.Tests
git commit -m "feat: рассчитывать итоги смены из sbkernel"
~~~

### Task 3: Parse organization reports and apply them atomically

**Files:**
- Create: src/Krs.AcquiringMonitor.Core/Reports/OrganizationReport.cs
- Create: src/Krs.AcquiringMonitor.Core/Reports/OrganizationNameShortener.cs
- Create: src/Krs.AcquiringMonitor.Core/Reports/StatisticsReportParser.cs
- Create: src/Krs.AcquiringMonitor.Core/Reports/StatisticsSnapshotMerger.cs
- Create: tests/Krs.AcquiringMonitor.Tests/StatisticsReportParserTests.cs

**Interfaces:**
- Produces OrganizationNameShortener.Shorten(string).
- Produces StatisticsReportParser.Parse(string) returning ordered IReadOnlyList<OrganizationReport>.
- Produces StatisticsSnapshotMerger.TryMerge(IReadOnlyList<int>, IReadOnlyDictionary<int,string>, IReadOnlyList<OrganizationReport>, out IReadOnlyDictionary<int,DepartmentTotal>).

- [x] **Step 1: Write failing report tests**

~~~csharp
TestAssert.Equal("ИП Иванов", OrganizationNameShortener.Shorten("ИП Иванов Иван Иванович"));
TestAssert.Equal("ООО Колокольчик", OrganizationNameShortener.Shorten("ООО «Колокольчик»"));
~~~

Use two sanitized report fixtures containing organization headings, ОПЛАТА, ВОЗВРАТ and ИТОГО. Test one and two organizations, spaces/comma/dot in amounts, exact-total preference, purchase-minus-refund fallback, manual-name priority, complete atomic merge and rejection of a partial two-organization report.

- [x] **Step 2: Run tests and verify failure**

Run: dotnet build Krs.AcquiringMonitor.sln -c Release -p:Platform=x86

Expected: FAIL because report types are absent.

- [x] **Step 3: Implement conservative parsing**

Start a section only on a trimmed legal-form line matching ИП, ООО, АО or ПАО. Within a section prefer a uniquely parsed line containing ИТОГО and one monetary value. If absent, accept exactly one purchase value and zero or one refund value. Reject conflicting totals.

Normalize 12 345,67, 12345.67 and 12 345.67 to kopeks using decimal/integer arithmetic; never use binary floating point.

- [x] **Step 4: Implement atomic merge**

Require report count to equal expected department count. Match unique shortened report names to unique configured names first; map remaining sections in report order to remaining sorted departments. Return false without changing caller state on duplicates, missing totals or count mismatch.

- [x] **Step 5: Run all core tests and commit**

~~~powershell
dotnet run --project tests/Krs.AcquiringMonitor.Tests/Krs.AcquiringMonitor.Tests.csproj -c Release -p:Platform=x86
git add src/Krs.AcquiringMonitor.Core/Reports tests/Krs.AcquiringMonitor.Tests
git commit -m "feat: разбирать отчёт статистики по организациям"
~~~

### Task 4: Monitor monthly files and persist safe state

**Files:**
- Create: src/Krs.AcquiringMonitor/Configuration/AppSettings.cs
- Create: src/Krs.AcquiringMonitor/Configuration/SettingsStore.cs
- Create: src/Krs.AcquiringMonitor/Configuration/UposLocator.cs
- Create: src/Krs.AcquiringMonitor/Diagnostics/SafeLogger.cs
- Create: src/Krs.AcquiringMonitor/Monitoring/BankLogMonitor.cs
- Create: tests/Krs.AcquiringMonitor.Tests/MonthRolloverTests.cs

**Interfaces:**
- Produces BankLogMonitor.Start(), BankLogMonitor.RefreshNow(), SnapshotChanged and HasPendingOperation.
- Produces settings in %LOCALAPPDATA%\KRS\AcquiringMonitor\settings.json and runtime state in state.json.

- [x] **Step 1: Write failing rollover and restart tests**

Create temporary sanitized sbkernel2608.log and sbkernel2609.log; place a successful purchase before rollover and another after it. Assert both are counted, a completed two-call close resets both, and a saved snapshot is returned stale when the directory becomes unavailable.

- [x] **Step 2: Run and verify failure**

Run: dotnet run --project tests/Krs.AcquiringMonitor.Tests/Krs.AcquiringMonitor.Tests.csproj -c Release -p:Platform=x86

Expected: FAIL because monitoring/persistence types are absent.

- [x] **Step 3: Implement shared-read tailing**

On startup parse the newest two files matching sbkernel plus four digits plus .log in name order, then remember newest path and byte offset. Read appended bytes with FileShare.ReadWrite and FileShare.Delete, retain an unterminated final line, and process completed lines only. Use FileSystemWatcher as a wake signal plus a one-second System.Threading.Timer fallback. Rebuild after truncation/replacement.

- [x] **Step 4: Implement settings, locator and sanitized logging**

Serialize through DataContractJsonSerializer with an atomic temporary-file replace in application data. Locator order: saved directory, executable directory, SC552 at fixed-drive roots and common Sberbank program directories. Logger accepts only event code, safe message and exception type; its API does not accept raw report text.

- [x] **Step 5: Run tests and commit**

~~~powershell
dotnet run --project tests/Krs.AcquiringMonitor.Tests/Krs.AcquiringMonitor.Tests.csproj -c Release -p:Platform=x86
git add src/Krs.AcquiringMonitor tests/Krs.AcquiringMonitor.Tests
git commit -m "feat: следить за журналом и сохранять состояние"
~~~

### Task 5: Isolated _get_statistics helper

**Files:**
- Create: src/Krs.AcquiringMonitor.Core/Terminal/PilotContract.cs
- Create: src/Krs.AcquiringMonitor.TerminalQuery/Program.cs
- Create: src/Krs.AcquiringMonitor.TerminalQuery/PilotNtInterop.cs
- Create: src/Krs.AcquiringMonitor/Terminal/TerminalStatisticsClient.cs
- Create: tests/Krs.AcquiringMonitor.Tests/PilotContractTests.cs

**Interfaces:**
- Helper arguments: --directory <UPOS folder> --output <UTF-8 report file>.
- Exit 0 means a non-empty report was written; nonzero codes distinguish invalid arguments, incompatible process/DLL, missing export, terminal error and empty report.
- Produces TerminalStatisticsClient.QueryAsync(string, TimeSpan, CancellationToken).

- [x] **Step 1: Write a failing ABI test**

~~~csharp
TestAssert.Equal(35, PilotContract.AuthAnswerSize32);
TestAssert.Equal("_get_statistics", PilotContract.ExportName);
TestAssert.Equal(0, PilotContract.ShortReportType);
~~~

- [x] **Step 2: Run and verify failure**

Run: dotnet build Krs.AcquiringMonitor.sln -c Release -p:Platform=x86

Expected: FAIL because PilotContract is absent.

- [x] **Step 3: Implement the exact safe contract**

~~~csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct AuthAnswer
{
    public int TType;
    public uint Amount;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)] public byte[] RCode;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] AMessage;
    public int CType;
    public IntPtr Check;
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
private delegate int GetStatisticsDelegate(ref AuthAnswer answer);
~~~

Reject unless IntPtr.Size is 4, Marshal.SizeOf(AuthAnswer) is 35, PE machine is 0x014c and GetProcAddress resolves exactly _get_statistics. Zero all fields and set TType=0; copy at most 1 MiB from NUL-terminated Check; free it through GlobalFree in finally; unload the DLL. Never resolve another pilot export.

- [x] **Step 4: Implement the parent client**

Start the helper with UseShellExecute=false, CreateNoWindow=true, UPOS working directory and a unique file under %TEMP%\KRS-AcquiringMonitor. Kill only that helper after timeout. Read once, delete transient data and return a typed result without logging the report.

- [x] **Step 5: Verify without contacting a terminal**

Run helper against an empty temporary directory and a copied non-DLL file; assert deterministic nonzero exits. Do not execute against SC552 on the development machine.

- [x] **Step 6: Run tests and commit**

~~~powershell
dotnet run --project tests/Krs.AcquiringMonitor.Tests/Krs.AcquiringMonitor.Tests.csproj -c Release -p:Platform=x86
git add src tests
git commit -m "feat: безопасно запрашивать текущую статистику UPOS"
~~~

### Task 6: Transparent Frontol overlay and settings UI

**Files:**
- Create: src/Krs.AcquiringMonitor/Program.cs
- Create: src/Krs.AcquiringMonitor/AppConstants.cs
- Create: src/Krs.AcquiringMonitor/MonitorApplicationContext.cs
- Create: src/Krs.AcquiringMonitor/Frontol/FrontolWindowTracker.cs
- Create: src/Krs.AcquiringMonitor/UI/OverlayForm.cs
- Create: src/Krs.AcquiringMonitor/UI/ShadowTextControl.cs
- Create: src/Krs.AcquiringMonitor/UI/SettingsForm.cs
- Create: src/Krs.AcquiringMonitor/UI/SupportForm.cs
- Create: src/Krs.AcquiringMonitor/UI/AboutForm.cs
- Create: src/Krs.AcquiringMonitor/Configuration/AutoStartManager.cs

**Interfaces:**
- Produces a single-instance tray application and an overlay relative to the active Frontol window.
- AppConstants.SupportUrl is the only CloudTips URL source.

- [x] **Step 1: Implement process lifetime and tray**

Use a named mutex, NotifyIcon, BankLogMonitor and exact commands Настройки, Обновить, Сбросить положение, Справка, Поддержать разработку, Выход. Refresh refuses while HasPendingOperation; otherwise call the helper once and atomically merge only a complete report.

- [x] **Step 2: Implement Frontol tracking**

Every 250 ms inspect GetForegroundWindow, GetWindowThreadProcessId, IsIconic and GetWindowRect. Accept process names beginning Frontol or a title containing Frontol v. Show/reposition only while that window is foreground and not minimized.

- [x] **Step 3: Implement the transparent overlay**

Create a borderless topmost tool window with TransparencyKey, no activation, one or two ShadowTextControl rows, Russian currency and one compact ↻ control. Default offset is the upper free zone; dragging a name saves offset relative to Frontol. Unknown is —; stale is amber.

- [x] **Step 4: Implement dialogs**

Settings select UPOS and edit discovered department names; manual names persist. Auto-start uses only HKCU Run. Support shows the URL as selectable text with Открыть and Копировать; simply opening sends nothing. About exactly matches project metadata.

- [x] **Step 5: Build, smoke check and commit**

~~~powershell
dotnet build Krs.AcquiringMonitor.sln -c Release -p:Platform=x86
git add src/Krs.AcquiringMonitor
git commit -m "feat: добавить прозрачный оверлей поверх Frontol"
~~~

Expected: build succeeds; application and helper PE headers are x86.

### Task 7: Documentation, packaging and screenshot

**Files:**
- Create: README.md
- Create: LICENSE
- Create: CHANGELOG.md
- Create: docs/ACCEPTANCE-CHECKLIST.md
- Create: docs/SECURITY-NOTES.md
- Create: docs/screenshots/overlay.png
- Create: build/build-release.ps1
- Create: build/Krs.AcquiringMonitor.iss

**Interfaces:**
- Produces artifacts/KRS-AcquiringMonitor-0.1.0-win-x86.zip and an Inno Setup definition using only application-owned files.

- [x] **Step 1: Write README and safety docs**

Document purpose, requirements, UPOS selection, first start, moving/refreshing overlay, auto-start, updates, errors, privacy, limits, SmartScreen, issue reporting, official release source, developer/owner, MIT and voluntary support.

State that _get_statistics(auth_answer*) with TType=0 is the only terminal call; close_day and LoadParm.exe are never called. Include the 35-byte x86 layout, CP866 conversion, conservative parser and exact files copied to a cash desk.

- [x] **Step 2: Create a sanitized screenshot**

Render actual overlay styling with fictitious ИП Иванов / ООО Колокольчик and fictitious amounts over a neutral Frontol-like background. Include no client pixels, cashier name, terminal/merchant ID, RRN or card data. Link and label it as a demonstration.

- [x] **Step 3: Add release scripts**

build-release.ps1 cleans only artifacts/release, builds Release x86, copies the main exe, core DLL, helper exe, config files, README, LICENSE and checklist, then zips them. Inno installs per user, offers current-user auto-start and requests no administrator rights.

- [x] **Step 4: Package and inspect**

Run: powershell -NoProfile -ExecutionPolicy Bypass -File build/build-release.ps1

Expected: zip exists and contains no pilot_nt.dll, SC552, sbkernel log, pinpad.ini, p, SESS.D or SPLC.D.

- [x] **Step 5: Commit**

~~~powershell
git add README.md LICENSE CHANGELOG.md docs build
git commit -m "docs: подготовить установку и выпуск версии 0.1.0"
~~~

### Task 8: Final verification and review

**Files:**
- Modify only files implicated by verified failures.

**Interfaces:**
- Produces a verified source tree, portable archive and one explicit live-cash-desk acceptance boundary.

- [x] **Step 1: Run everything**

~~~powershell
dotnet build Krs.AcquiringMonitor.sln -c Release -p:Platform=x86
dotnet run --project tests/Krs.AcquiringMonitor.Tests/Krs.AcquiringMonitor.Tests.csproj -c Release -p:Platform=x86
powershell -NoProfile -ExecutionPolicy Bypass -File build/build-release.ps1
~~~

Expected: build succeeds, all tests pass, packaging succeeds.

- [x] **Step 2: Inspect hygiene and architecture**

Run git status, git diff, and targeted searches for close_day, LoadParm, pilot_nt, CloudTips, RRN and PAN. Inspect zip listing. Confirm no duplicated totals logic, no raw-report logging path, no bank file in Git and no unused abstraction.

- [x] **Step 3: Inspect binaries**

Confirm app/helper PE32 x86 and metadata ProductName, CompanyName and FileVersion agree with README and About.

- [x] **Step 4: Record the live boundary**

Do not call production pilot_nt.dll on this machine. On site compare current report with terminal output, prove shift remains open, verify report order against Department 1/2 and retain only a sanitized fixture if parsing needs adjustment.

- [x] **Step 5: Commit verified fixes only if needed**

~~~powershell
git add -A
git commit -m "fix: устранить замечания итоговой проверки"
~~~
