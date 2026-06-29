$ErrorActionPreference = 'Continue'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$pidFile = Join-Path $root 'artifacts/dev-run/pids.json'
if (-not (Test-Path $pidFile)) {
    Write-Host 'No Vortex dev pid file found.'
    exit 0
}
$pids = Get-Content $pidFile -Raw | ConvertFrom-Json
foreach ($entry in $pids) {
    try {
        $process = Get-Process -Id $entry.Id -ErrorAction Stop
        Stop-Process -Id $entry.Id -Force
        Write-Host "Stopped $($entry.Name) PID $($entry.Id)"
    } catch {
        Write-Host "Already stopped: $($entry.Name) PID $($entry.Id)"
    }
}
Remove-Item $pidFile -Force
