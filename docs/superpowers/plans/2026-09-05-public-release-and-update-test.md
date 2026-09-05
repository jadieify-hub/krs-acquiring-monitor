# Public Release and Update Test Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Выпустить публичную версию 0.2.2 с более лёгким шрифтом названий, проверить обновление 0.2.1 → 0.2.2 через GitHub Releases и убедиться, что репозиторий не раскрывает данные клиента.

**Architecture:** Визуальная правка остаётся локальной в `OverlayForm`. Текущий механизм обновления не расширяется: проверяется его реальный производственный путь через публичный GitHub Release, точное имя установщика и SHA-256. Перед публикацией сканируются все объекты Git и пользовательские артефакты.

**Tech Stack:** C#/.NET Framework 4.8 WinForms x86, PowerShell, Inno Setup 6, GitHub CLI.

**Spec:** `docs/superpowers/specs/2026-09-04-field-update-and-auto-update-design.md`

## Global Constraints

- Не загружать и не вызывать банковскую DLL из боевой папки `SC552` во время локальной проверки обновления.
- Публичный релиз содержит только установщик, portable ZIP и `update.json` версии 0.2.2.
- `update.json` содержит ровно `version` и `sha256`; хеш обязан совпадать с установщиком.
- Общая ширина оверлея остаётся 470 px; названия используют обычный `Segoe UI`, суммы остаются `Segoe UI Semibold`.
- Личные сведения разработчика, явно требуемые проектом (`Руслан Керусов`, `KRS`), не считаются утечкой; данные клиента, локальные пути, платёжные реквизиты и секреты запрещены.

---

### Task 1: Typography and version 0.2.2

**Files:**
- Modify: `src/Krs.AcquiringMonitor/UI/OverlayForm.cs`
- Modify: `tests/Krs.AcquiringMonitor.Tests/OverlayPresentationTests.cs`
- Modify: `tests/Krs.AcquiringMonitor.Tests/Program.cs`
- Modify: `Directory.Build.props`
- Modify: `build/build-release.ps1`
- Modify: `build/Krs.AcquiringMonitor.iss`
- Modify: `CHANGELOG.md`

**Interfaces:**
- Consumes: существующая форма `Krs.AcquiringMonitor.UI.OverlayForm`.
- Produces: имена организаций шрифтом `Segoe UI Regular`, увеличенное поле имени и версия 0.2.2.

- [ ] **Step 1: Write the failing test**

Добавить отражательный тест, который создаёт `OverlayForm` и проверяет `Font.Name == "Segoe UI"` и отсутствие жирного начертания у первого имени.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/Krs.AcquiringMonitor.Tests/Krs.AcquiringMonitor.Tests.csproj -c Release -p:Platform=x86`

Expected: новый тест падает на текущем `Segoe UI Semibold`.

- [ ] **Step 3: Write minimal implementation**

В `OverlayForm` заменить только шрифт имён на `new Font("Segoe UI", 15.5f, FontStyle.Regular)`, расширить имя до 268 px, сдвинуть сумму вправо и сократить её ширину без изменения формы или кнопки.

- [ ] **Step 4: Bump version and changelog**

Заменить 0.2.1 на 0.2.2 в трёх источниках версии и добавить запись в `CHANGELOG.md`.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet run --project tests/Krs.AcquiringMonitor.Tests/Krs.AcquiringMonitor.Tests.csproj -c Release -p:Platform=x86`

Expected: все тесты проходят.

### Task 2: Privacy audit and public documentation

**Files:**
- Modify: `README.md`
- Modify: `docs/ACCEPTANCE-CHECKLIST.md`
- Inspect: every tracked Git object and `docs/screenshots/overlay.png`

**Interfaces:**
- Consumes: текущая история Git и документы проекта.
- Produces: публично безопасная история, актуальный README и чек-лист 0.2.2.

- [ ] **Step 1: Scan all Git objects**

Проверить имена объектов, тексты всех коммитов, QR-код и метаданные изображений на `SC552`, журналы, локальные пути, секреты, email, реальные ФИО клиента, PAN/RRN/Terminal ID/Merchant ID и старые ссылки поддержки.

- [ ] **Step 2: Inspect screenshot visually**

Убедиться, что изображение не содержит данных клиента или локальной системы; при наличии заменить обезличенным снимком.

- [ ] **Step 3: Update public-facing docs**

README должен явно содержать назначение, возможности, требования, установку, автозапуск, обновление, ограничения, официальный источник сборок, сообщение об ошибках, лицензию, разработчика/владельца, скриншот и добровольную поддержку.

- [ ] **Step 4: Re-run privacy scan**

Expected: нет запрещённых файлов или секретов; остаются только заявленные сведения разработчика и документированные безопасные примеры.

### Task 3: Build and review release artifacts

**Files:**
- Generated: `artifacts/KRS-AcquiringMonitor-0.2.2-setup.exe`
- Generated: `artifacts/KRS-AcquiringMonitor-0.2.2-win-x86.zip`
- Generated: `artifacts/update.json`

**Interfaces:**
- Consumes: исходники и документацию версии 0.2.2.
- Produces: проверенные пользовательские файлы для GitHub Release.

- [ ] **Step 1: Build release**

Run: `powershell -ExecutionPolicy Bypass -File build/build-release.ps1 -Version 0.2.2`

- [ ] **Step 2: Verify artifact contract**

Проверить версию EXE, SHA-256 установщика против `update.json`, точный состав ZIP и отсутствие запрещённых файлов.

- [ ] **Step 3: Commit and review**

Создать коммит, затем выполнить независимое ревью диффа от `0d039e6` до нового HEAD; исправить Critical/Important замечания и повторить проверки.

### Task 4: Publish and run a real update

**Files:**
- Remote: `jadieify-hub/krs-acquiring-monitor`, branch `master`, release `v0.2.2`.

**Interfaces:**
- Consumes: проверенный коммит и три релизных файла.
- Produces: публичный репозиторий и доказанный сценарий обновления 0.2.1 → 0.2.2.

- [ ] **Step 1: Prepare and push clean public history while private**

Сохранить полный прежний репозиторий в локальный Git bundle, затем создать новый корневой `master` из проверенного текущего снимка и отправить его в существующий приватный origin через `--force-with-lease`. Так прежняя чужая ссылка и старый QR не будут достижимы из публичной истории.

- [ ] **Step 2: Make repository public and set metadata**

Установить публичную видимость и согласованное краткое описание через GitHub CLI.

- [ ] **Step 3: Publish v0.2.2**

Создать GitHub Release `v0.2.2` с release notes, установщиком, ZIP и `update.json`.

- [ ] **Step 4: Test 0.2.1 → 0.2.2**

Остановить монитор, сохранить пользовательские `%LOCALAPPDATA%\KRS\AcquiringMonitor` и запись автозагрузки. До установки создать синтетический каталог UPOS только с пустым `sbkernelYYMM.log`, без `pilot_nt.dll`, и сохранить настройки с заполненным `BankName`, чтобы исключить автоматический вызов helper. Затем установить 0.2.1, дождаться реального обновления и проверить установленную версию 0.2.2, событие `UpdateInstallerStarted`, повторный запуск приложения и сохранность тестовых настроек.

- [ ] **Step 5: Restore local state**

Остановить тестовый процесс, вернуть исходные настройки и автозагрузку, не запускать запрос терминала.

- [ ] **Step 6: Final public verification**

Проверить публичную видимость, страницу релиза, доступность `releases/latest/download/update.json`, его SHA-256 и свежий полный прогон тестов.
