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

  public static bool StartStressTest(int threadCount, string mode = "heavy")
  {
    if (threadCount <= 0)
    {
      LastError = "Thread count must be greater than zero.";
      return false;
    }

    var result = Stress.Start(threadCount, mode);
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
    private string stressMode = "heavy"; // "heavy" or "instability"

    public bool Start(int threadCount, string mode = "heavy")
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
        stressMode = mode;
        workers.Clear();

        for (int i = 0; i < threadCount; i++)
        {
          int threadId = i;
          workers.Add(Task.Run(() => WorkerLoop(threadId, cts.Token), cts.Token));
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

    private void WorkerLoop(int threadId, CancellationToken token)
    {
      if (stressMode == "instability")
        WorkerLoopInstability(threadId, token);
      else
        WorkerLoopHeavy(token);
    }

    private void WorkerLoopHeavy(CancellationToken token)
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

    private void WorkerLoopInstability(int threadId, CancellationToken token)
    {
      try
      {
        double f = threadId + 1.0d;
        ulong x = (ulong)(threadId + 1);
        uint i = (uint)(threadId + 1);
        long phase = 0;

        // Create hostile memory patterns
        uint[] bufferU32 = new uint[65536];
        double[] bufferDouble = new double[32768];
        for (int k = 0; k < bufferU32.Length; k++)
          bufferU32[k] = (uint)(k ^ 0xDEADBEEF);
        for (int k = 0; k < bufferDouble.Length; k++)
          bufferDouble[k] = Math.Sin(k * 0.001);

        while (!token.IsCancellationRequested)
        {
          pauseEvent.Wait(token);

          // Phase 1: FP-heavy burst (exposes AVX voltage domains)
          if ((phase & 3) == 0)
          {
            for (int k = 0; k < 256; k++)
            {
              f = Math.Sqrt(Math.Abs(f * 0.999999d + 0.000001d));
              f = Math.Sin(f) * Math.Cos(f);
              f = Math.Tanh(f);
            }
          }
          // Phase 2: Integer-heavy phase
          else if ((phase & 3) == 1)
          {
            for (int k = 0; k < 256; k++)
            {
              i = (i * 1103515245u) + 12345u;
              i ^= (i << 13);
              i ^= (i >> 17);
              i ^= (i << 5);
            }
          }
          // Phase 3: Hostile memory access (pointer chasing, eviction stress)
          else if ((phase & 3) == 2)
          {
            int idx = (int)(x % (ulong)bufferU32.Length);
            for (int k = 0; k < 128; k++)
            {
              bufferU32[idx] ^= (uint)x;
              x = ((x * 6364136223846793005UL) ^ (x >> 33)) * 1442695040888963407UL;
              idx = (int)(bufferU32[idx] % (uint)bufferU32.Length);
            }
          }
          // Phase 4: Mixed + memory bandwidth
          else
          {
            for (int k = 0; k < bufferDouble.Length; k += 16)
            {
              bufferDouble[k] += f;
              bufferDouble[(k + 1) % bufferDouble.Length] *= 1.0001;
              f = Math.Sqrt(bufferDouble[k] * bufferDouble[k] + 1.0);
              i = (i * 1103515245u) + 12345u;
            }
          }

          phase++;

          // Occasional idle burst (forces clock ramp transitions)
          if ((phase & 0x3FFF) == 0)
          {
            Thread.Sleep(50); // More visible dip
          }

          // Thread desynchronization (creates power noise)
          if ((x & 0xFF) == (ulong)threadId)
          {
            Thread.SpinWait(2000);
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
    private const double NormalizationFactorSingle = 624_000d;
    private const double NormalizationFactorMulti = 91_000d;  // Calibrated to match original benchmark scoring
    private const int BatchSize = 1024;

    public double RunSingleThread()
    {
      return ExecuteBenchmark(1, TargetSecondsSingle, NormalizationFactorSingle);
    }

    public double RunMultiThread()
    {
      int threadCount = Math.Max(1, Environment.ProcessorCount);
      return ExecuteBenchmark(threadCount, TargetSecondsMulti, NormalizationFactorMulti);
    }

    private static double ExecuteBenchmark(int threadCount, double durationSeconds, double normalizationFactor)
    {
      long[] results = new long[threadCount];
      
      // Calculate timing before thread creation (but don't start timer yet)
      long durationTicks = (long)(durationSeconds * Stopwatch.Frequency);
      long minDurationTicks = Math.Max(durationTicks, Stopwatch.Frequency / 10);

      // Create a start barrier to ensure all threads begin simultaneously
      var startGate = new ManualResetEventSlim(false);
      long startTicks = 0;
      long targetEndTicks = 0;

      Thread[] threads = new Thread[threadCount];

      for (int t = 0; t < threadCount; t++)
      {
        int tid = t;
        threads[t] = new Thread(() =>
        {
          // Wait for signal before starting benchmark
          startGate.Wait();

          // Per-thread private state — completely independent
          double f = tid + 1.0d;
          ulong x = (ulong)(tid + 1);
          long ops = 0;

          // Private working set (~2MB per thread) to prevent cache contention
          double[] buffer = new double[256 * 1024];
          for (int i = 0; i < buffer.Length; i++)
            buffer[i] = f;

          while (true)
          {
            // Process batch of operations
            for (int b = 0; b < BatchSize; b++)
            {
              // Math-heavy independent work
              f = Math.Sqrt(f * 0.999999d + 0.000001d);
              f = Math.Sin(f);
              f = Math.Cos(f);

              // Access private buffer
              int idx = (int)(x % (ulong)buffer.Length);
              buffer[idx] = f;

              // PCG-style LCG for next iteration
              x = (x * 2862933555777941757UL) + 3037000493UL;
            }

            ops += BatchSize;

            // Check timer only once per batch (not per operation)
            if (Stopwatch.GetTimestamp() >= targetEndTicks)
              break;
          }

          results[tid] = ops;
        })
        {
          IsBackground = true,
          Priority = ThreadPriority.Highest
        };

        threads[t].Start();
      }

      // All threads are now created and waiting. Start the timer NOW.
      startTicks = Stopwatch.GetTimestamp();
      targetEndTicks = startTicks + minDurationTicks;

      // Release all threads simultaneously
      startGate.Set();

      // Wait for all threads to complete
      foreach (var t in threads)
        t.Join();

      // Sum all thread results (critical: don't average)
      long totalOps = 0;
      for (int i = 0; i < results.Length; i++)
      {
        System.Diagnostics.Debug.WriteLine($"Thread {i}: {results[i]} ops");
        totalOps += results[i];
      }
      System.Diagnostics.Debug.WriteLine($"TOTAL OPS: {totalOps}");

      double elapsedSeconds = Math.Max((Stopwatch.GetTimestamp() - startTicks) / (double)Stopwatch.Frequency, 1e-5d);
      double operationsPerSecond = totalOps / elapsedSeconds;
      double score = operationsPerSecond / normalizationFactor;
      System.Diagnostics.Debug.WriteLine($"Score: {score} (ops/sec: {operationsPerSecond}, elapsed: {elapsedSeconds}s)");

      startGate.Dispose();
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
