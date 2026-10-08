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

function Get-ReleaseIfExpression([string] $releaseBlock) {
    $lines = $releaseBlock -split '\r?\n'
    $ifIndex = -1
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -eq '    if: >-') {
            $ifIndex = $index
            break
        }
    }
    if ($ifIndex -lt 0) {
        throw 'The release job has no folded if expression to evaluate.'
    }

    $expressionLines = @()
    for ($index = $ifIndex + 1; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -notmatch '^      ') {
            break
        }
        $expressionLines += $lines[$index].Substring(6)
    }
    if ($expressionLines.Count -eq 0) {
        throw 'The release job has an empty folded if expression.'
    }

    return ($expressionLines -join ' ').Trim()
}

function Evaluate-ReleaseIf([string] $expression, [string] $eventName, [string] $ref, [bool] $publishRelease, [string] $buildResult = 'success', [string] $relayResult = 'success', [bool] $relayConfigured = $true, [string] $signResult = 'success') {
    $values = @{
        'always()' = $true
        "github.event_name == 'workflow_dispatch'" = ($eventName -eq 'workflow_dispatch')
        "inputs.publish_release == true" = $publishRelease
        "github.ref == 'refs/heads/main'" = ($ref -eq 'refs/heads/main')
        "github.event_name == 'push'" = ($eventName -eq 'push')
        "startsWith(github.ref, 'refs/heads/claude/')" = $ref.StartsWith('refs/heads/claude/')
        "needs.build.result == 'success'" = ($buildResult -eq 'success')
        "needs.relay.result == 'success'" = ($relayResult -eq 'success')
        "needs.build.outputs.relay == 'true'" = $relayConfigured
        "needs.sign.result == 'success'" = ($signResult -eq 'success')
        "needs.sign.result == 'skipped'" = ($signResult -eq 'skipped')
    }

    $evaluated = $expression
    foreach ($entry in ($values.GetEnumerator() | Sort-Object { $_.Key.Length } -Descending)) {
        $replacement = if ($entry.Value) { '$true' } else { '$false' }
        $evaluated = $evaluated.Replace($entry.Key, $replacement)
    }
    $evaluated = $evaluated.Replace('&&', ' -and ').Replace('||', ' -or ')
    return [bool](& ([scriptblock]::Create($evaluated)))
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

$releaseExpression = Get-ReleaseIfExpression $release
Assert-Equal $false (Evaluate-ReleaseIf $releaseExpression 'push' 'refs/heads/main' $false) 'A main push must not publish.'
Assert-Equal $false (Evaluate-ReleaseIf $releaseExpression 'workflow_dispatch' 'refs/heads/main' $false) 'A default manual main run must not publish.'
Assert-Equal $true (Evaluate-ReleaseIf $releaseExpression 'workflow_dispatch' 'refs/heads/main' $true) 'An explicitly approved manual main run should publish after passing gates.'
Assert-Equal $false (Evaluate-ReleaseIf $releaseExpression 'workflow_dispatch' 'refs/heads/main' $true 'failure') 'A manual main run with a failed build must not publish.'
Assert-Equal $false (Evaluate-ReleaseIf $releaseExpression 'workflow_dispatch' 'refs/heads/main' $true 'success' 'failure') 'A manual main run with a failed relay job must not publish.'
Assert-Equal $false (Evaluate-ReleaseIf $releaseExpression 'workflow_dispatch' 'refs/heads/main' $true 'success' 'success' $true 'failure') 'A manual main run with failed signing must not publish.'
Assert-Equal $true (Evaluate-ReleaseIf $releaseExpression 'push' 'refs/heads/claude/example' $false 'success' 'success' $true 'skipped') 'A claude preview push should retain its existing publication behavior.'
Assert-Equal $false (Evaluate-ReleaseIf $releaseExpression 'push' 'refs/heads/feature/example' $true) 'Other branch pushes must not publish.'

Write-Host 'MSIX signing workflow invariants passed; release if-expression scenarios: 8 passed.'
