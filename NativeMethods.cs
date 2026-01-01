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
      double value = 0.000001d + new Random(Guid.NewGuid().GetHashCode()).NextDouble();
      double increment = 0.0000001d;

      try
      {
        while (!token.IsCancellationRequested)
        {
          pauseEvent.Wait(token);

          // Math-heavy workload to keep the core busy.
          value = Math.Sqrt(value * 1.000001d + increment);
          value = Math.Sin(value) + 1.000001d;
          value = Math.Cos(value);

          if (value > 4.0d)
          {
            value -= 3.75d;
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
    private const double TargetSecondsSingle = 1.8;
    private const double TargetSecondsMulti = 0.8;
    private const int OperationsPerBatch = 1024;

    public double RunSingleThread()
    {
      return ExecuteBenchmark(1, TargetSecondsSingle, normalizeForSingle: true);
    }

    public double RunMultiThread()
    {
      int threadCount = Math.Max(1, Environment.ProcessorCount);
      return ExecuteBenchmark(threadCount, TargetSecondsMulti, normalizeForSingle: false);
    }

    private static double ExecuteBenchmark(int threads, double durationSeconds, bool normalizeForSingle)
    {
      long start = Stopwatch.GetTimestamp();
      long durationTicks = (long)(durationSeconds * Stopwatch.Frequency);
      long targetEnd = start + Math.Max(durationTicks, Stopwatch.Frequency / 10);
      long globalIterations = 0;

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

      double elapsedSeconds = Math.Max((Stopwatch.GetTimestamp() - start) / (double)Stopwatch.Frequency, 1e-5d);
      double operationsPerSecond = globalIterations / elapsedSeconds;

      double normalization = normalizeForSingle ? 624_000d : 115_000d;
      double score = operationsPerSecond / normalization;

      // Removed thread multiplier for more authentic multi-core score

      return Math.Round(Math.Max(score, 0d), 1);
    }
  }

  private sealed class HardwareMetricsProvider
  {
    private readonly object sync = new();
    private readonly PerformanceCounter? cpuCounter;
    private readonly Lazy<int> baseClockMHz;

    public HardwareMetricsProvider()
    {
      cpuCounter = TryCreateCounter();
      baseClockMHz = new Lazy<int>(ReadBaseClock, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public bool TryGetMetrics(out HardwareMetrics metrics)
    {
      metrics = default;

      double cpuLoad = SampleCpuLoad();
      if (double.IsNaN(cpuLoad))
      {
        return false;
      }

      metrics.cpuLoad = Math.Clamp(cpuLoad, 0d, 100d);
      metrics.cpuFreqMHz = baseClockMHz.Value;
      metrics.tempC = double.NaN;
      metrics.voltage = double.NaN;
      metrics.packagePowerW = double.NaN;
      metrics.isValid = true;
      return true;
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
      {
        return double.NaN;
      }

      lock (sync)
      {
        try
        {
          // First call often returns 0, so sample twice when possible.
          double value = cpuCounter.NextValue();
          Thread.Sleep(50);
          value = cpuCounter.NextValue();
          return value;
        }
        catch
        {
          return double.NaN;
        }
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
