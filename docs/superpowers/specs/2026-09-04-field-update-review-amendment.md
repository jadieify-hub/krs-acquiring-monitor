# Review amendment: field update and automatic updates

This amendment is part of `2026-09-04-field-update-and-auto-update-design.md` and supersedes its conflicting wording.

1. New installers use the exact name `KRS-AcquiringMonitor-<version>-setup.exe`. The historical 0.1.0 filename is not supported by the automatic-update contract because 0.1.0 contains no updater. Each installed 0.1.0 copy requires one manual upgrade to 0.2.0; automatic updates begin with 0.2.0.
2. The Inno Setup run entry does not use `skipifsilent`, so the application starts after both interactive installation and a silent update.
3. The installer download URL is exactly `https://github.com/jadieify-hub/krs-acquiring-monitor/releases/download/v<version>/KRS-AcquiringMonitor-<version>-setup.exe`.
4. `update.json` contains only `version` and `sha256`. `filename` is removed because it is fully derived from the validated version and provides no independent security barrier.
5. The release script parameter is the authoritative version for a release. It passes the version to MSBuild and ISCC. Values in `Directory.Build.props` and the Inno script remain development fallbacks. Runtime UI and logging read the executing assembly version rather than a separate `AppConstants.Version` literal.
6. `ShadowTextControl.cs` is removed. The replacement draws plain white overlay text once and retains only the minimum hit-testing required for dragging and refresh.
