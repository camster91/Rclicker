$ErrorActionPreference = 'Stop'

$workflow = Get-Content (Join-Path $PSScriptRoot '../.github/workflows/ci.yml') -Raw

function Assert-Contains([string] $text, [string] $expected, [string] $message) {
    if (-not $text.Contains($expected)) {
        throw "$message (missing '$expected')."
    }
}

function Assert-Matches([string] $text, [string] $pattern, [string] $message) {
    if ($text -notmatch $pattern) {
        throw "$message (pattern '$pattern' did not match)."
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

function Assert-Equal([object] $expected, [object] $actual, [string] $message) {
    if ($expected -ne $actual) {
        throw "$message (expected '$expected', received '$actual')."
    }
}

function Test-ReleaseGuard([string] $eventName, [string] $ref, [bool] $publishRelease, [string] $buildResult = 'success', [string] $relayResult = 'success', [bool] $relayConfigured = $true, [string] $signResult = 'success') {
    $mainManual = $eventName -eq 'workflow_dispatch' -and $publishRelease -and $ref -eq 'refs/heads/main'
    $previewPush = $eventName -eq 'push' -and $ref.StartsWith('refs/heads/claude/')
    $checksPassed = $buildResult -eq 'success' -and $relayResult -eq 'success' -and $relayConfigured -and ($signResult -eq 'success' -or $signResult -eq 'skipped')
    return ($mainManual -or $previewPush) -and $checksPassed
}

Assert-Matches $workflow '(?ms)^  workflow_dispatch:\r?\n    inputs:\r?\n      publish_release:' 'Manual publication must expose an explicit workflow input.'
Assert-Contains $workflow "default: false" 'Manual publication must default to artifact-only behavior.'
Assert-Contains $workflow "type: boolean" 'Manual publication must use a boolean confirmation input.'

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
Assert-Contains $release "github.event_name == 'workflow_dispatch'" 'Main publication must require a manual workflow run.'
Assert-Contains $release 'inputs.publish_release == true' 'Main publication must require the explicit publish checkbox.'
Assert-Contains $release "github.ref == 'refs/heads/main'" 'The portable release must remain main-only.'
Assert-Contains $release "github.event_name == 'push' && startsWith(github.ref, 'refs/heads/claude/')" 'Existing claude preview pushes must remain supported.'
Assert-Contains $release "needs.build.result == 'success'" 'Publication must require a passing build.'
Assert-Contains $release "needs.relay.result == 'success'" 'Publication must require a passing relay job.'
Assert-Contains $release "needs.build.outputs.relay == 'true'" 'Publication must require a configured relay.'
Assert-Contains $release "needs.sign.result == 'success' || needs.sign.result == 'skipped'" 'Publication must preserve the existing signing gate.'

Assert-Equal $false (Test-ReleaseGuard 'push' 'refs/heads/main' $false) 'A main push must not publish.'
Assert-Equal $false (Test-ReleaseGuard 'workflow_dispatch' 'refs/heads/main' $false) 'A default manual main run must not publish.'
Assert-Equal $true (Test-ReleaseGuard 'workflow_dispatch' 'refs/heads/main' $true) 'An explicitly approved manual main run should publish after passing gates.'
Assert-Equal $false (Test-ReleaseGuard 'workflow_dispatch' 'refs/heads/main' $true 'failure') 'A manual main run with a failed build must not publish.'
Assert-Equal $true (Test-ReleaseGuard 'push' 'refs/heads/claude/example' $false) 'A claude preview push should retain its existing publication behavior.'
Assert-Equal $false (Test-ReleaseGuard 'push' 'refs/heads/feature/example' $true) 'Other branch pushes must not publish.'

Write-Host 'MSIX signing and release workflow guards: 26 scenarios passed.'
