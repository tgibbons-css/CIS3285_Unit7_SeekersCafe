param([Parameter(Mandatory = $true)][string]$ManifestPath)
$ErrorActionPreference = 'Stop'
$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
foreach ($service in $manifest.services) {
    $process = Get-Process -Id $service.id -ErrorAction SilentlyContinue
    if ($null -eq $process) { continue }
    # Avoid stopping an unrelated process if Windows has reused a saved PID.
    if ($process.ProcessName -ne 'dotnet' -or $process.StartTime.ToUniversalTime().ToString('o') -ne $service.started) {
        throw "Process identity changed for $($service.name); it was not stopped."
    }
    Stop-Process -Id $process.Id
    Write-Output "Stopped $($service.name)."
}
