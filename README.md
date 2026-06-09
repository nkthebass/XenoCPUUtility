![Status](https://img.shields.io/badge/status-active-success) ![Version](https://img.shields.io/badge/version-1.9.6-blue)

⚠️ **Security Notice**  
XenoCPUUtility does not distribute ZIP-based installers or binaries from third-party links. If you encounter downloads that do not function or behave unexpectedly, they are not official.

# XenoCPUUtility 
By nkthebass  
⭐ **Official Repository:** https://github.com/nkthebass/XenoCPUUtility
---

**BIG ANNOUNCEMENT**
I just released a legacy version compatible with window XP and newer CPU's from 1998-date should be compatible check it out here: https://github.com/nkthebass/XenoCPUUtility-legacy

This project does take use of "vibe coding" rest be assured it is far from being "AI slop". ---
---
Please contact me at nkthebass@gmail.com for any questions or issues and requests if any

## Overview
XenoCPUUtility is a **comprehensive CPU benchmarking and stress testing utility** designed to measure single-core and multi-core performance, thread scaling, floating-point throughput, integer workload behavior, and sustained CPU stability across modern processors.

It features **deterministic workloads**, real-time hardware monitoring, CPU instability detection (WHEA errors), and configurable stress testing modes for validation and overclocking verification. 

---

## System Requirements
* **OS:** Windows 10 or 11
* **RAM:** 2GB minimum
* **Storage:** 200MB free space
* **CPU:** Intel 2nd Gen or newer, AMD FX or newer (or ARM-compatible)

*Note: As of v1.9.0, legacy CPU support (Intel 1st Gen, AMD Phenom/Athlon K10) is not included. A legacy version is avalible on my profile.*

---

## Features

### 🎯 Benchmarking
- **Single-Core Benchmark:** Measures peak single-thread floating-point performance
- **Multi-Core Benchmark:** Aggregates all CPU threads for total throughput
- **Path Tracing Benchmark**  Measures path tracing performance with thread configurability 
- **Configurable Runs:** Average multiple benchmark runs for stability
- **CPU Comparison Charts:** Compare your score against 11+ reference CPUs (horizontally and vertically scrollable)
- **Histogram Distribution:** Visualize score variance across runs
  

### 💪 Stress Testing
- **Heavy Load Mode:** Continuous sqrt/sin/cos workload for sustained 95-100% CPU saturation
- **Instability Check Mode:** 4-phase rotating workload (FP-heavy → Integer → Memory pointer-chasing → Mixed bandwidth) with periodic idle dips and thread desynchronization to detect marginal CPU instabilities and voltage droops
- **Configurable Threads:** Manual or auto-detect (up to system core count)
- **Pause/Resume:** Temporarily suspend testing without losing progress
- **Execution Control:** Hidden background processes for non-intrusive testing

### ⚡ Hardware Monitoring & Diagnostics
- **WHEA Error Tab:** Real-time Windows Hardware Error Architecture (WHEA) event monitoring for CPU instability detection
- **Real-Time Metrics:** CPU utilization, RAM usage
- **Alerts Tab:** Event logging and system health notifications
- **CPU Info Panel:** Hardware details (model, max clock, core/thread count)

### ⚙️ UI & Controls
- **5 Sidebar Tabs:** 
  - ⚡ **WHEA** — CPU error monitoring
  - ⚠️ **Alerts** — System notifications
  - ⚙️ **Settings** — Configuration
  - 📊 **Benchmarks** — CPU comparison scores
  - ℹ️ **About** — Program info & features
- **Synchronized Tab Closing:** Tabs auto-close when another opens for clean workflow
- **Responsive Layout:** Optimized for 1920×1080 and higher resolutions

---

## Benchmarked CPUs

(note these scores may vary for some people please contact me at nkthebass@gmail.com if you see issues with provided scores)
### Single-Core Scores
* FX-4300: 17
* i5-7200U: 23
* N200: 23.5
* i7-4770K: 36.5
* i7-6700: 37
* Ryzen 5 3600: 38
* i7-5960X 4.3GHz: 43.5
* Core i5-210H: 56
* Ryzen 7 7700X: 58
* Ryzen 5 7600X: 58
* Ryzen 9 9950X: 65

### Multi-Core Scores
* i5-7200U: 105
* i7-4770K: 215
* i7-6700: 230
* Core i5-210H: 515
* i7-5960X 4.3GHz: 530
* Ryzen 5 7600X: 685
* Ryzen 9 9950X: 1700
* ryzen 5 3600: 400

---

## Architecture

### Benchmarking Engine
- **Synchronization:** ManualResetEventSlim start barriers ensure all threads begin simultaneously
- **Per-Thread Isolation:** Each thread maintains ~2MB private buffer to prevent cache contention
- **Workload:** Math-heavy FP operations (sqrt, sin, cos) + private buffer access + PCG-style LCG generator
- **Batching:** 1024 ops per timer check to minimize measurement overhead
- **Normalization:** Single-core (624k), Multi-core (91k) for consistent cross-platform scoring

### Stress Testing Engine
- **Heavy Load:** Continuous floating-point saturation (sqrt/sin/cos loop)
- **Instability Check:** Multi-phase workload with idle bursts and thread desynchronization for voltage droop detection

---

## Installation

### Download & Setup
1. Download `XenoCPUUtility.exe.zip`
2. unzip
3. Run `XenoCPUUtility.exe`

```

## Usage

### Benchmarking
1. Set number of runs (default: 3)
2. Click "Run Single Core Benchmark" or "Run Multi Core Benchmark"
3. View results in real-time with histogram
4. Compare against reference CPUs in **📊 Benchmarks tab**

### Stress Testing
1. Select thread count (auto-detect recommended)
2. Choose **Heavy Load** (sustained saturation) or **Instability Check** (marginal failure detection)
3. Monitor CPU utilization, temperature, and WHEA errors
4. Press **Pause Test** to suspend, **Stop All** to terminate

### Diagnostics
- **WHEA Tab (⚡):** Monitor CPU instability events during stress testing
- **Alerts Tab (⚠️):** Review system notifications
- **Settings Tab (⚙️):** Configure test parameters

---

## Important Notes

⚠️ **Third-Party Copies**  
Other repositories using the name "XenoCPUUtility" are **unofficial copies** and may:
- Contain broken installers
- Be outdated or unmaintained
- Lack official support

**Official builds and support are available only from this repository.**

⚠️ **Thermal/Power Safety**  
XenoCPUUtility respects system thermal and power limits. However:
- Ensure adequate cooling before stress testing
- Close background applications for accurate benchmarking
- Monitor temperatures during extended tests

---

## Contributing

If you've benchmarked your CPU, please share:
- Single-core and multi-core scores
- CPU model and specifications
- Screenshot of results

This helps improve the reference database for future versions.

---

## Official Repository
**GitHub:** https://github.com/nkthebass/XenoCPUUtility

Created by nkthebass














