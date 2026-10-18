using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace ProjectorDash
{
    public sealed class ProcessUsage
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public long Started { get; set; }
        public long RamBytes { get; set; }
        public long PrivateBytes { get; set; }
        public long VirtualBytes { get; set; }
        public double? CpuPercent { get; set; }
        public bool Browser { get; set; }
        public bool ServiceHost { get; set; }
        public List<HostedService> Services = new List<HostedService>();
        public string DisplayName
        {
            get { return !ServiceHost || Services.Count == 0 ? Name : "Service host · " + Services[0].DisplayName +
                (Services.Count == 1 ? "" : " + " + (Services.Count - 1) + " more"); }
        }
        public string ServiceNames
        {
            get { return string.Join(", ", Services.ConvertAll(delegate(HostedService s) { return s.ToString(); }).ToArray()); }
        }
        public string Ram { get { return ResourceMonitor.Bytes((ulong)Math.Max(0, RamBytes)); } }
        public string PrivateMemory { get { return ResourceMonitor.Bytes((ulong)Math.Max(0, PrivateBytes)); } }
        public string Cpu { get { return CpuPercent.HasValue ? CpuPercent.Value.ToString("0.0") + "%" : "—"; } }
    }

    public sealed class ResourceSnapshot
    {
        public ulong RamTotal, RamAvailable, CommitUsed, CommitLimit, CommitPeak;
        public double? CpuPercent;
        public DateTime Captured;
        public int Unavailable;
        public string ServiceError = "";
        public List<ProcessUsage> Processes = new List<ProcessUsage>();
    }

    // Sampled off the UI thread, only while the Memory + CPU page is visible.
    // No performance-counter service, WMI query, elevated helper or dependency.
    public sealed class ResourceMonitor
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct PerformanceInfo
        {
            public uint Size;
            public UIntPtr CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable;
            public UIntPtr SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
            public uint HandleCount, ProcessCount, ThreadCount;
        }
        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool GetPerformanceInfo(ref PerformanceInfo info, int size);
        [DllImport("kernel32.dll")]
        private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);
        [DllImport("kernel32.dll")]
        private static extern bool IsProcessCritical(IntPtr process, out bool critical);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        private sealed class Baseline { public long Started, Cpu; }
        private Dictionary<int, Baseline> _previous = new Dictionary<int, Baseline>();
        private long _sampleTime, _idle, _total;
        private bool _systemBaseline;

        public static string Bytes(ulong bytes)
        {
            return bytes >= 1073741824 ? (bytes / 1073741824.0).ToString("0.00") + " GB"
                : (bytes / 1048576.0).ToString("0") + " MB";
        }

        public ResourceSnapshot Sample(string browserPath)
        {
            ResourceSnapshot sample = new ResourceSnapshot { Captured = DateTime.Now };
            PerformanceInfo memory = new PerformanceInfo();
            memory.Size = (uint)Marshal.SizeOf(typeof(PerformanceInfo));
            if (!GetPerformanceInfo(ref memory, (int)memory.Size))
                throw new InvalidOperationException("Windows couldn't read memory usage.");
            ulong page = memory.PageSize.ToUInt64();
            sample.RamTotal = memory.PhysicalTotal.ToUInt64() * page;
            sample.RamAvailable = memory.PhysicalAvailable.ToUInt64() * page;
            sample.CommitUsed = memory.CommitTotal.ToUInt64() * page;
            sample.CommitLimit = memory.CommitLimit.ToUInt64() * page;
            sample.CommitPeak = memory.CommitPeak.ToUInt64() * page;
            long idle, kernel, user;
            if (GetSystemTimes(out idle, out kernel, out user))
            {
                long total = kernel + user;
                if (_systemBaseline && total > _total)
                    sample.CpuPercent = Math.Max(0, Math.Min(100, 100.0 * (1 - (idle - _idle) / (double)(total - _total))));
                _idle = idle; _total = total; _systemBaseline = true;
            }
            long now = Stopwatch.GetTimestamp();
            double elapsed = _sampleTime == 0 ? 0 : (now - _sampleTime) / (double)Stopwatch.Frequency;
            Dictionary<int, Baseline> next = new Dictionary<int, Baseline>();
            Dictionary<string, bool> browserMatches = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            Dictionary<int, List<HostedService>> services = ServiceCatalog.Read(out sample.ServiceError);
            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        if (process.Id == 0) continue; // Idle time is not an application's CPU use.
                        ProcessUsage usage = new ProcessUsage { Id = process.Id, Name = process.ProcessName,
                            RamBytes = process.WorkingSet64, PrivateBytes = process.PrivateMemorySize64,
                            VirtualBytes = process.VirtualMemorySize64 };
                        try
                        {
                            usage.Started = process.StartTime.ToUniversalTime().Ticks;
                            long cpu = process.TotalProcessorTime.Ticks;
                            Baseline old;
                            if (elapsed > 0 && _previous.TryGetValue(usage.Id, out old) && old.Started == usage.Started && cpu >= old.Cpu)
                                usage.CpuPercent = Math.Max(0, Math.Min(100,
                                    (cpu - old.Cpu) / 10000000.0 / elapsed / Environment.ProcessorCount * 100));
                            next[usage.Id] = new Baseline { Started = usage.Started, Cpu = cpu };
                        }
                        catch { /* Memory remains useful when CPU access is denied. */ }
                        string path = ScreenUtil.ProcessPath((uint)usage.Id);
                        bool browser = false;
                        if (path.Length > 0 && !browserMatches.TryGetValue(path, out browser))
                        {
                            browser = string.Equals(path, browserPath, StringComparison.OrdinalIgnoreCase) || ScreenUtil.IsSupermiumExecutable(path);
                            browserMatches[path] = browser;
                        }
                        usage.Browser = browser;
                        usage.ServiceHost = string.Equals(usage.Name, "svchost", StringComparison.OrdinalIgnoreCase);
                        List<HostedService> hosted;
                        if (services.TryGetValue(usage.Id, out hosted)) usage.Services = hosted;
                        if (browser) usage.Name = "Supermium · " + usage.Name;
                        sample.Processes.Add(usage);
                    }
                    catch { sample.Unavailable++; } // Exited/protected processes never abort a sample.
                }
            }
            _sampleTime = now; _previous = next;
            return sample;
        }

        public static bool CanEndTask(ProcessUsage item)
        {
            if (item == null || item.Started == 0 || item.Id <= 4) return false;
            using (Process current = Process.GetCurrentProcess())
            {
                if (item.Id == current.Id) return false;
                try
                {
                    using (Process process = Process.GetProcessById(item.Id))
                    {
                        if (process.StartTime.ToUniversalTime().Ticks != item.Started || process.SessionId != current.SessionId) return false;
                    }
                    string path = ScreenUtil.ProcessPath((uint)item.Id);
                    string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";
                    if (path.Length == 0 || path.StartsWith(windows, StringComparison.OrdinalIgnoreCase)) return false;
                    IntPtr handle = OpenProcess(0x1000, false, item.Id);
                    if (handle == IntPtr.Zero) return false;
                    try
                    {
                        bool critical;
                        return IsProcessCritical(handle, out critical) && !critical;
                    }
                    finally { CloseHandle(handle); }
                }
                catch { return false; }
            }
        }

        public static void EndTask(ProcessUsage item)
        {
            if (!CanEndTask(item)) throw new InvalidOperationException("This process has exited or cannot be ended here.");
            using (Process process = Process.GetProcessById(item.Id))
            {
                // Keep a handle to this instance; a recycled PID must never
                // turn a stale selection into a different application's kill.
                IntPtr handle = process.Handle;
                if (process.StartTime.ToUniversalTime().Ticks != item.Started)
                    throw new InvalidOperationException("The selected process has already exited.");
                process.Kill();
            }
        }
    }
}
