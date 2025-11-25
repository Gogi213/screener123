# screener123 - Performance Monitoring Commands

## 🎯 Quick Start

### 1. Start Application (Terminal 1)
```powershell
cd "c:\visual projects\screener123\collections"
dotnet build && dotnet run --project src\SpreadAggregator.Presentation
```

### 2. Start Performance Monitor (Terminal 2)
```powershell
cd "c:\visual projects\screener123"
.\monitor.ps1 -IntervalSeconds 5
```

This will monitor every 5 seconds and save to `performance_log.csv`

---

## 📊 Advanced: dotnet-counters (Better .NET Metrics)

If you want detailed .NET metrics (GC, ThreadPool, Memory), install dotnet-counters:

### Install (one-time)
```powershell
dotnet tool install --global dotnet-counters
```

### Monitor in real-time
```powershell
# Find dotnet process ID
Get-Process dotnet

# Monitor with dotnet-counters (replace PID)
dotnet-counters monitor -p <PID> --refresh-interval 5 System.Runtime Microsoft.AspNetCore.Hosting

# Example:
dotnet-counters monitor -p 12345 --refresh-interval 5 System.Runtime
```

### Key Metrics to Watch:
- **CPU Usage (%)** - should stay <10% for 2000 symbols
- **Working Set** - current RAM usage (MB)
- **GC Heap Size** - managed memory
- **Gen 0/1/2 Collections** - GC activity
- **ThreadPool Thread Count** - active threads
- **ThreadPool Queue Length** - backlog (should be ~0)

---

## 📈 After 30 Minutes

The `performance_log.csv` will contain:
- Timestamp
- CPU %
- RAM MB
- GPU %

You can analyze:
```powershell
# View log
Get-Content performance_log.csv | Select-Object -Last 20

# Calculate averages
Import-Csv performance_log.csv | Measure-Object -Property "RAM MB" -Average -Maximum

# Check for memory leaks (increasing RAM over time)
Import-Csv performance_log.csv | Select-Object Timestamp, "RAM MB" | Format-Table
```

---

## 🔍 What to Look For

### Good Signs ✅
- CPU: <5% average (2000 symbols)
- RAM: Stable around 100-200MB
- No continuous growth (memory leak)
- GC Gen2: <5 collections/min

### Bad Signs ❌
- CPU: >20% sustained
- RAM: Continuously growing (leak)
- GC Gen2: Frequent collections (GC pressure)
- ThreadPool Queue: >0 sustained (backlog)

---

## 💡 Quick Commands Reference

```powershell
# Start monitoring
.\monitor.ps1

# Check current dotnet processes
Get-Process dotnet | Select-Object Id, CPU, WS

# View live performance (Windows Task Manager alternative)
Get-Process dotnet | Format-Table ProcessName, Id, CPU, @{L='RAM MB';E={[math]::Round($_.WS/1MB,2)}} -AutoSize

# Kill all dotnet processes (if needed)
Stop-Process -Name dotnet -Force
```
