[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [ValidateSet('win-x64', 'win-arm64')]
    [string] $RuntimeIdentifier = 'win-x64',
    [string] $OutputDirectory = '',
    [string] $PackageVersion = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$architecture = switch ($RuntimeIdentifier) {
    'win-x64' { 'x64' }
    'win-arm64' { 'arm64' }
}

if ([string]::IsNullOrWhiteSpace($PackageVersion)) {
    $props = Get-Content (Join-Path $repository 'Directory.Build.props') -Raw
    $versionMatch = [regex]::Match($props, '<Version>(?<version>[^<]+)</Version>')
    if (-not $versionMatch.Success) {
        throw 'Directory.Build.props does not contain a Version property.'
    }

    $PackageVersion = "$($versionMatch.Groups['version'].Value).0"
}

if ($PackageVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "MSIX package version must be a four-part number (received '$PackageVersion')."
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repository "artifacts/msix/$RuntimeIdentifier"
}

$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$repositoryFullPath = [IO.Path]::GetFullPath($repository).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$outputDirectoryFullPath = $OutputDirectory.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
if ([string]::Equals($repositoryFullPath, $outputDirectoryFullPath, [StringComparison]::OrdinalIgnoreCase) -or
    $repositoryFullPath.StartsWith("$outputDirectoryFullPath$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory '$OutputDirectory' cannot be the repository root or one of its ancestors."
}

$payloadDirectory = Join-Path $OutputDirectory 'payload'
$packagePath = Join-Path $OutputDirectory "rclicker-$architecture.msix"
$manifestPath = Join-Path $payloadDirectory 'AppxManifest.xml'
$sourceManifestPath = Join-Path $repository 'packaging/msix/AppxManifest.xml'
$assetsPath = Join-Path $repository 'packaging/msix/Assets'

New-Item $OutputDirectory -ItemType Directory -Force | Out-Null
if (Test-Path $payloadDirectory) {
    Remove-Item $payloadDirectory -Recurse -Force
}
if (Test-Path $packagePath) {
    Remove-Item $packagePath -Force
}
New-Item $payloadDirectory -ItemType Directory -Force | Out-Null
New-Item (Join-Path $payloadDirectory 'Assets') -ItemType Directory -Force | Out-Null

Push-Location $repository
try {
    & dotnet publish src/RClicker/RClicker.csproj -c $Configuration -r $RuntimeIdentifier -o $payloadDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

$publishedFiles = @(Get-ChildItem $payloadDirectory -File)
if ($publishedFiles.Count -ne 1 -or $publishedFiles[0].Name -ne 'rclicker.exe') {
    throw "The MSIX payload must contain exactly one rclicker.exe (found: $($publishedFiles.Name -join ', '))."
}

$manifest = Get-Content $sourceManifestPath -Raw
$manifest = $manifest.Replace('__VERSION__', $PackageVersion).Replace('__ARCHITECTURE__', $architecture)
Set-Content -Path $manifestPath -Value $manifest -Encoding utf8NoBOM
Copy-Item (Join-Path $assetsPath '*') (Join-Path $payloadDirectory 'Assets') -Recurse -Force

$makeAppx = Get-Command makeappx.exe -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $makeAppx) {
    $kitsPath = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $makeAppx = Get-ChildItem $kitsPath -Filter makeappx.exe -Recurse -File -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1
}
if ($null -eq $makeAppx) {
    throw 'makeappx.exe was not found. Install the Windows 10 SDK before building an MSIX package.'
}

& $makeAppx.FullName pack /d $payloadDirectory /p $packagePath /o
if ($LASTEXITCODE -ne 0) {
    throw "makeappx.exe failed with exit code $LASTEXITCODE."
}

Write-Host "Created unsigned MSIX: $packagePath"
