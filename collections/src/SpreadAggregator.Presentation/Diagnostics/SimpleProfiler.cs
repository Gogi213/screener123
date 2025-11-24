using System.Diagnostics;

namespace SpreadAggregator.Presentation.Diagnostics;

/// <summary>
/// Lightweight performance profiler - logs CPU and Memory every N seconds
/// </summary>
public class SimpleProfiler : IDisposable
{
    private readonly Timer _timer;
    private readonly Process _process;
    private DateTime _lastCheck;
    private TimeSpan _lastCpuTime;
    
    public SimpleProfiler(int intervalSeconds = 10)
    {
        _process = Process.GetCurrentProcess();
        _lastCheck = DateTime.UtcNow;
        _lastCpuTime = _process.TotalProcessorTime;
        
        _timer = new Timer(LogMetrics, null, 
            TimeSpan.FromSeconds(intervalSeconds), 
            TimeSpan.FromSeconds(intervalSeconds));
        
        Console.WriteLine("[PROFILER] Started - logging every {0} seconds", intervalSeconds);
    }
    
    private void LogMetrics(object? state)
    {
        try
        {
            var now = DateTime.UtcNow;
            var currentCpuTime = _process.TotalProcessorTime;
            
            // CPU %
            var cpuUsedMs = (currentCpuTime - _lastCpuTime).TotalMilliseconds;
            var totalMsPassed = (now - _lastCheck).TotalMilliseconds;
            var cpuPercent = (cpuUsedMs / (Environment.ProcessorCount * totalMsPassed)) * 100.0;
            
            // Memory
            _process.Refresh();
            var memoryMB = _process.WorkingSet64 / 1024 / 1024;
            var gcMemoryMB = GC.GetTotalMemory(false) / 1024 / 1024;
            
            // GC stats
            var gen0 = GC.CollectionCount(0);
            var gen1 = GC.CollectionCount(1);
            var gen2 = GC.CollectionCount(2);
            
            Console.WriteLine(
                "[PROFILER] CPU: {0:F1}% | Memory: {1} MB (GC: {2} MB) | GC: Gen0={3} Gen1={4} Gen2={5}",
                Math.Min(100, Math.Max(0, cpuPercent)),
                memoryMB,
                gcMemoryMB,
                gen0, gen1, gen2
            );
            
            _lastCheck = now;
            _lastCpuTime = currentCpuTime;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[PROFILER] Error: {0}", ex.Message);
        }
    }
    
    public void Dispose()
    {
        _timer?.Dispose();
        Console.WriteLine("[PROFILER] Stopped");
    }
}
