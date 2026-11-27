[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TrxPath,
    [Parameter(Mandatory = $true)]
    [int]$MinimumTotal
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $TrxPath -PathType Leaf)) { throw "TRX result not found: $TrxPath" }
[xml]$result = Get-Content -LiteralPath $TrxPath -Raw
$counters = $result.TestRun.ResultSummary.Counters
if ($null -eq $counters) { throw 'TRX result summary is missing.' }
$total = [int]$counters.total
$passed = [int]$counters.passed
$blocking = @('failed', 'error', 'timeout', 'aborted', 'inconclusive', 'passedButRunAborted', 'notRunnable', 'notExecuted')
$failures = @($blocking | Where-Object { [int]$counters.$_ -ne 0 })
if ($total -lt $MinimumTotal -or $passed -ne $total -or $failures.Count -gt 0) {
    throw "CI tests did not complete cleanly: total=$total passed=$passed blocking=$($failures -join ',')."
}
Write-Host "PASS: $passed CI tests completed with zero failures and zero skips."
