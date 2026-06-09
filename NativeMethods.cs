#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CPUUtilityHybrid;

/// <summary>
/// Managed replacement for the historical CPUUtilityNative.dll P/Invoke surface.
/// Eliminates the WinRing0 dependency while keeping the same call patterns
/// expected by the WinForms host.
/// </summary>
public static class NativeMethods
{
  private static readonly StressEngine Stress = new();
  private static readonly BenchmarkEngine Benchmark = new();
  private static readonly HardwareMetricsProvider MetricsProvider = new();
  private static readonly CpuInfoProvider CpuInfo = new();

  /// <summary>
  /// Gets the most recent failure description for operations that return false.
  /// </summary>
  public static string LastError { get; private set; } = string.Empty;

  private readonly record struct CpuInfoSnapshot
  {
    public string Model { get; init; }
    public int PhysicalCores { get; init; }
    public int LogicalCores { get; init; }
    public int MaxClockMHz { get; init; }
  }

  public static bool StartStressTest(int threadCount)
  {
    if (threadCount <= 0)
    {
      LastError = "Thread count must be greater than zero.";
      return false;
    }

    var result = Stress.Start(threadCount);
    LastError = result ? string.Empty : "CPU stress engine is already running.";
    return result;
  }

  public static bool StopStressTest()
  {
    Stress.Stop();
    LastError = string.Empty;
    return true;
  }

  public static bool PauseStressTest()
  {
    var result = Stress.Pause();
    LastError = result ? string.Empty : "CPU stress engine is not running.";
    return result;
  }

  public static bool ResumeStressTest()
  {
    var result = Stress.Resume();
    LastError = result ? string.Empty : "CPU stress engine is not running.";
    return result;
  }

  public static int GetActiveThreadCount() => Stress.ActiveThreadCount;

  public static double RunSingleCoreBenchmark() => Benchmark.RunSingleThread();

  public static double RunMultiCoreBenchmark() => Benchmark.RunMultiThread();

  public static bool GetHardwareMetrics(ref HardwareMetrics metrics)
  {
    if (MetricsProvider.TryGetMetrics(out var result))
    {
      metrics = result;
      LastError = string.Empty;
      return true;
    }

    metrics = default;
    LastError = "Unable to gather hardware metrics (performance counters unavailable).";
    return false;
  }

  public static bool GetCPUInfo(StringBuilder modelName, int modelNameSize, ref int cores, ref int threads, ref int maxMHz)
  {
    if (CpuInfo.TryRead(out var info))
    {
      modelName.Clear();
      if (!string.IsNullOrWhiteSpace(info.Model) && modelNameSize > 0)
      {
        var truncated = info.Model.Length >= modelNameSize
          ? info.Model.Substring(0, modelNameSize - 1)
          : info.Model;
        modelName.Append(truncated);
      }

      cores = info.PhysicalCores;
      threads = info.LogicalCores;
      maxMHz = info.MaxClockMHz;
      LastError = string.Empty;
      return true;
    }

    LastError = "Unable to read CPU information.";
    return false;
  }

  public static string RunGPUBenchmarkDirectX(int width, int height, int samplesPerPixel, int maxBounces)
  {
    // Legacy API - now runs stress test instead
    return "Stress test mode";
  }

  /// <summary>
  /// Set process working set size to control memory usage.
  /// </summary>
  [DllImport("kernel32.dll")]
  public static extern bool SetProcessWorkingSetSize(IntPtr process, int minimumWorkingSetSize, int maximumWorkingSetSize);

  public struct HardwareMetrics
  {
    public double cpuLoad;
    public int cpuFreqMHz;
    public double tempC;
    public double voltage;
    public double packagePowerW;
    public bool isValid;
  }

  private sealed class StressEngine
  {
    private readonly object sync = new();
    private CancellationTokenSource? cts;
    private readonly ManualResetEventSlim pauseEvent = new(true);
    private readonly List<Task> workers = new();
    private int configuredThreads;

    public bool Start(int threadCount)
    {
      lock (sync)
      {
        if (cts != null)
        {
          return false;
        }

        cts = new CancellationTokenSource();
        pauseEvent.Set();
        configuredThreads = threadCount;
        workers.Clear();

        for (int i = 0; i < threadCount; i++)
        {
          workers.Add(Task.Run(() => WorkerLoop(cts.Token), cts.Token));
        }

        return true;
      }
    }

    public void Stop()
    {
      Task[] toWait;

      lock (sync)
      {
        if (cts == null)
        {
          configuredThreads = 0;
          return;
        }

        cts.Cancel();
        pauseEvent.Set();
        toWait = workers.ToArray();
        workers.Clear();
        configuredThreads = 0;
      }

      try
      {
        Task.WaitAll(toWait, TimeSpan.FromSeconds(2));
      }
      catch (AggregateException) { }
      catch (OperationCanceledException) { }

      lock (sync)
      {
        cts?.Dispose();
        cts = null;
      }
    }

    public bool Pause()
    {
      lock (sync)
      {
        if (cts == null)
        {
          return false;
        }

        pauseEvent.Reset();
        return true;
      }
    }

    public bool Resume()
    {
      lock (sync)
      {
        if (cts == null)
        {
          return false;
        }

        pauseEvent.Set();
        return true;
      }
    }

    public int ActiveThreadCount
    {
      get
      {
        lock (sync)
        {
          if (cts == null)
          {
            return 0;
          }

          return pauseEvent.IsSet ? configuredThreads : 0;
        }
      }
    }

    private void WorkerLoop(CancellationToken token)
    {
      var rng = new Random(Guid.NewGuid().GetHashCode());

      // 8 independent accumulators with no data dependency between chains.
      // This lets the CPU issue ops from multiple chains each cycle and keep
      // all FP execution ports (div/sqrt + transcendental + mul-add) busy,
      // rather than stalling on the latency of a single serial chain.
      double a0 = 1.1d + rng.NextDouble() * 0.8d;
      double a1 = 1.2d + rng.NextDouble() * 0.8d;
      double a2 = 1.3d + rng.NextDouble() * 0.8d;
      double a3 = 1.4d + rng.NextDouble() * 0.8d;
      double a4 = 1.5d + rng.NextDouble() * 0.8d;
      double a5 = 1.6d + rng.NextDouble() * 0.8d;
      double a6 = 1.7d + rng.NextDouble() * 0.8d;
      double a7 = 1.8d + rng.NextDouble() * 0.8d;
      uint tick = 0;

      try
      {
        while (!token.IsCancellationRequested)
        {
          pauseEvent.Wait(token);

          a0 = Math.Sqrt(a0 * 1.000001d + 1e-9d);
          a1 = Math.Sqrt(a1 * 1.000002d + 2e-9d);
          a2 = Math.Sin(a2) + 1.000001d;
          a3 = Math.Cos(a3) + 1.000001d;
          a4 = Math.Sqrt(a4 * 1.000003d + 3e-9d);
          a5 = Math.Sqrt(a5 * 1.000004d + 4e-9d);
          a6 = Math.Sin(a6 * 1.0000005d) + 1.000002d;
          a7 = Math.Cos(a7 * 1.0000005d) + 1.000002d;

          // Tiny cross-mix every ~64k iterations prevents dead-code elimination
          // without meaningfully affecting throughput or value ranges.
          if ((++tick & 0xFFFFu) == 0u)
          {
            a0 += a7 * 1e-12d;
            a2 += a5 * 1e-12d;
            a4 += a1 * 1e-12d;
            a6 += a3 * 1e-12d;
          }
        }
      }
      catch (OperationCanceledException)
      {
        // Expected during shutdown.
      }
    }
  }

  private sealed class BenchmarkEngine
  {
    private const double TargetSecondsSingle = 10.0;
    private const double TargetSecondsMulti = 10.0;  // was 6.0 — equal durations reduce variance
    private const int OperationsPerBatch = 1024;

    private const double SingleNorm = 1_456_000d;  // ~45–50 range on a modern mid-tier CPU
    private const double MultiNorm  =   145_600d;  // ~700–900 range on a modern mid-tier CPU

    public double RunSingleThread()
    {
      return ExecuteBenchmark(1, TargetSecondsSingle, SingleNorm);
    }

    public double RunMultiThread()
    {
      int threadCount = Math.Max(1, Environment.ProcessorCount);
      return ExecuteBenchmark(threadCount, TargetSecondsMulti, MultiNorm);
    }

    private static double ExecuteBenchmark(int threads, double durationSeconds, double normalization)
    {
      long start = Stopwatch.GetTimestamp();
      long durationTicks = (long)(durationSeconds * Stopwatch.Frequency);
      long targetEnd = start + Math.Max(durationTicks, Stopwatch.Frequency / 10);
      long globalIterations = 0;

      if (threads == 1)
      {
        // Single-threaded: avoid Parallel.For overhead
        double x = 1.0d;
        double y = 1.0d;

        while (true)
        {
          for (int i = 0; i < OperationsPerBatch; i++)
          {
            x = Math.Sqrt(x * 1.0000005d + 0.0000008d);
            y = Math.Cos(x) * Math.Sin(y) + 1.0000002d;
          }

          globalIterations += OperationsPerBatch;

          if (Stopwatch.GetTimestamp() >= targetEnd)
          {
            break;
          }
        }
      }
      else
      {
        // Multi-threaded: use Parallel.For
        var options = new ParallelOptions
        {
          MaxDegreeOfParallelism = threads
        };

        Parallel.For(0, threads, options, () => 0L, (index, state, localIterations) =>
        {
          double x = 1.0d + index * 0.15d;
          double y = 1.0d + index * 0.07d;

          while (true)
          {
            for (int i = 0; i < OperationsPerBatch; i++)
            {
              x = Math.Sqrt(x * 1.0000005d + 0.0000008d);
              y = Math.Cos(x) * Math.Sin(y) + 1.0000002d;
            }

            localIterations += OperationsPerBatch;

            if (Stopwatch.GetTimestamp() >= targetEnd)
            {
              break;
            }
          }

          return localIterations;
        }, localIterations => Interlocked.Add(ref globalIterations, localIterations));
      }

      double elapsedSeconds = Math.Max((Stopwatch.GetTimestamp() - start) / (double)Stopwatch.Frequency, 1e-5d);
      double operationsPerSecond = globalIterations / elapsedSeconds;

      double score = operationsPerSecond / normalization;

      return Math.Round(Math.Max(score, 0d), 1);
    }
  }

  private sealed class HardwareMetricsProvider
  {
    private readonly object sync = new();
    private readonly PerformanceCounter? cpuCounter;
    private readonly Lazy<int> baseClockMHz;

    // State for GetSystemTimes delta-based CPU calculation
    private long prevIdle;
    private long prevKernel;
    private long prevUser;
    private bool prevValid;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
      public uint Low;
      public uint High;
      public long ToInt64() => ((long)High << 32) | Low;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FILETIME lpIdle, out FILETIME lpKernel, out FILETIME lpUser);

    public HardwareMetricsProvider()
    {
      cpuCounter = TryCreateCounter();
      baseClockMHz = new Lazy<int>(ReadBaseClock, LazyThreadSafetyMode.ExecutionAndPublication);

      // Prime performance counter (first NextValue() always returns 0)
      if (cpuCounter != null)
      {
        try { cpuCounter.NextValue(); } catch { }
      }

      // Establish baseline for GetSystemTimes delta
      if (GetSystemTimes(out var i, out var k, out var u))
      {
        prevIdle = i.ToInt64();
        prevKernel = k.ToInt64();
        prevUser = u.ToInt64();
        prevValid = true;
      }
    }

    public bool TryGetMetrics(out HardwareMetrics metrics)
    {
      metrics = default;

      // GetSystemTimes: direct kernel32 call, no service dependencies, always works
      double cpuLoad = SampleCpuLoadSystemTimes();

      // Fallback: Windows performance counter
      if (double.IsNaN(cpuLoad))
        cpuLoad = SampleCpuLoad();

      // Last resort: WMI LoadPercentage
      if (double.IsNaN(cpuLoad))
        cpuLoad = SampleCpuLoadWmi();

      if (double.IsNaN(cpuLoad))
        return false;

      metrics.cpuLoad = Math.Clamp(cpuLoad, 0d, 100d);
      metrics.cpuFreqMHz = baseClockMHz.Value;
      metrics.tempC = double.NaN;
      metrics.voltage = double.NaN;
      metrics.packagePowerW = double.NaN;
      metrics.isValid = true;
      return true;
    }

    private double SampleCpuLoadSystemTimes()
    {
      if (!GetSystemTimes(out var idleFt, out var kernelFt, out var userFt))
        return double.NaN;

      long idle = idleFt.ToInt64();
      long kernel = kernelFt.ToInt64();
      long user = userFt.ToInt64();

      lock (sync)
      {
        if (!prevValid)
        {
          prevIdle = idle; prevKernel = kernel; prevUser = user;
          prevValid = true;
          return double.NaN;
        }

        long dIdle = idle - prevIdle;
        long dKernel = kernel - prevKernel;
        long dUser = user - prevUser;

        prevIdle = idle; prevKernel = kernel; prevUser = user;

        long total = dKernel + dUser;
        if (total <= 0) return 0d;
        return Math.Max(0d, (total - dIdle) / (double)total * 100d);
      }
    }

    private PerformanceCounter? TryCreateCounter()
    {
      try
      {
        return new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
      }
      catch
      {
        return null;
      }
    }

    private double SampleCpuLoad()
    {
      if (cpuCounter == null)
        return double.NaN;

      lock (sync)
      {
        try { return cpuCounter.NextValue(); }
        catch { return double.NaN; }
      }
    }

    private static double SampleCpuLoadWmi()
    {
      try
      {
        using var searcher = new ManagementObjectSearcher("select LoadPercentage from Win32_Processor");
        double sum = 0;
        int count = 0;
        foreach (var obj in searcher.Get().Cast<ManagementObject>())
        {
          if (obj["LoadPercentage"] != null && ushort.TryParse(obj["LoadPercentage"].ToString(), out var load))
          {
            sum += load;
            count++;
          }
        }
        return count > 0 ? sum / count : double.NaN;
      }
      catch
      {
        return double.NaN;
      }
    }

    private int ReadBaseClock()
    {
      try
      {
        using var searcher = new ManagementObjectSearcher("select MaxClockSpeed from Win32_Processor");
        var mhz = searcher.Get()
          .Cast<ManagementObject>()
          .Select(obj => obj["MaxClockSpeed"])
          .OfType<uint>()
          .Select(Convert.ToInt32)
          .DefaultIfEmpty(0)
          .Max();
        return Math.Max(mhz, 0);
      }
      catch
      {
        return 0;
      }
    }
  }

  private sealed class CpuInfoProvider
  {
    private CpuInfoSnapshot? cached;
    private DateTime lastRefreshUtc = DateTime.MinValue;
    private readonly TimeSpan cacheDuration = TimeSpan.FromMinutes(5);
    private readonly object sync = new();

    public bool TryRead(out CpuInfoSnapshot info)
    {
      lock (sync)
      {
        if (cached != null && DateTime.UtcNow - lastRefreshUtc < cacheDuration)
        {
          info = cached.Value;
          return true;
        }

        try
        {
          using var searcher = new ManagementObjectSearcher(
            "select Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed from Win32_Processor");

          string? name = null;
          int cores = 0;
          int logical = 0;
          int maxClock = 0;

          foreach (var obj in searcher.Get().Cast<ManagementObject>())
          {
            if (name == null)
            {
              name = obj["Name"]?.ToString();
            }

            if (obj["NumberOfCores"] != null && int.TryParse(obj["NumberOfCores"].ToString(), out var coreCount))
            {
              cores += Math.Max(coreCount, 0);
            }

            if (obj["NumberOfLogicalProcessors"] != null && int.TryParse(obj["NumberOfLogicalProcessors"].ToString(), out var logicalCount))
            {
              logical += Math.Max(logicalCount, 0);
            }

            if (obj["MaxClockSpeed"] != null && int.TryParse(obj["MaxClockSpeed"].ToString(), out var clock))
            {
              maxClock = Math.Max(maxClock, clock);
            }
          }

          cached = new CpuInfoSnapshot
          {
            Model = name ?? string.Empty,
            PhysicalCores = Math.Max(cores, 0),
            LogicalCores = Math.Max(logical, 0),
            MaxClockMHz = Math.Max(maxClock, 0)
          };

          lastRefreshUtc = DateTime.UtcNow;
          info = cached.Value;
          return true;
        }
        catch
        {
          info = default;
          cached = null;
          return false;
        }
      }
    }

  }
}
