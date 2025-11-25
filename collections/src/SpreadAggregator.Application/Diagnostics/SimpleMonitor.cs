using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SpreadAggregator.Application.Diagnostics;

/// <summary>
/// Simple monitoring for leak detection: CPU, Memory, Symbol count
/// Logs metrics every 10 seconds to console for manual inspection
/// </summary>
public class SimpleMonitor : IDisposable
{
    private readonly Process _process;
    private readonly PeriodicTimer _timer;
    private readonly Task _monitoringTask;
    private readonly CancellationTokenSource _cts;

    private DateTime _lastCheck;
    private TimeSpan _lastCpuTime;
    private long _lastMemory;

    public SimpleMonitor()
    {
        _process = Process.GetCurrentProcess();
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        _cts = new CancellationTokenSource();

        _lastCheck = DateTime.UtcNow;
        _lastCpuTime = _process.TotalProcessorTime;
        _lastMemory = GC.GetTotalMemory(false);

        _monitoringTask = MonitorLoop(_cts.Token);

        Console.WriteLine("[SimpleMonitor] Started. Metrics every 10 seconds.");
    }

    private async Task MonitorLoop(CancellationToken cancellationToken)
    {
        while (await _timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                var now = DateTime.UtcNow;
                var elapsed = (now - _lastCheck).TotalSeconds;

                // CPU Usage (percentage)
                var currentCpuTime = _process.TotalProcessorTime;
                var cpuDelta = (currentCpuTime - _lastCpuTime).TotalMilliseconds;
                var cpuUsage = (cpuDelta / (elapsed * 1000)) * 100;

                // Memory (MB)
                var workingSetMB = _process.WorkingSet64 / 1024.0 / 1024.0;
                var managedHeapMB = GC.GetTotalMemory(false) / 1024.0 / 1024.0;
                var memoryDeltaMB = (GC.GetTotalMemory(false) - _lastMemory) / 1024.0 / 1024.0;

                // GC Stats
                var gen0 = GC.CollectionCount(0);
                var gen1 = GC.CollectionCount(1);
                var gen2 = GC.CollectionCount(2);

                // Log metrics
                Console.WriteLine(
                    $"[Monitor] CPU: {cpuUsage:F1}% | " +
                    $"Memory: {workingSetMB:F1} MB (Δ {memoryDeltaMB:+0.0;-0.0} MB) | " +
                    $"Heap: {managedHeapMB:F1} MB | " +
                    $"GC: G0={gen0} G1={gen1} G2={gen2}"
                );

                // Alert on anomalies
                if (cpuUsage > 80)
                {
                    Console.WriteLine($"[Monitor] ⚠️ HIGH CPU: {cpuUsage:F1}%");
                }

                if (workingSetMB > 1000) // 1 GB threshold
                {
                    Console.WriteLine($"[Monitor] ⚠️ HIGH MEMORY: {workingSetMB:F1} MB");
                }

                if (memoryDeltaMB > 50) // Growing > 50MB per 10 sec = potential leak
                {
                    Console.WriteLine($"[Monitor] ⚠️ MEMORY LEAK SUSPECTED: +{memoryDeltaMB:F1} MB");
                }

                // Update last values
                _lastCheck = now;
                _lastCpuTime = currentCpuTime;
                _lastMemory = GC.GetTotalMemory(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Monitor] Error: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _timer.Dispose();
        _cts.Dispose();
        Console.WriteLine("[SimpleMonitor] Stopped.");
    }
}
