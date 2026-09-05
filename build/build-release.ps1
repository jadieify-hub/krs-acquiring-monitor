param(
    [string]$Configuration = "Release",
    [string]$Version = "0.2.2"
)

$ErrorActionPreference = "Stop"
if ($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw "Version must contain exactly three numeric components, for example 0.2.0."
}

$ProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$ArtifactsRoot = [IO.Path]::GetFullPath((Join-Path $ProjectRoot "artifacts"))
$ReleaseRoot = [IO.Path]::GetFullPath((Join-Path $ArtifactsRoot "release"))
$ArchivePath = Join-Path $ArtifactsRoot "KRS-AcquiringMonitor-$Version-win-x86.zip"
$InstallerPath = Join-Path $ArtifactsRoot "KRS-AcquiringMonitor-$Version-setup.exe"
$ManifestPath = Join-Path $ArtifactsRoot "update.json"
$VersionArguments = @(
    "-p:Version=$Version",
    "-p:FileVersion=$Version.0",
    "-p:AssemblyVersion=$Version.0"
)

if (-not $ReleaseRoot.StartsWith(
        $ArtifactsRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Invalid release directory path."
}

foreach ($Path in @($ArchivePath, $InstallerPath, $ManifestPath)) {
    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        Remove-Item -LiteralPath $Path -Force
    }
}

if (Test-Path -LiteralPath $ReleaseRoot) {
    Remove-Item -LiteralPath $ReleaseRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $ReleaseRoot -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $ReleaseRoot "docs") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $ReleaseRoot "docs\screenshots") -Force | Out-Null

& dotnet run --project (Join-Path $ProjectRoot "tests\Krs.AcquiringMonitor.Tests\Krs.AcquiringMonitor.Tests.csproj") `
    -c $Configuration `
    -p:Platform=x86 `
    @VersionArguments
if ($LASTEXITCODE -ne 0) {
    throw "Tests failed with exit code $LASTEXITCODE."
}

& dotnet build (Join-Path $ProjectRoot "Krs.AcquiringMonitor.sln") `
    -c $Configuration `
    -p:Platform=x86 `
    @VersionArguments `
    --nologo
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

$AppOutput = Join-Path $ProjectRoot "src\Krs.AcquiringMonitor\bin\x86\$Configuration\net48"
$HelperOutput = Join-Path $ProjectRoot "src\Krs.AcquiringMonitor.TerminalQuery\bin\x86\$Configuration\net48"

$Files = @(
    (Join-Path $AppOutput "Krs.AcquiringMonitor.exe"),
    (Join-Path $AppOutput "Krs.AcquiringMonitor.exe.config"),
    (Join-Path $AppOutput "Krs.AcquiringMonitor.Core.dll"),
    (Join-Path $HelperOutput "Krs.AcquiringMonitor.TerminalQuery.exe"),
    (Join-Path $HelperOutput "Krs.AcquiringMonitor.TerminalQuery.exe.config"),
    (Join-Path $AppOutput "support-qr.png"),
    (Join-Path $ProjectRoot "README.md"),
    (Join-Path $ProjectRoot "CHANGELOG.md"),
    (Join-Path $ProjectRoot "LICENSE")
)

foreach ($File in $Files) {
    if (-not (Test-Path -LiteralPath $File -PathType Leaf)) {
        throw "Required release file not found: $File"
    }
    Copy-Item -LiteralPath $File -Destination $ReleaseRoot -Force
}

Copy-Item -LiteralPath (Join-Path $ProjectRoot "docs\ACCEPTANCE-CHECKLIST.md") -Destination (Join-Path $ReleaseRoot "docs\ACCEPTANCE-CHECKLIST.md") -Force
Copy-Item -LiteralPath (Join-Path $ProjectRoot "docs\SECURITY-NOTES.md") -Destination (Join-Path $ReleaseRoot "docs\SECURITY-NOTES.md") -Force
Copy-Item -LiteralPath (Join-Path $ProjectRoot "docs\screenshots\overlay.png") -Destination (Join-Path $ReleaseRoot "docs\screenshots\overlay.png") -Force

Compress-Archive -Path (Join-Path $ReleaseRoot "*") -DestinationPath $ArchivePath
$ArchiveHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $ArchivePath).Hash

$IsccPath = $null
$IsccCommand = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
if ($null -ne $IsccCommand) {
    $IsccPath = $IsccCommand.Source
}

if ([string]::IsNullOrWhiteSpace($IsccPath)) {
    $CompilerCandidates = @(
        (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)) "Inno Setup 6\ISCC.exe"),
        (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) "Inno Setup 6\ISCC.exe"),
        (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) "Programs\Inno Setup 6\ISCC.exe")
    )
    $IsccPath = $CompilerCandidates |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($IsccPath)) {
    throw "Inno Setup 6 compiler (ISCC.exe) was not found."
}

& $IsccPath "/DMyAppVersion=$Version" (Join-Path $ProjectRoot "build\Krs.AcquiringMonitor.iss")
if ($LASTEXITCODE -ne 0) {
    throw "Installer build failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $InstallerPath -PathType Leaf)) {
    throw "Expected installer was not created: $InstallerPath"
}

$InstallerHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $InstallerPath).Hash.ToLowerInvariant()
$Manifest = [ordered]@{
    version = $Version
    sha256 = $InstallerHash
} | ConvertTo-Json
$Utf8NoBom = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText($ManifestPath, $Manifest + [Environment]::NewLine, $Utf8NoBom)

Write-Output "Portable: $ArchivePath"
Write-Output "SHA256:   $ArchiveHash"
Write-Output "Installer: $InstallerPath"
Write-Output "SHA256:   $InstallerHash"
Write-Output "Manifest:  $ManifestPath"
