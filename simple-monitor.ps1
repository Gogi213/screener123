# Simple Process Monitor - tracks dotnet.exe RAM/CPU usage
param([int]$Seconds = 1800)  # Default 30 minutes

$startTime = Get-Date
$logFile = "process_monitor_$(Get-Date -Format 'yyyyMMdd_HHmmss').csv"

Write-Host "==================================" -ForegroundColor Cyan
Write-Host "  Process Monitor (30 min test)" -ForegroundColor Cyan
Write-Host "==================================" -ForegroundColor Cyan
Write-Host ""

# CSV header
"Timestamp,ElapsedMin,ProcessCount,TotalCPU,TotalRAM_MB,AvgRAM_MB" | Out-File $logFile

$iteration = 0
while ($true) {
    $now = Get-Date
    $elapsed = ($now - $startTime).TotalMinutes
    
    if ($elapsed -ge ($Seconds / 60)) {
        Write-Host "`nTest complete! Ran for $([math]::Round($elapsed, 1)) minutes" -ForegroundColor Green
        break
    }
    
    # Get all dotnet processes
    $processes = Get-Process dotnet -ErrorAction SilentlyContinue
    
    if (-not $processes) {
        Write-Host "No dotnet process found!" -ForegroundColor Red
        Start-Sleep -Seconds 5
        continue
    }
    
    # Calculate totals
    $procCount = @($processes).Count
    $totalRAM = ($processes | Measure-Object WorkingSet64 -Sum).Sum / 1MB
    $avgRAM = $totalRAM / $procCount
    $totalCPU = ($processes | Measure-Object CPU -Sum).Sum
    
    # Log
    $timestamp = $now.ToString("HH:mm:ss")
    "$timestamp,$([math]::Round($elapsed, 1)),$procCount,$([math]::Round($totalCPU, 2)),$([math]::Round($totalRAM, 1)),$([math]::Round($avgRAM, 1))" | Out-File $logFile -Append
    
    # Display every 10 iterations (50 seconds)
    $iteration++
    if ($iteration % 10 -eq 0) {
        Clear-Host
        Write-Host "==================================" -ForegroundColor Cyan
        Write-Host "  Process Monitor" -ForegroundColor Cyan
        Write-Host "==================================" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "Runtime:   $([math]::Round($elapsed, 1)) / 30.0 minutes" -ForegroundColor Yellow
        Write-Host "Progress:  $([math]::Round($elapsed / 30 * 100, 1))%" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "Processes: $procCount" -ForegroundColor White
        Write-Host "Total RAM: $([math]::Round($totalRAM, 1)) MB" -ForegroundColor White
        Write-Host "Avg RAM:   $([math]::Round($avgRAM, 1)) MB" -ForegroundColor White
        Write-Host "Total CPU: $([math]::Round($totalCPU, 2)) sec" -ForegroundColor White
        Write-Host ""
        Write-Host "Log: $logFile" -ForegroundColor DarkGray
        Write-Host "Press Ctrl+C to stop" -ForegroundColor DarkGray
    }
    
    Start-Sleep -Seconds 5
}

Write-Host "`nResults saved to: $logFile" -ForegroundColor Green
Write-Host "`nAnalyzing..." -ForegroundColor Yellow

# Analysis
$data = Import-Csv $logFile
$ramValues = $data | Select-Object -ExpandProperty TotalRAM_MB | ForEach-Object { [double]$_ }
$avgRam = ($ramValues | Measure-Object -Average).Average
$maxRam = ($ramValues | Measure-Object -Maximum).Maximum
$minRam = ($ramValues | Measure-Object -Minimum).Minimum

Write-Host ""
Write-Host "RAM Analysis:" -ForegroundColor Green
Write-Host "  Min:  $([math]::Round($minRam, 1)) MB" -ForegroundColor White
Write-Host "  Avg:  $([math]::Round($avgRam, 1)) MB" -ForegroundColor White
Write-Host "  Max:  $([math]::Round($maxRam, 1)) MB" -ForegroundColor White
Write-Host ""

if ($maxRam - $minRam -gt 50) {
    Write-Host "⚠ WARNING: RAM grew by $([math]::Round($maxRam - $minRam, 1)) MB - possible memory leak!" -ForegroundColor Red
} else {
    Write-Host "✅ RAM stable (variation: $([math]::Round($maxRam - $minRam, 1)) MB)" -ForegroundColor Green
}
