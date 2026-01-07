# XenoCPUUtility
![Status](https://img.shields.io/badge/status-active-success)
![Version](https://img.shields.io/badge/version-1.8.0-blue)
![Official](https://img.shields.io/badge/Official-Original_Source-red)

> **Official Repository:** [https://github.com/nkthebass/XenoCPUUtility](https://github.com/nkthebass/XenoCPUUtility)  
> **Author:** nkthebass  

XenoCPUUtility is a high-precision **CPU benchmarking and stress testing utility** designed for hardware enthusiasts and overclockers. It provides deterministic scoring for single-core and multi-core performance across Intel, AMD, and ARM architectures.

---

### ⚠️ IMPORTANT: Authentication & Security
* **Beware of Imitations:** Third-party repositories (such as the MACHE-pool fork) may host outdated binaries, broken installers, or lack the critical security updates found in this official repo.
* **Security Verified:** As of v1.5.1, this utility **no longer uses** the vulnerable `WinRing0.sys` driver. Always download from this page to ensure you have the latest secure build.
* **Support:** Issues, score submissions, and feature requests are only handled here by the original author (**nkthebass**).

---

### ℹ️ System Requirements
* **OS:** Windows 10 or Windows 11
* **Memory:** 2GB minimum
* **Storage:** 200MB free space (Single portable EXE)
* **Compatibility:** Intel 2nd Gen+, AMD FX+, or modern ARM architectures.
* *Note: Legacy support for 1st Gen Intel and Phenom II is in development.*

---

### 🚀 Key Features
* **Deterministic Benchmarking:** Reliable, repeatable integer and floating-point workloads.
* **Thermal Intelligence:** 8-second heavy load "warmup" phase and enhanced delays to allow for heat dissipation between runs.
* **Precision Outlier Detection:** Automatically flags and highlights anomalous runs (thermal throttling, background interference).
* **Memory Stress Testing:** Robust RAM validation from 256MB to 128GB with error logging.
* **Modern UI:** Lightweight, no background services, featuring smooth gradients and real-time visualization.
* **Exportable Results:** Save and share your scores easily for comparison.

---

### 📊 Reference Scores (Official)

| CPU Model | Single-Core | Multi-Core |
| :--- | :--- | :--- |
| **Ryzen 9 9950X** | 72 | 1700 |
| **Ryzen 5 7600X** | 65 | 685 |
| **Core 5-210H** | 60 | 515 |
| **i7-5960X (4.3GHz)** | 43.5 | 530 |
| **i7-6700** | 39.5 | 230 |
| **i7-4770K** | 37 | 215 |
| **i5-7200U** | 31.5 | 105 |

---

### 🛠 Known Behaviors
* **Ryzen 9000:** We are currently investigating specific architecture-related quirks on certain AGESA versions.
* **Enterprise Security:** If Windows Defender or AppLocker flags the app, it is due to the nature of hardware register access. This utility is safe and contains no background services.

**Created by nkthebass.** If you find this tool useful, please leave a ⭐ on this repository to help others find the official source.
