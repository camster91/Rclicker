$ErrorActionPreference = 'Stop'

$workflow = Get-Content (Join-Path $PSScriptRoot '../.github/workflows/ci.yml') -Raw

function Assert-Contains([string] $text, [string] $expected, [string] $message) {
    if (-not $text.Contains($expected)) {
        throw "$message (missing '$expected')."
    }
}

function Get-JobBlock([string] $name) {
    $escapedName = [regex]::Escape($name)
    $match = [regex]::Match($workflow, "(?ms)^  $escapedName`:\r?\n(?<body>.*?)(?=^  [A-Za-z0-9_-]+`:\r?\n|\z)")
    if (-not $match.Success) {
        throw "The CI workflow has no '$name' job."
    }

    return $match.Groups['body'].Value
}

$signMsix = Get-JobBlock 'sign-msix'
Assert-Contains $signMsix "needs: [build, relay]" 'MSIX signing must wait for the build and relay checks.'
Assert-Contains $signMsix "github.event_name == 'push' || github.event_name == 'workflow_dispatch'" 'MSIX signing must be limited to push and manual workflow runs.'
Assert-Contains $signMsix "github.ref == 'refs/heads/main'" 'MSIX signing must be limited to main.'
Assert-Contains $signMsix "needs.build.outputs.relay == 'true'" 'MSIX signing must require a configured relay build.'
Assert-Contains $signMsix 'id-token: write' 'MSIX signing must use OIDC permissions.'
Assert-Contains $signMsix 'uses: ./.github/workflows/sign-windows.yml' 'MSIX signing must use the shared signing workflow.'
Assert-Contains $signMsix 'artifact: rclicker-msix-win-x64-unsigned' 'MSIX signing must consume the unsigned MSIX artifact.'
Assert-Contains $signMsix 'file-types: msix' 'MSIX signing must restrict the shared signer to MSIX files.'

$release = Get-JobBlock 'release'
if ($release.Contains('sign-msix') -or $release.Contains('rclicker-msix-win-x64-signed')) {
    throw 'The release job must not consume or publish the signed MSIX artifact.'
}

Write-Host 'MSIX signing workflow guards: 9 scenarios passed.'
