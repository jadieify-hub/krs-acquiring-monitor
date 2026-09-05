param([Parameter(Mandatory = $true)][string]$IsccPath)

$ErrorActionPreference = "Stop"
$ProbeRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot (
    "..\artifacts\manual-update-test-" + [Guid]::NewGuid().ToString("N"))))
$TargetDirectory = Join-Path $ProbeRoot "installed"
New-Item -ItemType Directory -Path $TargetDirectory -Force | Out-Null
$Utf8 = [Text.UTF8Encoding]::new($true)

# Reproduce older releases: a hidden window and an ApplicationContext without MainForm.
# This client has no monitor, settings, autostart, banking code, or network access.
$ClientSource = @'
using System;
using System.Windows.Forms;
using System.Reflection;
[assembly: AssemblyVersion("0.0.VERSION.0")]
internal static class Client {
    [STAThread] private static void Main() {
        using (var form = new Form { ShowInTaskbar = false })
        using (var context = new ApplicationContext())
        using (var timeout = new Timer { Interval = 90000 }) {
            IntPtr handle = form.Handle;
            timeout.Tick += delegate { context.ExitThread(); };
            timeout.Start();
            Application.Run(context);
        }
    }
}
'@
$Compiler = Join-Path ([Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) "csc.exe"
if (-not (Test-Path -LiteralPath $Compiler)) {
    $Compiler = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
$SourcePath = Join-Path $ProbeRoot "client.cs"
foreach ($Version in @(1, 2)) {
    [IO.File]::WriteAllText($SourcePath, $ClientSource.Replace("VERSION", "$Version"), $Utf8)
    & $Compiler /nologo /target:winexe /platform:x86 /reference:System.Windows.Forms.dll (
        "/out:" + (Join-Path $ProbeRoot "client-$Version.exe")) $SourcePath
    if ($LASTEXITCODE -ne 0) { throw "Client compilation failed." }
}
$TargetExe = Join-Path $TargetDirectory "Krs.AcquiringMonitor.exe"
Copy-Item -LiteralPath (Join-Path $ProbeRoot "client-1.exe") -Destination $TargetExe

# Use the real installer's process-closing configuration, but no real install/registry entries.
$ProductionScript = Get-Content -LiteralPath (Join-Path $PSScriptRoot "Krs.AcquiringMonitor.iss") -Raw -Encoding UTF8
$ClosingSettings = ($ProductionScript -split '\r?\n' |
    Where-Object { $_ -match '^(CloseApplications|CloseApplicationsFilter|RestartApplications)=' }) -join [Environment]::NewLine
$SetupSource = @"
[Setup]
AppName=KRS Manual Update Test
AppVersion=0.0.2
DefaultDirName=$TargetDirectory
Uninstallable=no
PrivilegesRequired=lowest
OutputDir=$ProbeRoot
OutputBaseFilename=manual-update-probe
$ClosingSettings
[Files]
Source: "$ProbeRoot\client-2.exe"; DestDir: "{app}"; DestName: "Krs.AcquiringMonitor.exe"; Flags: ignoreversion
"@
$SetupSourcePath = Join-Path $ProbeRoot "setup.iss"
[IO.File]::WriteAllText($SetupSourcePath, $SetupSource, $Utf8)
& $IsccPath /Q $SetupSourcePath
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed." }

$OldProcess = Start-Process -FilePath $TargetExe -PassThru -WindowStyle Hidden
$UnrelatedProcess = Start-Process -FilePath (Join-Path $ProbeRoot "client-1.exe") -PassThru -WindowStyle Hidden
$Installer = $null
try {
    # Give both clients time to create their hidden windows.
    Start-Sleep -Milliseconds 750
    $Installer = Start-Process -FilePath (Join-Path $ProbeRoot "manual-update-probe.exe") -ArgumentList @(
        "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-",
        ('/LOG="' + (Join-Path $ProbeRoot "installer.log") + '"')
    ) -PassThru -WindowStyle Hidden
    if (-not $Installer.WaitForExit(60000)) { throw "Manual installer hung." }
    $Installer.Refresh()
    if ($UnrelatedProcess.HasExited) { throw "Installer stopped an unrelated process." }
    if ($Installer.ExitCode -ne 0 -or -not $OldProcess.HasExited) {
        throw "Old hidden process was not closed; installer exit=$($Installer.ExitCode). Logs: $ProbeRoot"
    }
    if ((Get-FileHash -LiteralPath $TargetExe).Hash -ne
        (Get-FileHash -LiteralPath (Join-Path $ProbeRoot "client-2.exe")).Hash) {
        throw "Installer did not replace the running old executable."
    }
    Write-Output "PASS: old hidden process exited, file replaced, unrelated process remains alive."
}
finally {
    foreach ($Process in @($Installer, $OldProcess, $UnrelatedProcess)) {
        if ($null -ne $Process) {
            if (-not $Process.HasExited) { $Process.Kill() }
            $Process.Dispose()
        }
    }
}
Write-Output "Manual update logs: $ProbeRoot"
