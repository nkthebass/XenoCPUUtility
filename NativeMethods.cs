// Decompiled with JetBrains decompiler
// Type: CPUUtilityHybrid.NativeMethods
// Assembly: XenoCPUUtility, Version=1.5.0.0, Culture=neutral, PublicKeyToken=null
// MVID: 8EA8C267-943B-48AB-9688-875FFCA314A8
// Assembly location: C:\Program Files (x86)\Xeno CPU utility\XenoCPUUtility.dll

using System;
using System.Runtime.InteropServices;
using System.Text;

#nullable enable
namespace CPUUtilityHybrid;

public static class NativeMethods
{
  private const string DllName = "CPUUtilityNative.dll";

  [DllImport("CPUUtilityNative.dll", CallingConvention = CallingConvention.Cdecl)]
  public static extern bool StartStressTest(int threadCount);

  [DllImport("CPUUtilityNative.dll", CallingConvention = CallingConvention.Cdecl)]
  public static extern bool StopStressTest();

  [DllImport("CPUUtilityNative.dll", CallingConvention = CallingConvention.Cdecl)]
  public static extern bool PauseStressTest();

  [DllImport("CPUUtilityNative.dll", CallingConvention = CallingConvention.Cdecl)]
  public static extern bool ResumeStressTest();

  [DllImport("CPUUtilityNative.dll", CallingConvention = CallingConvention.Cdecl)]
  public static extern int GetActiveThreadCount();

  [DllImport("CPUUtilityNative.dll", CallingConvention = CallingConvention.Cdecl)]
  public static extern double RunSingleCoreBenchmark();

  [DllImport("CPUUtilityNative.dll", CallingConvention = CallingConvention.Cdecl)]
  public static extern double RunMultiCoreBenchmark();

  [DllImport("CPUUtilityNative.dll", CallingConvention = CallingConvention.Cdecl)]
  public static extern double RunMultiCoreBenchmarkWithProgress(
    NativeMethods.ProgressCallback callback,
    int numRuns);

  [DllImport("CPUUtilityNative.dll", CallingConvention = CallingConvention.Cdecl)]
  public static extern bool GetHardwareMetrics(ref NativeMethods.HardwareMetrics metrics);

  [DllImport("CPUUtilityNative.dll", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
  public static extern bool GetCPUInfo(
    StringBuilder modelName,
    int modelNameSize,
    ref int cores,
    ref int threads,
    ref int maxMHz);

  [DllImport("kernel32.dll")]
  public static extern bool SetProcessWorkingSetSize(IntPtr process, int minimumWorkingSetSize, int maximumWorkingSetSize);

  [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
  public delegate void ProgressCallback(int currentRun, int totalRuns);

  public struct HardwareMetrics
  {
    public double cpuLoad;
    public int cpuFreqMHz;
    public double tempC;
    public double voltage;
    public double packagePowerW;
    [MarshalAs(UnmanagedType.I1)]
    public bool isValid;
  }
}
