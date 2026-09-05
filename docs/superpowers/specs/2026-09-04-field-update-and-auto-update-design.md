# Field update and automatic updates design

## Goal

Prepare KRS Acquiring Monitor 0.2.0 for deployment to several cash registers without manual replacement on each machine. The release must also incorporate the field-test feedback: stable overlay position over Frontol dialogs, plain white text, visible manual-refresh state, and eventual automatic organization naming.

This document extends and, where it differs, supersedes `2026-09-04-krs-acquiring-monitor-design.md`.

## Scope and constraints

- Target remains Windows 10/11, .NET Framework 4.8, x86 and ordinary user rights.
- Supported bank source remains Sberbank UPOS with one or two organizations mapped to Department 1 and 2.
- No Windows service, resident secondary updater, third-party update framework or GitHub token is added.
- The application never performs payment, refund or shift-closing commands. Terminal statistics continue to use only the existing isolated `_get_statistics` helper.
- `SC552`, bank DLLs, customer logs, reports and settings are never included in Git or release artifacts.
- The next release version is 0.2.0.

## Overlay behavior

Organization names and amounts are rendered in white in one pass, without a shadow, outline or colored fringe. Stale state no longer changes row colors; only the small refresh control becomes amber. There is still no background, frame, heading, department number, total or third line.

Frontol identity is determined from the foreground window as before, but placement uses its root owner. A modal Frontol window such as “Closing document” therefore keeps the overlay anchored to the unchanged main Frontol bounds. The saved relative offset and drag behavior remain unchanged.

Clicking refresh gives immediate feedback. During a real terminal request the control shows an ellipsis and is disabled. If the request cannot safely start because the journal is stale or has an unfinished operation, the control becomes amber, a short tray notification explains that the update is deferred, and the application retries after the monitor becomes safe. Existing totals are never cleared merely to indicate refresh state.

## Automatic organization names

The existing statistics parser, department resolver, merger and name shortener are reused; there is no OCR or second recognition path.

When at least one detected organization has no stored bank name, the application schedules the same safe statistics request used by manual refresh. The first attempt starts only after the UPOS journal has been read and no unfinished bank operation is visible. If the terminal is busy, the journal is not yet safe, or the request fails, another attempt may occur after ten minutes. This repeats until all organizations returned by an unambiguous complete report have names.

One-organization reports fill one known department; two-organization reports fill both expected departments. Ambiguous or partial mappings are rejected rather than guessed. A display name explicitly entered by the user is never overwritten; its stored bank name may be refreshed solely to preserve future matching. Successful names are shortened to forms such as `ИП Иванов` and `ООО Колокольчик`, saved immediately, and stop further background attempts.

Background failures are written only as safe technical diagnostics and do not display repetitive balloons. A later manual refresh can complete the same naming flow.

## Shift-close correction

The number of successful `close_day` results required before reset is based on expected configured or previously detected departments, not only on departments that happened to have transactions in the current shift. With two expected organizations, both successful closes are required even if one organization has a zero total. The first close marks data stale; only the second successful close resets both totals. A failed close never counts.

## Automatic update flow

After the normal UI starts, the installed application asynchronously requests:

`https://github.com/jadieify-hub/krs-acquiring-monitor/releases/latest/download/update.json`

The check does not delay the overlay. While the repository is private, the unauthenticated 404 response is treated like no available update. No personal access token is stored on a cash register. Once the repository is public, the same URL works anonymously.

The JSON manifest contains exactly the release version, installer filename and installer SHA-256. The application accepts only a version strictly newer than its assembly version. The filename must equal `KRS-AcquiringMonitor-<version>-setup.exe`; path separators and arbitrary URLs are rejected. The download URL is constructed by the application for the fixed HTTPS GitHub repository rather than trusted from the manifest.

The installer is downloaded under `%LOCALAPPDATA%\KRS\AcquiringMonitor\updates`. Its SHA-256 is compared before execution. A mismatch or malformed manifest aborts the update and removes the unusable file. Network, HTTP, parse, disk, hash and process-start failures leave the current version running and produce only a safe diagnostic entry.

After verification, the application starts Inno Setup with `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS` and exits. The fixed AppId and per-user install directory cause an in-place upgrade. Settings and runtime state remain in `%LOCALAPPDATA%\KRS\AcquiringMonitor` and are not removed or overwritten. The installer starts the new application after copying files, including in silent update mode.

SHA-256 plus fixed HTTPS origin protects against corruption and an arbitrary URL in the manifest. Until the executable is Authenticode-signed, release trust still depends on the GitHub publisher account; README must state this limitation.

## Build and release

One version supplied to `build/build-release.ps1` controls assembly metadata, archive names, Inno Setup and `update.json`. The script builds and tests x86 Release, creates the portable ZIP, invokes an installed Inno Setup 6 compiler, computes the installer SHA-256, and writes the manifest beside the artifacts. It fails instead of publishing mismatched or incomplete artifacts.

GitHub Release `v0.2.0` must contain at least:

- `KRS-AcquiringMonitor-0.2.0-win-x86.zip`;
- `KRS-AcquiringMonitor-0.2.0-setup.exe`;
- `update.json`;
- concise release notes covering the overlay, refresh, organization-name and update changes.

The repository remains private during field testing. Therefore the automatic check is intentionally dormant on unauthenticated cash registers until the repository is made public. The installer can still be downloaded manually by an authenticated maintainer.

## Documentation

README is updated so that installation recommends the installer, startup is enabled by default but remains configurable, organization names can appear later without cashier action, and installed versions update automatically from official GitHub Releases. It must distinguish installed and portable behavior, document the private-repository limitation, retain the SmartScreen/AuthentiCode warning, name Руслан Керусов as developer and KRS as publisher/owner, link issues, retain the MIT license, and use only the project CloudTips URL `https://pay.cloudtips.ru/p/2f23e8c9`.

The screenshot and UI description must show plain white overlay text and no third line.

## Verification

Minimal automated coverage adds or adjusts tests for:

- choosing the root owner as the Frontol placement anchor;
- keeping row text white while stale state changes only the refresh control;
- manual refresh entering running or deferred state immediately;
- background naming retry eligibility, ten-minute throttling and stopping after success;
- preserving manual display names while saving automatic bank names;
- requiring two successful closes when two departments are expected and one has no transactions;
- accepting only a strictly newer semantic version and the exact installer filename;
- rejecting malformed manifests and SHA-256 mismatches.

Release verification consists of a fresh x86 Release test run, solution build, artifact inspection proving that no `SC552`, log, report or bank DLL is present, SHA-256 recomputation, and an install-over-0.1.0 smoke test that preserves settings, replaces the binary with 0.2.0 and starts it again.

## Acceptance criteria

- A Frontol modal dialog no longer moves the overlay.
- Names and amounts are plain white without outline; stale data is indicated only by the refresh control.
- Refresh always provides immediate visible feedback and a blocked request is retried safely rather than silently ignored.
- Missing organization names are eventually populated without cashier action and manual labels remain untouched.
- With two expected organizations, the first of two shift closes cannot reset totals by itself.
- Offline or private-repository startup works normally without credentials or blocking dialogs.
- A valid newer public GitHub Release updates an installed copy silently and preserves settings.
- Release artifacts and repository history contain no customer or bank runtime files.
