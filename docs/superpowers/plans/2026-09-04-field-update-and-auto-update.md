# KRS Acquiring Monitor 0.2.0 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Выпустить версию 0.2.0 с исправленным закрытием смены и оверлеем, фоновым определением организаций, понятным ручным обновлением и безопасным автоматическим обновлением через GitHub Releases.

**Architecture:** Денежный инвариант исправляется в существующем `BankLogParser`, которому монитор передаёт ожидаемые отделы. Запрос статистики остаётся единственным общим путём для ручного и фонового обновления. Автообновление реализуется одним небольшим клиентом внутри основного приложения: фиксированный GitHub origin, проверяемый JSON, SHA-256 и существующий Inno Setup.

**Tech Stack:** C# / WinForms / .NET Framework 4.8 / x86, BCL `HttpClient`, `DataContractJsonSerializer`, `SHA256`, PowerShell, Inno Setup 6.

**Spec:** `docs/superpowers/specs/2026-09-04-field-update-and-auto-update-design.md` и `docs/superpowers/specs/2026-09-04-field-update-review-amendment.md`.

## Global Constraints

- Windows 10/11, .NET Framework 4.8, x86, обычные права пользователя.
- Следующая версия — `0.2.0`; версия релиза задаётся параметром `build-release.ps1`.
- Не добавлять службу, отдельный постоянный updater, сторонние пакеты или GitHub-токен.
- Не вызывать никакие команды UPOS кроме существующего `_get_statistics` в изолированном helper-процессе.
- Не запускать файлы из `SC552` и не включать банковские DLL, журналы, отчёты или настройки в Git и артефакты.
- CloudTips остаётся только `https://pay.cloudtips.ru/p/2f23e8c9`.

---

### Task 1: Correct the expected shift-close count

**Files:**
- Modify: `src/Krs.AcquiringMonitor.Core/Monitoring/BankLogParser.cs`
- Modify: `src/Krs.AcquiringMonitor/Monitoring/BankLogMonitor.cs`
- Modify: `src/Krs.AcquiringMonitor/MonitorApplicationContext.cs`
- Modify: `tests/Krs.AcquiringMonitor.Tests/BankLogParserTests.cs`
- Modify: `tests/Krs.AcquiringMonitor.Tests/Program.cs`

**Interfaces:**
- Produces: `BankLogParser(IEnumerable<int> expectedDepartments)`; existing parameterless construction remains valid.
- Consumes: configured positive department numbers from `AppSettings.Organizations`.

- [ ] **Step 1: Add the failing regression test**

Add `ConfiguredSecondDepartmentWithoutTransactionsStillNeedsSecondClose()`: construct `new BankLogParser(new[] { 1, 2 })`, post a purchase only to department 1, complete one successful close and assert that 10,000 kopeks remain stale; complete the second successful close and assert departments 1 and 2 are both zero and current.

- [ ] **Step 2: Run the test executable and confirm the new test fails**

Run:

```powershell
dotnet run --project tests/Krs.AcquiringMonitor.Tests/Krs.AcquiringMonitor.Tests.csproj -c Release -p:Platform=x86
```

Expected: the new close-count test fails because the first close currently compares with `_totals.Count == 1`.

- [ ] **Step 3: Implement expected departments once in the parser path**

Store validated expected department IDs separately from transaction totals. In `CompleteClose`, use `Math.Max(_totals.Count, _expectedDepartments.Count)` as the required successful close count. When all required closes succeed, set the union of observed and expected departments to zero. Preserve the current behavior when neither source identifies any department.

Add the expected departments to every parser construction in `BankLogMonitor`, including rebuild and resume. Pass `_settings.Organizations.Where(x => x != null && x.Department > 0).Select(x => x.Department)` from `RestartLogMonitor`.

- [ ] **Step 4: Run all tests and commit the isolated fix**

Expected: all tests pass, including the new zero-transaction department case.

```powershell
git add src/Krs.AcquiringMonitor.Core/Monitoring/BankLogParser.cs src/Krs.AcquiringMonitor/Monitoring/BankLogMonitor.cs src/Krs.AcquiringMonitor/MonitorApplicationContext.cs tests/Krs.AcquiringMonitor.Tests/BankLogParserTests.cs tests/Krs.AcquiringMonitor.Tests/Program.cs
git commit -m "fix: учитывать ожидаемые отделы при закрытии смены"
```

### Task 2: Stabilize and simplify the overlay

**Files:**
- Modify: `src/Krs.AcquiringMonitor/Frontol/FrontolWindowTracker.cs`
- Modify: `src/Krs.AcquiringMonitor/UI/OverlayForm.cs`
- Delete: `src/Krs.AcquiringMonitor/UI/ShadowTextControl.cs`
- Modify: `tests/Krs.AcquiringMonitor.Tests/OverlayPresentationTests.cs`
- Modify: `tests/Krs.AcquiringMonitor.Tests/Program.cs`

**Interfaces:**
- Produces: `FrontolWindowTracker.SelectAnchorWindow(IntPtr foreground, IntPtr rootOwner)` for deterministic regression coverage.
- Produces: `OverlayForm.SetDataStale(bool)` and retains `SetRefreshing(bool)`.

- [ ] **Step 1: Add the failing root-owner regression test**

Add `UsesRootOwnerForFrontolPlacement()`: assert that a non-zero root owner is selected over the foreground modal handle and that zero root owner falls back to the foreground handle.

- [ ] **Step 2: Run tests and confirm the anchor test fails**

Expected: compile failure because `SelectAnchorWindow` does not exist.

- [ ] **Step 3: Anchor placement and remove shadow rendering**

Call Win32 `GetAncestor(foreground, GA_ROOTOWNER)` after Frontol identity validation; read bounds and minimized state from the selected anchor. Replace `ShadowTextControl` instances with standard `Label` controls using white `ForeColor`, transparent background and no second paint pass. Delete `ShadowTextControl.cs`.

Keep names and amounts white. Track stale/deferred/running state only in the refresh label: white `↻` when current, amber `↻` when stale or deferred, and white disabled `…` while the terminal query runs.

- [ ] **Step 4: Run tests and commit the overlay fix**

```powershell
git add src/Krs.AcquiringMonitor/Frontol/FrontolWindowTracker.cs src/Krs.AcquiringMonitor/UI/OverlayForm.cs tests/Krs.AcquiringMonitor.Tests/OverlayPresentationTests.cs tests/Krs.AcquiringMonitor.Tests/Program.cs
git rm src/Krs.AcquiringMonitor/UI/ShadowTextControl.cs
git commit -m "fix: стабилизировать и упростить оверлей Frontol"
```

### Task 3: Unify manual refresh and eventual organization naming

**Files:**
- Create: `src/Krs.AcquiringMonitor/Terminal/AutomaticNameRefreshPolicy.cs`
- Modify: `src/Krs.AcquiringMonitor/MonitorApplicationContext.cs`
- Modify: `src/Krs.AcquiringMonitor/Terminal/TerminalStatisticsClient.cs`
- Create: `tests/Krs.AcquiringMonitor.Tests/AutomaticNameRefreshPolicyTests.cs`
- Modify: `tests/Krs.AcquiringMonitor.Tests/Program.cs`

**Interfaces:**
- Produces: `AutomaticNameRefreshPolicy.ShouldAttempt(AppSettings, BankLogSnapshot, DateTimeOffset)` and `RecordAttempt(DateTimeOffset)` with a ten-minute retry interval.
- Reuses: `StatisticsReportParser`, `ExpectedDepartmentResolver`, `StatisticsSnapshotMerger`, `RememberAutomaticNames` and `TerminalStatisticsClient.QueryAsync`.

- [ ] **Step 1: Add failing policy tests**

Cover one realistic sequence: a detected department with no bank name is eligible after the initial time, `RecordAttempt` blocks another attempt for ten minutes, eligibility returns at ten minutes, and saving a bank name stops attempts. Also assert that an `IsManual` display name is left intact while its missing bank identity still requires learning.

- [ ] **Step 2: Run tests and confirm the policy type is missing**

Run the complete test executable; expect a compile failure for `AutomaticNameRefreshPolicy`.

- [ ] **Step 3: Implement a single shared query flow**

Create a one-second WinForms timer in `MonitorApplicationContext`. Manual click immediately marks refresh deferred, then tries the shared async query. If the monitor is stale or pending, keep one pending manual request, show one explanatory balloon, and retry it as soon as safe. A real query shows `…`; repeated clicks are ignored.

For automatic names, initialize the policy with a short startup delay and call the same query silently when known departments lack bank names. Call `RecordAttempt` only when a real terminal request starts. Failed or incomplete reports wait ten minutes; successful complete reports call the existing name-learning and settings-save path. Manual display names remain unchanged.

Invoke stale temporary report cleanup from the `TerminalStatisticsClient` constructor as well as before each query, so abandoned reports are removed even if no new query starts.

- [ ] **Step 4: Run all tests and commit**

```powershell
git add src/Krs.AcquiringMonitor/Terminal/AutomaticNameRefreshPolicy.cs src/Krs.AcquiringMonitor/MonitorApplicationContext.cs src/Krs.AcquiringMonitor/Terminal/TerminalStatisticsClient.cs tests/Krs.AcquiringMonitor.Tests/AutomaticNameRefreshPolicyTests.cs tests/Krs.AcquiringMonitor.Tests/Program.cs
git commit -m "feat: обновлять терминал и названия без участия кассира"
```

### Task 4: Add the fail-open GitHub Release updater

**Files:**
- Create: `src/Krs.AcquiringMonitor/Updates/UpdateManifest.cs`
- Create: `src/Krs.AcquiringMonitor/Updates/StartupUpdater.cs`
- Modify: `src/Krs.AcquiringMonitor/AppConstants.cs`
- Modify: `src/Krs.AcquiringMonitor/MonitorApplicationContext.cs`
- Modify: `src/Krs.AcquiringMonitor/Diagnostics/SafeLogger.cs`
- Modify: `src/Krs.AcquiringMonitor/Krs.AcquiringMonitor.csproj`
- Create: `tests/Krs.AcquiringMonitor.Tests/UpdateManifestTests.cs`
- Modify: `tests/Krs.AcquiringMonitor.Tests/Program.cs`

**Interfaces:**
- Produces: `UpdateManifest.TrySelect(string json, Version currentVersion, out ValidatedUpdate update)`.
- Produces: `UpdateManifest.HashMatches(string path, string expectedSha256)`.
- Produces: `StartupUpdater.CheckAndInstallAsync(Version currentVersion, CancellationToken)` returning `true` only after the verified installer starts.

- [ ] **Step 1: Add failing update trust-boundary tests**

Use JSON `{"version":"0.2.0","sha256":"...64 hex..."}`. Assert that 0.2.0 is selected over 0.1.0, its filename and URL are exactly derived from the version, equal/older versions are rejected, malformed hashes and malformed JSON are rejected, and a temporary file containing ASCII `abc` matches SHA-256 `BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD` but not a changed hash.

- [ ] **Step 2: Run tests and confirm the update types are missing**

Expected: compile failure for `UpdateManifest` / `ValidatedUpdate`.

- [ ] **Step 3: Implement validation, download, verification and launch**

Parse with `DataContractJsonSerializer`; require a normalized three-component version strictly newer by major/minor/build and exactly 64 hexadecimal SHA characters. Derive:

```text
KRS-AcquiringMonitor-<version>-setup.exe
https://github.com/jadieify-hub/krs-acquiring-monitor/releases/download/v<version>/<filename>
```

Only run the startup check when `unins000.exe` exists beside the application. Fetch `releases/latest/download/update.json` asynchronously with a short manifest timeout, download to `%LOCALAPPDATA%\KRS\AcquiringMonitor\updates`, verify SHA-256, start Inno with `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /SP-`, and return true. Treat 404, cancellation and every recoverable network/file/process error as no update while logging only event code and safe detail.

Start the check once from a one-shot WinForms timer after the UI message loop begins. If it returns true, exit the application context. Derive `AppConstants.Version` and `ApplicationVersion` from the executing assembly.

- [ ] **Step 4: Run tests and commit the updater**

```powershell
git add src/Krs.AcquiringMonitor/Updates/UpdateManifest.cs src/Krs.AcquiringMonitor/Updates/StartupUpdater.cs src/Krs.AcquiringMonitor/AppConstants.cs src/Krs.AcquiringMonitor/MonitorApplicationContext.cs src/Krs.AcquiringMonitor/Diagnostics/SafeLogger.cs src/Krs.AcquiringMonitor/Krs.AcquiringMonitor.csproj tests/Krs.AcquiringMonitor.Tests/UpdateManifestTests.cs tests/Krs.AcquiringMonitor.Tests/Program.cs
git commit -m "feat: добавить безопасное обновление через GitHub Releases"
```

### Task 5: Produce installer, manifest and user documentation

**Files:**
- Modify: `Directory.Build.props`
- Modify: `build/Krs.AcquiringMonitor.iss`
- Modify: `build/build-release.ps1`
- Modify: `README.md`
- Modify: `CHANGELOG.md`
- Modify: `docs/ACCEPTANCE-CHECKLIST.md`
- Modify if needed after visual check: `docs/screenshots/overlay.png`

**Interfaces:**
- Consumes: release parameter `-Version 0.2.0`.
- Produces: ZIP, `KRS-AcquiringMonitor-0.2.0-setup.exe`, and `update.json` containing `version` plus lowercase installer `sha256`.

- [ ] **Step 1: Parameterize all release version consumers**

Set development defaults to 0.2.0. In the Inno script guard `MyAppVersion` with `#ifndef`, set exact output basename `KRS-AcquiringMonitor-{#MyAppVersion}-setup`, and remove `skipifsilent` from `[Run]`.

Validate `Version` as three numeric components in PowerShell. Pass `-p:Version=$Version`, `-p:FileVersion=$Version.0`, and `-p:AssemblyVersion=$Version.0` to both test and build commands. Locate ISCC in `PATH` or the two standard Inno Setup 6 program directories, compile with `/DMyAppVersion=$Version`, calculate SHA-256 and write the two-field `update.json`.

- [ ] **Step 2: Update README, changelog and field checklist**

Document the one-time manual 0.1.0→0.2.0 installation, future silent updates, private-repository 404 behavior, installer preference, automatic names, refresh status and white overlay. Retain author/publisher/license/issues/limitations and the exact project CloudTips URL. Add release notes for 0.2.0.

- [ ] **Step 3: Run the release script and inspect artifacts**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build/build-release.ps1 -Version 0.2.0
```

Expected: tests pass, build succeeds, and all three versioned artifacts are produced. Recompute the installer hash and compare with `update.json`. Inspect ZIP and installer input tree for `SC552`, `pilot_nt.dll`, `sbkernel`, reports and settings; none may be present.

- [ ] **Step 4: Smoke-test the installer without touching UPOS**

Install 0.2.0 for the current test user over an isolated 0.1.0 application installation, using synthetic settings only. Verify that the settings directory is unchanged, the installed executable reports 0.2.0, autostart remains configured, and silent installation launches the new app. Do not point the smoke test at the production `SC552` folder and do not invoke terminal statistics.

- [ ] **Step 5: Commit release preparation**

```powershell
git add Directory.Build.props build/Krs.AcquiringMonitor.iss build/build-release.ps1 README.md CHANGELOG.md docs/ACCEPTANCE-CHECKLIST.md docs/screenshots/overlay.png
git commit -m "build: подготовить установщик и обновление 0.2.0"
```

### Task 6: Final verification and independent review

**Files:**
- Review all changes since commit `0017a65`.

- [ ] **Step 1: Run a clean full verification**

Run the test executable, Release/x86 solution build and release script again. Record exact pass count, compiler warnings/errors and produced SHA-256.

- [ ] **Step 2: Audit the diff and artifacts**

Check for duplicated query logic, dead code, accidental settings resets, unsafe arbitrary URLs, unbounded retries, blocking UI work, mismatched version strings, customer files and incorrect CloudTips URLs.

- [ ] **Step 3: Request an independent code review**

Provide the reviewer with the two spec files, base `0017a65`, current HEAD and test evidence. Fix Critical and Important findings, then rerun the relevant regression test and the complete verification suite.

- [ ] **Step 4: Push only after verification**

Push `master` to the existing private origin. Create the private `v0.2.0` GitHub Release only if the installer smoke test passed; otherwise leave locally verified artifacts and report the exact remaining manual check.
