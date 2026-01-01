#nullable disable
using System;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text;
using LibreHardwareMonitor.Hardware;
using System.Linq;
using System.Drawing;
using System.Collections.Generic;
using System.Threading;
using System.Diagnostics;
using System.Management;

namespace CPUUtilityHybrid
{
    public class Form1 : Form
    {
        private WebView2 webView;
        private string appDir = string.Empty;
        private bool isPaused = false;
        private Computer computer;
        private readonly List<byte[]> ramStressBuffers = new();
        private readonly object ramStressLock = new();
        private long ramAllocatedBytes;
        private bool cpuStressActive;
        private CancellationTokenSource? ramStressCts;
        private Task? ramStressTask;
        private bool ramStressActive;
        private const byte RamPatternA = 0xAA;
        private const byte RamPatternB = 0x55;

        public Form1()
        {
            Text = "Xeno CPU utility 1.6.3";
            Width = 820;
            Height = 760;
            
            // Set the form icon
            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_icon.ico");
                if (File.Exists(iconPath))
                {
                    Icon = new Icon(iconPath);
                }
            }
            catch { /* Ignore icon loading errors */ }
            
            webView = new WebView2 { Dock = DockStyle.Fill };
            Controls.Add(webView);
            Load += Form1_Load;
            FormClosing += Form1_FormClosing;
            
            // Initialize hardware monitor
            computer = new Computer
            {
                IsCpuEnabled = true,
                IsMotherboardEnabled = true
            };
            computer.Open();
        }

        private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
        {
            try
            {
                if (cpuStressActive)
                {
                    NativeMethods.StopStressTest();
                }
            }
            catch { }
            finally
            {
                cpuStressActive = false;
                ReleaseRamStress();
            }
            computer?.Close();
        }

        private async void Form1_Load(object? sender, EventArgs e)
        {
            appDir = AppDomain.CurrentDomain.BaseDirectory;
            var userDataFolder = Path.Combine(Path.GetTempPath(), "CPUUtilityHybrid", Guid.NewGuid().ToString());
            Directory.CreateDirectory(userDataFolder);

            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await webView.EnsureCoreWebView2Async(env);

            webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;

            var indexPath = Path.Combine(appDir, "www", "index.html");
            if (File.Exists(indexPath))
            {
                webView.CoreWebView2.Navigate(new Uri(indexPath).AbsoluteUri);
            }
            else
            {
                MessageBox.Show($"Could not find index.html at: {indexPath}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool TryAllocateRamStress(int megabytes, out string message)
        {
            ReleaseRamStress();

            if (megabytes <= 0)
            {
                message = "RAM stress disabled.";
                return true;
            }

            long totalMb = 0;
            try
            {
                var ci = new Microsoft.VisualBasic.Devices.ComputerInfo();
                totalMb = (long)ci.TotalPhysicalMemory / (1024 * 1024);
            }
            catch
            {
            }

            long targetMb = megabytes;
            bool clampedByLimit = false;
            if (totalMb > 0)
            {
                long reserve = Math.Max((long)(totalMb * 0.15), 2048L);
                long maxByTotal = Math.Max(totalMb - reserve, 0);
                if (targetMb > maxByTotal)
                {
                    targetMb = maxByTotal;
                    clampedByLimit = true;
                }
            }

            if (targetMb <= 0)
            {
                message = "RAM stress request exceeds safe limit; allocation skipped.";
                return false;
            }

            var buffers = new List<byte[]>();
            long totalBytes = 0;
            bool hitLimit = false;

            try
            {
                const int chunkMb = 64;
                long remaining = targetMb;
                while (remaining > 0)
                {
                    int currentChunk = (int)Math.Max(1, Math.Min(chunkMb, remaining));
                    var buffer = new byte[currentChunk * 1024 * 1024];
                    for (int i = 0; i < buffer.Length; i += 4096)
                    {
                        buffer[i] = 0xAA;
                    }
                    buffers.Add(buffer);
                    totalBytes += buffer.Length;
                    remaining -= currentChunk;
                }
            }
            catch (OutOfMemoryException ex)
            {
                if (totalBytes == 0)
                {
                    message = $"RAM allocation failed: {ex.Message}";
                    return false;
                }

                hitLimit = true;
            }
            catch (Exception ex)
            {
                message = $"RAM allocation error: {ex.Message}";
                return false;
            }

            lock (ramStressLock)
            {
                ramStressBuffers.Clear();
                ramStressBuffers.AddRange(buffers);
                ramAllocatedBytes = totalBytes;
            }

            try
            {
                var cts = new CancellationTokenSource();
                var loopTask = Task.Run(() => RunRamStressLoop(buffers, cts.Token), cts.Token);
                lock (ramStressLock)
                {
                    ramStressCts = cts;
                    ramStressTask = loopTask;
                    ramStressActive = true;
                }
            }
            catch (Exception ex)
            {
                ReleaseRamStress();
                message = $"Failed to start RAM stress verification: {ex.Message}";
                return false;
            }

            long allocatedMb = totalBytes / (1024 * 1024);
            message = (hitLimit || clampedByLimit)
                ? $"RAM stress allocated {allocatedMb} MB (limited for system safety)."
                : $"RAM stress allocated {allocatedMb} MB.";
            if (allocatedMb > 0)
            {
                message += " Running pattern verification.";
            }
            return true;
        }

        private void ReleaseRamStress()
        {
            CancellationTokenSource? cts;
            Task? task;

            lock (ramStressLock)
            {
                cts = ramStressCts;
                task = ramStressTask;
                ramStressCts = null;
                ramStressTask = null;
                ramStressActive = false;
            }

            if (cts != null)
            {
                try { cts.Cancel(); }
                catch { }
            }

            if (task != null)
            {
                try { task.Wait(TimeSpan.FromSeconds(5)); }
                catch (AggregateException ex) { Debug.WriteLine($"RAM stress task error: {ex.GetBaseException().Message}"); }
                catch (Exception ex) { Debug.WriteLine($"RAM stress task wait error: {ex.Message}"); }
            }

            cts?.Dispose();

            long releasedBytes = 0;
            lock (ramStressLock)
            {
                if (ramStressBuffers.Count > 0)
                {
                    ramStressBuffers.Clear();
                }
                releasedBytes = ramAllocatedBytes;
                ramAllocatedBytes = 0;
            }

            if (releasedBytes > 0)
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

                try
                {
                    using Process process = Process.GetCurrentProcess();
                    NativeMethods.SetProcessWorkingSetSize(process.Handle, -1, -1);
                }
                catch
                {
                }
            }
        }

        private int GetAllocatedRamMegabytes()
        {
            lock (ramStressLock)
            {
                return (int)(ramAllocatedBytes / (1024 * 1024));
            }
        }

        private bool IsRamStressRunning()
        {
            lock (ramStressLock)
            {
                return ramStressActive;
            }
        }

        private void RunRamStressLoop(List<byte[]> buffers, CancellationToken token)
        {
            int cycle = 0;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    cycle++;
                    if (!ExecuteRamPattern(buffers, RamPatternA, "A", cycle, token))
                        break;
                    if (token.IsCancellationRequested)
                        break;
                    if (!ExecuteRamPattern(buffers, RamPatternB, "B", cycle, token))
                        break;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RAM stress loop error: {ex.Message}");
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        Broadcast("ramStressError", new { message = ex.Message });
                    }));
                }
                catch
                {
                }
            }
            finally
            {
                lock (ramStressLock)
                {
                    ramStressActive = false;
                }
            }
        }

        private bool ExecuteRamPattern(List<byte[]> buffers, byte pattern, string patternName, int cycle, CancellationToken token)
        {
            var stopwatch = Stopwatch.StartNew();

            foreach (var buffer in buffers)
            {
                token.ThrowIfCancellationRequested();
                buffer.AsSpan().Fill(pattern);
            }

            int mismatches = 0;
            long firstOffset = -1;
            byte observed = 0;
            long offset = 0;

            foreach (var buffer in buffers)
            {
                token.ThrowIfCancellationRequested();
                var span = buffer.AsSpan();
                for (int i = 0; i < span.Length; i++)
                {
                    if (span[i] != pattern)
                    {
                        mismatches++;
                        if (firstOffset < 0)
                        {
                            firstOffset = offset + i;
                            observed = span[i];
                        }
                    }

                    if ((i & 0x3FFF) == 0 && token.IsCancellationRequested)
                    {
                        stopwatch.Stop();
                        return false;
                    }
                }
                offset += span.Length;
            }

            stopwatch.Stop();

            var firstOffsetValue = firstOffset >= 0 ? (long?)firstOffset : null;
            var observedValue = firstOffset >= 0 ? (int)observed : (int?)null;
            long totalBytes = offset;
            long processedBytes = totalBytes * 2;
            double elapsedSeconds = Math.Max(stopwatch.Elapsed.TotalSeconds, 1e-6);
            double throughputGbps = processedBytes / elapsedSeconds / (1024d * 1024d * 1024d);
            double iterationMs = stopwatch.Elapsed.TotalMilliseconds;

            try
            {
                BeginInvoke(new Action(() =>
                {
                    Broadcast("ramStressProgress", new
                    {
                        pattern = patternName,
                        iteration = cycle,
                        mismatches,
                        firstOffset = firstOffsetValue,
                        expected = (int)pattern,
                        observed = observedValue,
                        ramAllocatedMB = GetAllocatedRamMegabytes(),
                        durationMs = iterationMs,
                        throughputGBps = throughputGbps,
                        bytesTotal = totalBytes,
                        bytesProcessed = processedBytes
                    });
                }));
            }
            catch
            {
            }

            if (token.IsCancellationRequested)
                return false;

            Thread.Yield();
            return true;
        }

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var msg = JsonSerializer.Deserialize<JsonElement>(e.WebMessageAsJson);
                var id = msg.GetProperty("id").GetString() ?? "";
                var cmd = msg.GetProperty("cmd").GetString() ?? "";
                var args = msg.TryGetProperty("args", out var argsEl) ? argsEl : default;

                System.Diagnostics.Debug.WriteLine($"[Command] {cmd} (id: {id})");

                switch (cmd)
                {
                    case "startStress":
                        {
                            int threads = args.TryGetProperty("numProcesses", out var t) ? t.GetInt32() : 
                                         args.TryGetProperty("threads", out var t2) ? t2.GetInt32() : 4;
                            int ramMb = args.TryGetProperty("ramMB", out var r) ? r.GetInt32() : 0;
                            string ramMessage;
                            bool ramOk = TryAllocateRamStress(ramMb, out ramMessage);

                            if (threads <= 0 && (ramMb <= 0 || !ramOk))
                            {
                                ReleaseRamStress();
                                Reply(id, cmd, new { success = false, message = "CPU and RAM stress are disabled.", cpuEnabled = false, ramAllocatedMB = 0 });
                                break;
                            }

                            if (!ramOk)
                            {
                                Reply(id, cmd, new { success = false, message = ramMessage, cpuEnabled = threads > 0, ramAllocatedMB = GetAllocatedRamMegabytes() });
                                break;
                            }

                            if (threads <= 0)
                            {
                                cpuStressActive = false;
                                isPaused = false;
                                Reply(id, cmd, new
                                {
                                    success = true,
                                    message = $"CPU stress disabled. {ramMessage}",
                                    cpuEnabled = false,
                                    ramAllocatedMB = GetAllocatedRamMegabytes()
                                });
                                break;
                            }

                            try
                            {
                                bool success = NativeMethods.StartStressTest(threads);
                                cpuStressActive = success;
                                isPaused = false;
                                if (!success)
                                {
                                    ReleaseRamStress();
                                }
                                var messageText = success
                                    ? $"Stress test started. {ramMessage}"
                                    : (!string.IsNullOrWhiteSpace(NativeMethods.LastError)
                                        ? $"Failed to start CPU stress: {NativeMethods.LastError}"
                                        : "Failed to start CPU stress.");
                                Reply(id, cmd, new
                                {
                                    success,
                                    message = messageText,
                                    cpuEnabled = success,
                                    ramAllocatedMB = GetAllocatedRamMegabytes()
                                });
                            }
                            catch (Exception ex)
                            {
                                ReleaseRamStress();
                                Reply(id, cmd, new { success = false, message = ex.Message });
                            }
                        }
                        break;

                    case "stopStress":
                        {
                            try
                            {
                                bool success = true;
                                if (cpuStressActive)
                                {
                                    success = NativeMethods.StopStressTest();
                                }
                                cpuStressActive = false;
                                isPaused = false;
                                ReleaseRamStress();
                                var messageText = success
                                    ? "Stress test stopped. RAM stress cleared."
                                    : (!string.IsNullOrWhiteSpace(NativeMethods.LastError)
                                        ? $"Failed to stop CPU stress: {NativeMethods.LastError}"
                                        : "Failed to stop CPU stress.");
                                Reply(id, cmd, new
                                {
                                    success,
                                    message = messageText,
                                    ramAllocatedMB = GetAllocatedRamMegabytes()
                                });
                            }
                            catch (Exception ex)
                            {
                                cpuStressActive = false;
                                ReleaseRamStress();
                                Reply(id, cmd, new { success = false, message = ex.Message });
                            }
                        }
                        break;

                    case "togglePauseResume":
                    case "pauseStress":
                        {
                            if (!cpuStressActive)
                            {
                                Reply(id, cmd, new { success = false, message = "CPU stress is disabled.", isPaused });
                                break;
                            }
                            if (isPaused)
                            {
                                try
                                {
                                    bool success = NativeMethods.ResumeStressTest();
                                    if (success) isPaused = false;
                                    var messageText = success
                                        ? "Resumed"
                                        : (!string.IsNullOrWhiteSpace(NativeMethods.LastError)
                                            ? NativeMethods.LastError
                                            : "Failed to resume");
                                    Reply(id, cmd, new { success, message = messageText, isPaused });
                                }
                                catch (Exception ex)
                                {
                                    Reply(id, cmd, new { success = false, message = ex.Message, isPaused });
                                }
                            }
                            else
                            {
                                try
                                {
                                    bool success = NativeMethods.PauseStressTest();
                                    if (success) isPaused = true;
                                    var messageText = success
                                        ? "Paused"
                                        : (!string.IsNullOrWhiteSpace(NativeMethods.LastError)
                                            ? NativeMethods.LastError
                                            : "Failed to pause");
                                    Reply(id, cmd, new { success, message = messageText, isPaused });
                                }
                                catch (Exception ex)
                                {
                                    Reply(id, cmd, new { success = false, message = ex.Message, isPaused });
                                }
                            }
                        }
                        break;

                    case "resumeStress":
                        {
                            if (!cpuStressActive)
                            {
                                Reply(id, cmd, new { success = false, message = "CPU stress is disabled." });
                                break;
                            }
                            try
                            {
                                bool success = NativeMethods.ResumeStressTest();
                                if (success) isPaused = false;
                                var messageText = success
                                    ? "Resumed"
                                    : (!string.IsNullOrWhiteSpace(NativeMethods.LastError)
                                        ? NativeMethods.LastError
                                        : "Failed to resume");
                                Reply(id, cmd, new { success, message = messageText });
                            }
                            catch (Exception ex)
                            {
                                Reply(id, cmd, new { success = false, message = ex.Message });
                            }
                        }
                        break;

                    case "getStressStatus":
                        {
                            try
                            {
                                int count = 0;
                                if (cpuStressActive)
                                {
                                    count = NativeMethods.GetActiveThreadCount();
                                }
                                Reply(id, cmd, new
                                {
                                    running = count > 0 || GetAllocatedRamMegabytes() > 0,
                                    paused = isPaused,
                                    threadCount = count,
                                    ramAllocatedMB = GetAllocatedRamMegabytes()
                                });
                            }
                            catch (Exception ex)
                            {
                                Reply(id, cmd, new { running = false, paused = isPaused, threadCount = 0, error = ex.Message });
                            }
                        }
                        break;

                    case "getCpuInfo":
                    case "getCPUInfo":
                        {
                            string cpuName = string.Empty;
                            int cores = 0;
                            int threads = 0;
                            int maxMHz = 0;
                            bool success = false;

                            try
                            {
                                var modelName = new StringBuilder(256);
                                int nativeCores = 0;
                                int nativeThreads = 0;
                                int nativeMaxMHz = 0;
                                if (NativeMethods.GetCPUInfo(modelName, 256, ref nativeCores, ref nativeThreads, ref nativeMaxMHz))
                                {
                                    cpuName = modelName.ToString().Trim();
                                    cores = nativeCores;
                                    threads = nativeThreads;
                                    maxMHz = nativeMaxMHz;
                                    success = !string.IsNullOrWhiteSpace(cpuName);
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Native CPU info error: {ex.Message}");
                            }

                            if (!success || string.IsNullOrWhiteSpace(cpuName) || cores <= 0 || threads <= 0 || maxMHz <= 0)
                            {
                                try
                                {
                                    string? wmiName = null;
                                    int wmiCores = 0;
                                    int wmiThreads = 0;
                                    int wmiMaxMHz = 0;

                                    using var searcher = new ManagementObjectSearcher("select Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed from Win32_Processor");
                                    using var results = searcher.Get();
                                    foreach (var obj in results.Cast<ManagementObject>())
                                    {
                                        if (wmiName == null)
                                        {
                                            var nameValue = obj["Name"]?.ToString();
                                            if (!string.IsNullOrWhiteSpace(nameValue))
                                            {
                                                wmiName = nameValue.Trim();
                                            }
                                        }

                                        if (obj["NumberOfCores"] != null && int.TryParse(obj["NumberOfCores"].ToString(), out var coreCount))
                                        {
                                            wmiCores += Math.Max(coreCount, 0);
                                        }

                                        if (obj["NumberOfLogicalProcessors"] != null && int.TryParse(obj["NumberOfLogicalProcessors"].ToString(), out var logicalCount))
                                        {
                                            wmiThreads += Math.Max(logicalCount, 0);
                                        }

                                        if (obj["MaxClockSpeed"] != null && int.TryParse(obj["MaxClockSpeed"].ToString(), out var clock))
                                        {
                                            wmiMaxMHz = Math.Max(wmiMaxMHz, clock);
                                        }

                                        // Base clock should rely on MaxClockSpeed when present; do not override with turbo
                                    }

                                    if (!string.IsNullOrWhiteSpace(wmiName))
                                    {
                                        cpuName = wmiName;
                                    }
                                    if (wmiCores > 0)
                                    {
                                        cores = wmiCores;
                                    }
                                    if (wmiThreads > 0)
                                    {
                                        threads = wmiThreads;
                                    }
                                    if (wmiMaxMHz > 0)
                                    {
                                        maxMHz = wmiMaxMHz;
                                    }

                                    success = !string.IsNullOrWhiteSpace(cpuName);
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"WMI CPU info error: {ex.Message}");
                                }
                            }

                            if (threads <= 0)
                            {
                                threads = Environment.ProcessorCount;
                            }
                            if (cores <= 0)
                            {
                                cores = Math.Max(1, threads / 2);
                            }

                            var response = new
                            {
                                success = success || !string.IsNullOrWhiteSpace(cpuName),
                                name = string.IsNullOrWhiteSpace(cpuName) ? "Unknown CPU" : cpuName,
                                model = string.IsNullOrWhiteSpace(cpuName) ? "Unknown CPU" : cpuName,
                                cores,
                                threads,
                                logicalProcessors = threads,
                                maxClockMHz = maxMHz,
                                maxClockGHz = maxMHz > 0 ? maxMHz / 1000.0 : 0.0
                            };

                            Reply(id, cmd, response);
                        }
                        break;

                    case "getAppDir":
                        {
                            Reply(id, cmd, appDir);
                        }
                        break;

                    case "fileExists":
                        {
                            string path = args.GetProperty("path").GetString() ?? "";
                            bool exists = File.Exists(path);
                            Reply(id, cmd, exists);
                        }
                        break;

                    case "getHardwareSensors":
                        {
                            Task.Run(() =>
                            {
                                var sensors = new System.Collections.Generic.List<object>();
                                try
                                {
                                    foreach (var hardware in computer.Hardware)
                                    {
                                        hardware.Update();
                                        foreach (var sensor in hardware.Sensors)
                                        {
                                            sensors.Add(new
                                            {
                                                Hardware = hardware.Name,
                                                HardwareType = hardware.HardwareType.ToString(),
                                                Name = sensor.Name,
                                                Type = sensor.SensorType.ToString(),
                                                Value = sensor.Value
                                            });
                                        }
                                    }
                                }
                                catch { }
                                BeginInvoke(new Action(() => Reply(id, cmd, sensors)));
                            });
                        }
                        break;

                    case "runMultiCoreBenchmark":
                        {
                            Task.Run(() =>
                            {
                                double durationSeconds = 6.0;
                                int runCount = 1;
                                try
                                {
                                    if (args.ValueKind != JsonValueKind.Undefined && args.ValueKind != JsonValueKind.Null)
                                    {
                                        if (args.TryGetProperty("numRuns", out JsonElement runElement))
                                        {
                                            runCount = Math.Clamp(runElement.GetInt32(), 1, 100);
                                        }
                                        if (args.TryGetProperty("durationSeconds", out JsonElement durationElement))
                                        {
                                            double requested = durationElement.GetDouble();
                                            if (requested > 0.5 && requested <= 120.0)
                                            {
                                                durationSeconds = requested;
                                            }
                                        }
                                        else if (runCount > 1)
                                        {
                                            durationSeconds *= runCount;
                                        }
                                    }
                                }
                                catch
                                {
                                    // Keep default duration if parsing fails
                                }

                                var samples = new List<double>();
                                var stopwatch = Stopwatch.StartNew();
                                TimeSpan lastBroadcast = TimeSpan.Zero;
                                TimeSpan broadcastInterval = TimeSpan.FromMilliseconds(200);

                                try
                                {
                                    while (stopwatch.Elapsed.TotalSeconds < durationSeconds)
                                    {
                                        double sampleScore = NativeMethods.RunMultiCoreBenchmark();
                                        if (sampleScore > 0)
                                        {
                                            samples.Add(sampleScore);
                                        }

                                        var elapsed = stopwatch.Elapsed;
                                        if (elapsed - lastBroadcast >= broadcastInterval || elapsed.TotalSeconds >= durationSeconds)
                                        {
                                            double elapsedSeconds = Math.Min(elapsed.TotalSeconds, durationSeconds);
                                            int sampleCount = samples.Count;
                                            BeginInvoke(new Action(() =>
                                            {
                                                Broadcast("benchmarkProgress", new
                                                {
                                                    elapsedSeconds,
                                                    targetSeconds = durationSeconds,
                                                    sampleCount
                                                });
                                            }));
                                            lastBroadcast = elapsed;
                                        }
                                    }

                                    stopwatch.Stop();
                                    if (lastBroadcast < stopwatch.Elapsed)
                                    {
                                        double elapsedSeconds = Math.Min(stopwatch.Elapsed.TotalSeconds, durationSeconds);
                                        int sampleCount = samples.Count;
                                        BeginInvoke(new Action(() =>
                                        {
                                            Broadcast("benchmarkProgress", new
                                            {
                                                elapsedSeconds,
                                                targetSeconds = durationSeconds,
                                                sampleCount
                                            });
                                        }));
                                    }

                                    double averageScore = samples.Count > 0 ? samples.Average() : 0.0;
                                    double totalDuration = stopwatch.Elapsed.TotalSeconds;

                                    BeginInvoke(new Action(() =>
                                    {
                                        Reply(id, cmd, new
                                        {
                                            score = averageScore,
                                            success = averageScore > 0,
                                            samples = samples.Count,
                                            durationSeconds = totalDuration,
                                            runsRequested = runCount
                                        });
                                    }));
                                }
                                catch (Exception ex)
                                {
                                    stopwatch.Stop();
                                    BeginInvoke(new Action(() => Reply(id, cmd, new { success = false, message = ex.Message })));
                                }
                            });
                        }
                        break;

                    case "getHardwareMetrics":
                        {
                            Task.Run(() =>
                            {
                                // Get basic metrics from managed engine
                                var metrics = new NativeMethods.HardwareMetrics();
                                try
                                {
                                    if (!NativeMethods.GetHardwareMetrics(ref metrics))
                                    {
                                        var error = !string.IsNullOrWhiteSpace(NativeMethods.LastError)
                                            ? NativeMethods.LastError
                                            : "Unable to gather hardware metrics.";
                                        BeginInvoke(new Action(() => Reply(id, cmd, new { success = false, message = error })));
                                        return;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    BeginInvoke(new Action(() => Reply(id, cmd, new { success = false, message = ex.Message })));
                                    return;
                                }
                                
                                // Get additional metrics from LibreHardwareMonitor
                                double? tempC = null;
                                double? voltage = null;
                                double? power = null;
                                
                                try
                                {
                                    foreach (var hardware in computer.Hardware)
                                    {
                                        hardware.Update();
                                        
                                        if (hardware.HardwareType == HardwareType.Cpu)
                                        {
                                            foreach (var sensor in hardware.Sensors)
                                            {
                                                // Temperature: prefer Package, fallback to any CPU temp
                                                if (sensor.SensorType == SensorType.Temperature)
                                                {
                                                    if (sensor.Name.Contains("Package") || sensor.Name.Contains("CPU"))
                                                    {
                                                        if (!tempC.HasValue || sensor.Name.Contains("Package"))
                                                            tempC = sensor.Value;
                                                    }
                                                }
                                                // Voltage: take first available CPU voltage
                                                else if (sensor.SensorType == SensorType.Voltage && !voltage.HasValue)
                                                {
                                                    voltage = sensor.Value;
                                                    System.Diagnostics.Debug.WriteLine($"Found voltage sensor: {sensor.Name} = {sensor.Value}V");
                                                }
                                                // Power: prefer Package power
                                                else if (sensor.SensorType == SensorType.Power)
                                                {
                                                    if (sensor.Name.Contains("Package") || sensor.Name.Contains("CPU"))
                                                    {
                                                        if (!power.HasValue || sensor.Name.Contains("Package"))
                                                            power = sensor.Value;
                                                    }
                                                }
                                            }
                                        }
                                        // Also check motherboard for CPU voltage
                                        else if (hardware.HardwareType == HardwareType.Motherboard && !voltage.HasValue)
                                        {
                                            foreach (var sensor in hardware.Sensors)
                                            {
                                                if (sensor.SensorType == SensorType.Voltage && 
                                                    (sensor.Name.Contains("CPU") || sensor.Name.Contains("VCore") || sensor.Name.Contains("Vcore")))
                                                {
                                                    voltage = sensor.Value;
                                                    System.Diagnostics.Debug.WriteLine($"Found motherboard voltage sensor: {sensor.Name} = {sensor.Value}V");
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex) 
                                { 
                                    System.Diagnostics.Debug.WriteLine($"Error reading sensors: {ex.Message}");
                                }
                                
                                // Get RAM usage
                                var ramUsedGB = 0.0;
                                var ramTotalGB = 0.0;
                                var ramUsagePercent = 0.0;
                                try
                                {
                                    var ci = new Microsoft.VisualBasic.Devices.ComputerInfo();
                                    ramTotalGB = ci.TotalPhysicalMemory / (1024.0 * 1024.0 * 1024.0);
                                    var availableGB = ci.AvailablePhysicalMemory / (1024.0 * 1024.0 * 1024.0);
                                    ramUsedGB = ramTotalGB - availableGB;
                                    ramUsagePercent = (ramUsedGB / ramTotalGB) * 100.0;
                                }
                                catch { }
                                
                                var result = new
                                {
                                    CpuLoad = metrics.cpuLoad,
                                    CpuFreqMHz = metrics.cpuFreqMHz,
                                    TempC = tempC,
                                    Voltage = voltage,
                                    PackagePowerW = power,
                                    RamUsedGB = ramUsedGB,
                                    RamTotalGB = ramTotalGB,
                                    RamUsagePercent = ramUsagePercent,
                                    Timestamp = DateTime.UtcNow
                                };
                                
                                BeginInvoke(new Action(() => Reply(id, cmd, result)));
                            });
                        }
                        break;

                    default:
                        Reply(id, cmd, new { error = "Unknown command" });
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling message: {ex.Message}");
            }
        }

        private void Reply(string id, string command, object result)
        {
            try
            {
                var response = new { id, replyTo = command, result };
                var json = JsonSerializer.Serialize(response);
                System.Diagnostics.Debug.WriteLine($"[Reply] {command}: {json}");
                webView?.CoreWebView2?.PostWebMessageAsJson(json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Reply Error] {ex.Message}");
            }
        }

        private void Broadcast(string eventName, object data)
        {
            try
            {
                var message = new { broadcast = eventName, result = data };
                var json = JsonSerializer.Serialize(message);
                System.Diagnostics.Debug.WriteLine($"[Broadcast] {eventName}: {json}");
                webView?.CoreWebView2?.PostWebMessageAsJson(json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Broadcast Error] {ex.Message}");
            }
        }
    }
}
