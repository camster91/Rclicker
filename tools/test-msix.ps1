[CmdletBinding()]
param(
    [string] $PackagePath = '',
    [switch] $KeepArtifacts
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$packageName = 'CameronAshley.Rclicker'
$publisher = 'CN=Cameron Ashley, O=Cameron Ashley, L=Scarborough, S=on, C=CA'
$props = Get-Content (Join-Path $repository 'Directory.Build.props') -Raw
$versionMatch = [regex]::Match($props, '<Version>(?<version>[^<]+)</Version>')
if (-not $versionMatch.Success -or $versionMatch.Groups['version'].Value -notmatch '^\d+\.\d+\.\d+$') {
    throw 'Directory.Build.props must contain a three-part Version for the MSIX lifecycle test.'
}
$version = "$($versionMatch.Groups['version'].Value).0"
$versionParts = $version.Split('.') | ForEach-Object { [int] $_ }
$updateVersion = "$($versionParts[0]).$($versionParts[1]).$($versionParts[2]).$($versionParts[3] + 1)"
$workDirectory = Join-Path ([IO.Path]::GetTempPath()) "rclicker-msix-$([guid]::NewGuid().ToString('N'))"
$firstOutput = Join-Path $workDirectory 'first'
$updateOutput = Join-Path $workDirectory 'update'
$certificate = $null
$certificateFile = Join-Path $workDirectory 'rclicker-test.cer'
$installedPackage = $null
$script:runningProcess = $null

function Find-Tool([string] $name) {
    $command = Get-Command $name -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $command) {
        return $command.Source
    }

    $kitsPath = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $match = Get-ChildItem $kitsPath -Filter $name -Recurse -File -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if ($null -eq $match) {
        throw "$name was not found. Install the Windows 10 SDK before running the MSIX lifecycle test."
    }

    return $match.FullName
}

function Assert-Equal([object] $expected, [object] $actual, [string] $message) {
    if ($expected -ne $actual) {
        throw "$message (expected '$expected', received '$actual')."
    }
}

function Assert-True([bool] $condition, [string] $message) {
    if (-not $condition) {
        throw $message
    }
}

function Read-PackageXml([object] $package) {
    $manifest = Get-AppxPackageManifest -Package $package
    $namespaces = New-Object System.Xml.XmlNamespaceManager($manifest.NameTable)
    $namespaces.AddNamespace('f', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
    $namespaces.AddNamespace('uap', 'http://schemas.microsoft.com/appx/manifest/uap/windows10')
    $namespaces.AddNamespace('rescap', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedCapabilities')
    return [pscustomobject]@{
        Xml = $manifest
        Namespaces = $namespaces
        Identity = $manifest.SelectSingleNode('/f:Package/f:Identity', $namespaces)
        Application = $manifest.SelectSingleNode('/f:Package/f:Applications/f:Application', $namespaces)
        VisualElements = $manifest.SelectSingleNode('/f:Package/f:Applications/f:Application/uap:VisualElements', $namespaces)
        FullTrust = $manifest.SelectSingleNode('/f:Package/f:Capabilities/rescap:Capability[@Name="runFullTrust"]', $namespaces)
    }
}

function Assert-PackageContract([object] $package, [string] $expectedVersion) {
    Assert-Equal $packageName $package.Name 'The MSIX package name changed.'
    Assert-Equal $publisher $package.Publisher 'The MSIX publisher changed.'
    Assert-Equal $expectedVersion $package.Version.ToString() 'The MSIX version is not the expected four-part version.'

    $xml = Read-PackageXml $package
    Assert-True ($null -ne $xml.Identity) 'The installed package has no identity.'
    Assert-Equal $packageName $xml.Identity.Name 'The manifest identity name changed.'
    Assert-Equal $publisher $xml.Identity.Publisher 'The manifest publisher does not match the signed publisher.'
    Assert-Equal $expectedVersion $xml.Identity.Version 'The manifest does not contain the expected four-part version.'
    Assert-Equal 'x64' $xml.Identity.ProcessorArchitecture 'The lifecycle test must exercise the x64 package.'
    Assert-True ($null -ne $xml.Application) 'The manifest has no application entry.'
    Assert-Equal 'rclicker.exe' $xml.Application.Executable 'The manifest executable changed.'
    Assert-Equal 'Windows.FullTrustApplication' $xml.Application.EntryPoint 'The app is not declared as a full-trust desktop application.'
    Assert-True ($null -ne $xml.VisualElements) 'The manifest has no visual elements.'
    Assert-True ($null -ne $xml.FullTrust) 'The manifest is missing runFullTrust.'
}

function Stop-RClicker {
    if ($null -eq $script:runningProcess) {
        return
    }

    if (-not $script:runningProcess.HasExited) {
        $script:runningProcess.CloseMainWindow() | Out-Null
        if (-not $script:runningProcess.WaitForExit(10000)) {
            Stop-Process -Id $script:runningProcess.Id -Force
        }
    }

    $script:runningProcess = $null
}

New-Item $workDirectory -ItemType Directory -Force | Out-Null
$signtool = $null
$firstPackage = $null
try {
    $existing = @(Get-AppxPackage -Name $packageName -ErrorAction SilentlyContinue)
    if ($existing.Count -gt 0) {
        throw "Refusing to remove an existing $packageName installation. Run this test on an isolated Windows account or remove the test package yourself first."
    }

    if ([string]::IsNullOrWhiteSpace($PackagePath)) {
        Push-Location $repository
        try {
            & (Join-Path $repository 'tools/build-msix.ps1') -RuntimeIdentifier win-x64 -OutputDirectory $firstOutput -PackageVersion $version
            if ($LASTEXITCODE -ne 0) {
                throw "The first MSIX build failed with exit code $LASTEXITCODE."
            }
        }
        finally {
            Pop-Location
        }

        $firstPackage = Join-Path $firstOutput 'rclicker-x64.msix'
    }
    else {
        $firstPackage = (Resolve-Path $PackagePath).Path
    }

    Push-Location $repository
    try {
        & (Join-Path $repository 'tools/build-msix.ps1') -RuntimeIdentifier win-x64 -OutputDirectory $updateOutput -PackageVersion $updateVersion
        if ($LASTEXITCODE -ne 0) {
            throw "The update MSIX build failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }

    $updatePackage = Join-Path $updateOutput 'rclicker-x64.msix'
    $signtool = Find-Tool 'signtool.exe'

    $certificate = New-SelfSignedCertificate -Type Custom -KeyUsage DigitalSignature -Subject $publisher `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}') `
        -FriendlyName "rclicker MSIX lifecycle test $([guid]::NewGuid().ToString('N'))"
    Export-Certificate -Cert $certificate -FilePath $certificateFile | Out-Null
    Import-Certificate -FilePath $certificateFile -CertStoreLocation 'Cert:\CurrentUser\TrustedPeople' | Out-Null

    foreach ($packagePath in @($firstPackage, $updatePackage)) {
        & $signtool sign /fd SHA256 /sha1 $certificate.Thumbprint $packagePath
        if ($LASTEXITCODE -ne 0) {
            throw "signtool failed to sign '$packagePath' with exit code $LASTEXITCODE."
        }
    }

    Add-AppxPackage -Path $firstPackage
    $installedPackage = Get-AppxPackage -Name $packageName
    Assert-PackageContract $installedPackage $version
    $packageFamilyName = $installedPackage.PackageFamilyName
    Assert-True ($installedPackage.InstallLocation -like '*WindowsApps*') 'The installed package is not running from the WindowsApps package location.'
    if (@(Get-Process -Name rclicker -ErrorAction SilentlyContinue).Count -gt 0) {
        throw 'An rclicker process is already running; the package runtime check requires an isolated account.'
    }

    Start-Process explorer.exe -ArgumentList "shell:AppsFolder\$packageFamilyName!RClicker"
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        Start-Sleep -Milliseconds 500
        $script:runningProcess = Get-Process -Name rclicker -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -like "$($installedPackage.InstallLocation)*" } |
            Select-Object -First 1
        if ($null -ne $script:runningProcess) {
            break
        }
    }
    Assert-True ($null -ne $script:runningProcess) 'The installed MSIX did not launch through the AppsFolder activation path.'
    Write-Host "Packaged runtime launched: PID $($script:runningProcess.Id)"
    Stop-RClicker

    Add-AppxPackage -Path $updatePackage
    $installedPackage = Get-AppxPackage -Name $packageName
    Assert-PackageContract $installedPackage $updateVersion
    Write-Host "In-place package update passed: $($installedPackage.Version)"

    Remove-AppxPackage -Package $installedPackage.PackageFullName
    $installedPackage = $null
    Assert-True (@(Get-AppxPackage -Name $packageName -ErrorAction SilentlyContinue).Count -eq 0) 'The MSIX package remained installed after uninstall.'
    Write-Host 'MSIX install, full-trust manifest, packaged runtime, in-place update, and uninstall checks passed.'
}
finally {
    Stop-RClicker
    if ($null -ne $installedPackage) {
        Remove-AppxPackage -Package $installedPackage.PackageFullName -ErrorAction SilentlyContinue
    }
    if ($null -ne $certificate) {
        Remove-Item "Cert:\CurrentUser\TrustedPeople\$($certificate.Thumbprint)" -Force -ErrorAction SilentlyContinue
        Remove-Item "Cert:\CurrentUser\My\$($certificate.Thumbprint)" -Force -ErrorAction SilentlyContinue
    }
    if (-not $KeepArtifacts -and (Test-Path $workDirectory)) {
        Remove-Item $workDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
