$ErrorActionPreference = 'Stop'

# Execute the actual shared workflow step with synthetic Authenticode results.
# No certificate, Azure access, or signing side effects are needed for these tests.
$workflow = Get-Content (Join-Path $PSScriptRoot '../.github/workflows/sign-windows.yml') -Raw
$match = [regex]::Match($workflow, '(?ms)^      - name: Verify signatures\r?\n.*?^        run: \|\r?\n(?<code>(?:^          [^\r\n]*\r?\n|^\r?\n)+)')
if (-not $match.Success) { throw 'Cannot locate the shared verification step.' }
$code = [regex]::Replace($match.Groups['code'].Value, '(?m)^          ', '')
$verify = [scriptblock]::Create($code)

function Get-ChildItem {
    param($Path, [switch]$Recurse, [switch]$File, $Include)
    return $script:files
}

function Get-AuthenticodeSignature {
    param($LiteralPath)
    $script:checked += $LiteralPath
    return $script:signatures[$LiteralPath]
}

function New-Signature {
    param($Name = 'Cameron Ashley', $Status = 'Valid', $Timestamp = $true)
    $certificate = [pscustomobject]@{ Subject = "CN=$Name"; Name = $Name }
    $certificate | Add-Member ScriptMethod GetNameInfo { param($Type, $Issuer) return $this.Name }
    return [pscustomobject]@{
        Status = $Status
        SignerCertificate = $certificate
        TimeStamperCertificate = $(if ($Timestamp) { [pscustomobject]@{ Subject = 'CN=Test timestamp' } } else { $null })
    }
}

function Reset-Fixture {
    $env:FILE_TYPES = 'exe,dll,msi,msix'
    $env:ARTIFACT_NAME = 'synthetic-artifact'
    $env:AZURE_SIGNING_PUBLISHER = 'Cameron Ashley'
    $script:files = @(
        [pscustomobject]@{ Name = 'app.exe'; FullName = 'files/app.exe' },
        [pscustomobject]@{ Name = 'setup.msi'; FullName = 'files/nested/setup.msi' }
    )
    $script:signatures = @{
        'files/app.exe' = New-Signature
        'files/nested/setup.msi' = New-Signature
    }
    $script:checked = @()
}

function Assert-Rejected {
    param($ExpectedMessage)
    $failure = $null
    try { & $verify | Out-Null } catch { $failure = $_.Exception.Message }
    if (-not $failure -or $failure -notlike "*$ExpectedMessage*") {
        throw "Expected rejection '$ExpectedMessage'; got '$failure'."
    }
}

Reset-Fixture
& $verify | Out-Null
if ($script:checked.Count -ne 2) { throw 'Not all files were verified.' }

Reset-Fixture
$script:files = @()
Assert-Rejected 'No files'

Reset-Fixture
$script:signatures['files/app.exe'] = New-Signature -Status 'HashMismatch'
Assert-Rejected 'HashMismatch'

Reset-Fixture
$script:signatures['files/nested/setup.msi'] = New-Signature -Timestamp $false
Assert-Rejected 'timestamp is missing'

Reset-Fixture
$script:signatures['files/nested/setup.msi'] = New-Signature -Name 'Another Publisher'
Assert-Rejected 'publisher does not match'

Reset-Fixture
$script:signatures['files/app.exe'] = New-Signature -Name 'cameron ashley'
Assert-Rejected 'publisher does not match'

Reset-Fixture
$script:signatures['files/app.exe'].SignerCertificate = $null
Assert-Rejected 'signer certificate is missing'

Reset-Fixture
$env:AZURE_SIGNING_PUBLISHER = ' '
Assert-Rejected 'Expected publisher is required'

Write-Host 'Shared signature verification: 8 scenarios passed.'
