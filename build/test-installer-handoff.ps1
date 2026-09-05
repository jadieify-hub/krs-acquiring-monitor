param([Parameter(Mandatory = $true)][string]$IsccPath)

$ErrorActionPreference = "Stop"
$ProbeRoot = Join-Path $PSScriptRoot ("..\artifacts\handoff-test-" + [Guid]::NewGuid().ToString("N"))
$ProbeRoot = [IO.Path]::GetFullPath($ProbeRoot)
New-Item -ItemType Directory -Path $ProbeRoot | Out-Null

# Compile the production handoff code into a no-files, no-registry installer.
# It cannot install or launch the monitor, change autostart, or touch UPOS.
$InstallerSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot "Krs.AcquiringMonitor.iss") -Raw
$Code = ($InstallerSource -split '(?m)^\[Code\]\r?$', 2)[1]
$ProbeSource = @"
[Setup]
AppName=KRS Handoff Test
AppVersion=1.0
DefaultDirName={tmp}\KRS-Handoff-Test
CreateAppDir=no
Uninstallable=no
PrivilegesRequired=lowest
CloseApplications=no
RestartApplications=no
OutputDir=$ProbeRoot
OutputBaseFilename=handoff-probe
[Code]
$Code
"@
$ScriptPath = Join-Path $ProbeRoot "probe.iss"
[IO.File]::WriteAllText($ScriptPath, $ProbeSource, [Text.UTF8Encoding]::new($true))
& $IsccPath /Q $ScriptPath
if ($LASTEXITCODE -ne 0) { throw "Probe compilation failed." }

foreach ($Case in @("exits", "stays-running")) {
    $Seconds = if ($Case -eq "exits") { 8 } else { 60 }
    $Parent = Start-Process -FilePath "powershell.exe" -ArgumentList @(
        "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds $Seconds"
    ) -PassThru -WindowStyle Hidden
    $Installer = $null
    try {
        $LogPath = Join-Path $ProbeRoot "$Case.log"
        $Timer = [Diagnostics.Stopwatch]::StartNew()
        $Installer = Start-Process -FilePath (Join-Path $ProbeRoot "handoff-probe.exe") -ArgumentList @(
            "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-",
            ("/WAITPID=" + $Parent.Id), ('/LOG="' + $LogPath + '"')
        ) -PassThru -WindowStyle Hidden
        if (-not $Installer.WaitForExit(45000)) { throw "Installer hung beyond 45 seconds." }
        $Installer.Refresh()
        $Log = Get-Content -LiteralPath $LogPath -Raw
        if ($Case -eq "exits") {
            if ($Installer.ExitCode -ne 0 -or -not $Parent.HasExited -or $Timer.Elapsed.TotalSeconds -lt 7) {
                throw "Installer did not wait for its parent before proceeding."
            }
        }
        elseif ($Installer.ExitCode -eq 0 -or $Parent.HasExited -or
            -not $Log.Contains("Installation cancelled.")) {
            throw "Installer must cancel without killing the still-running parent."
        }
        Write-Output ("PASS {0}: exit={1}, elapsed={2:N1}s" -f $Case, $Installer.ExitCode, $Timer.Elapsed.TotalSeconds)
    }
    finally {
        # Only the exact harmless processes created above are cleaned up.
        if ($null -ne $Installer) {
            if (-not $Installer.HasExited) { $Installer.Kill() }
            $Installer.Dispose()
        }
        if (-not $Parent.HasExited) { $Parent.Kill() }
        $Parent.Dispose()
    }
}
Write-Output "Handoff logs: $ProbeRoot"
