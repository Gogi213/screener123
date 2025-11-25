# Performance Monitor for screener123
# Monitors CPU, RAM, GPU usage for dotnet.exe process

param(
    [int]$IntervalSeconds = 5,
    [string]$LogFile = "performance_log.csv"
)

Write-Host "====================================" -ForegroundColor Cyan
Write-Host "  screener123 Performance Monitor" -ForegroundColor Cyan
Write-Host "====================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Monitoring interval: $IntervalSeconds seconds" -ForegroundColor Yellow
Write-Host "Log file: $LogFile" -ForegroundColor Yellow
Write-Host ""
Write-Host "Waiting for dotnet.exe process..." -ForegroundColor Yellow

# Wait for dotnet process
while (-not (Get-Process dotnet -ErrorAction SilentlyContinue)) {
    Start-Sleep -Seconds 1
}

Write-Host "dotnet.exe found! Starting monitoring..." -ForegroundColor Green
Write-Host ""

# Create CSV header
"Timestamp,CPU %,RAM MB,GPU %,GC Gen0,GC Gen1,GC Gen2,ThreadPool Threads,ThreadPool Queue" | Out-File $LogFile

# Monitoring loop
$startTime = Get-Date
$iteration = 0

while ($true) {
    $iteration++
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $elapsed = (Get-Date) - $startTime
    
    # Get all dotnet processes
    $processes = Get-Process dotnet -ErrorAction SilentlyContinue
    
    if (-not $processes) {
        Write-Host "dotnet.exe not found. Stopping monitor." -ForegroundColor Red
        break
    }
    
    # Aggregate metrics across all dotnet processes
    $totalCpu = 0
    $totalRam = 0
    
    foreach ($proc in $processes) {
        $totalCpu += $proc.CPU
        $totalRam += $proc.WorkingSet64 / 1MB
    }
    
    # CPU percentage (approximate)
    $cpuPercent = [math]::Round($totalCpu / $elapsed.TotalSeconds, 2)
    $ramMB = [math]::Round($totalRam, 2)
    
    # GPU usage (requires Windows 10/11)
    $gpuPercent = 0
    try {
        $gpu = Get-Counter '\GPU Engine(*engtype_3D)\Utilization Percentage' -ErrorAction SilentlyContinue
        if ($gpu) {
            $gpuPercent = [math]::Round(($gpu.CounterSamples | Measure-Object -Property CookedValue -Sum).Sum, 2)
        }
    } catch {
        $gpuPercent = "N/A"
    }
    
    # .NET metrics (placeholder - would need dotnet-counters for real data)
    $gcGen0 = "N/A"
    $gcGen1 = "N/A"
    $gcGen2 = "N/A"
    $threadPoolThreads = "N/A"
    $threadPoolQueue = "N/A"
    
    # Display current stats
    Clear-Host
    Write-Host "====================================" -ForegroundColor Cyan
    Write-Host "  screener123 Performance Monitor" -ForegroundColor Cyan
    Write-Host "====================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Runtime: $($elapsed.ToString('hh\:mm\:ss'))" -ForegroundColor Yellow
    Write-Host "Samples: $iteration" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "--- SYSTEM METRICS ---" -ForegroundColor Green
    Write-Host "CPU Usage:    $cpuPercent %" -ForegroundColor White
    Write-Host "RAM Usage:    $ramMB MB" -ForegroundColor White
    Write-Host "GPU Usage:    $gpuPercent %" -ForegroundColor White
    Write-Host ""
    Write-Host "--- .NET METRICS ---" -ForegroundColor Green
    Write-Host "GC Gen0:      $gcGen0"
    Write-Host "GC Gen1:      $gcGen1"
    Write-Host "GC Gen2:      $gcGen2"
    Write-Host "ThreadPool:   $threadPoolThreads threads"
    Write-Host "TP Queue:     $threadPoolQueue items"
    Write-Host ""
    Write-Host "Press Ctrl+C to stop monitoring" -ForegroundColor DarkGray
    
    # Log to CSV
    "$timestamp,$cpuPercent,$ramMB,$gpuPercent,$gcGen0,$gcGen1,$gcGen2,$threadPoolThreads,$threadPoolQueue" | Out-File $LogFile -Append
    
    # Sleep
    Start-Sleep -Seconds $IntervalSeconds
}

Write-Host ""
Write-Host "Monitoring stopped. Log saved to: $LogFile" -ForegroundColor Green
