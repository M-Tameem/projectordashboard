using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using ProjectorDash;

internal static class ResourceTests
{
    private static int passed;
    private static void Check(bool ok, string message)
    { if (!ok) throw new Exception(message); passed++; Console.WriteLine("PASS: " + message); }
    public static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            byte[] memory = new byte[64 * 1024 * 1024];
            for (int i = 0; i < memory.Length; i += 4096) memory[i] = 1;
            while (true) { Thread.Sleep(100); GC.KeepAlive(memory); }
        }
        ResourceMonitor monitor = new ResourceMonitor();
        ResourceSnapshot first = monitor.Sample("");
        Check(first.ServiceError.Length == 0, "Service names are read directly from Windows without WMI");
        ProcessUsage serviceHost = first.Processes.Find(delegate(ProcessUsage p) { return p.ServiceHost && p.Services.Count > 0; });
        Check(serviceHost != null, "Running Windows service names are matched to a service-host PID");
        Check(serviceHost.DisplayName.Contains(serviceHost.Services[0].DisplayName) &&
            serviceHost.ServiceNames.Contains(serviceHost.Services[0].Name), "Service host shows friendly and technical service names");
        Check(!ResourceMonitor.CanEndTask(serviceHost), "Service discovery does not enable ending protected hosts");
        int self = Process.GetCurrentProcess().Id;
        Check(first.RamTotal > 0 && first.RamAvailable <= first.RamTotal, "Native physical RAM totals are valid");
        Check(first.CommitLimit > 0 && first.CommitUsed <= first.CommitLimit && first.CommitPeak >= first.CommitUsed,
            "System commit used, limit and peak are reported");
        Check(!first.CpuPercent.HasValue, "Initial CPU reading is unknown instead of a misleading zero");
        ProcessUsage own = first.Processes.Find(delegate(ProcessUsage p) { return p.Id == self; });
        Check(own != null && own.RamBytes > 0 && own.PrivateBytes > 0, "Current process memory is visible");
        Check(!ResourceMonitor.CanEndTask(own), "Task controls protect the dashboard's own process");
        bool denied = false;
        try { ResourceMonitor.EndTask(own); } catch (InvalidOperationException) { denied = true; }
        Check(denied, "Ending the dashboard is rejected at execution, not just in the UI");
        Stopwatch busy = Stopwatch.StartNew();
        while (busy.ElapsedMilliseconds < 300) { Thread.SpinWait(1000); }
        ResourceSnapshot second = monitor.Sample("");
        own = second.Processes.Find(delegate(ProcessUsage p) { return p.Id == self; });
        Check(second.CpuPercent.HasValue && second.CpuPercent.Value >= 0 && second.CpuPercent.Value <= 100,
            "System CPU uses bounded interval measurements");
        Check(own.CpuPercent.HasValue && own.CpuPercent.Value > 0 && own.CpuPercent.Value <= 100,
            "Process CPU detects work and is normalized to all cores");
        string exe = Assembly.GetExecutingAssembly().Location;
        using (Process fixture = Process.Start(new ProcessStartInfo(exe, "fixture") { UseShellExecute = false }))
        {
            try
            {
                for (int i = 0; i < 50; i++)
                { fixture.Refresh(); if (fixture.PrivateMemorySize64 >= 64 * 1024 * 1024) break; Thread.Sleep(100); }
                ResourceSnapshot sample = monitor.Sample(exe);
                ProcessUsage item = sample.Processes.Find(delegate(ProcessUsage p) { return p.Id == fixture.Id; });
                Check(item != null && item.PrivateBytes >= 64 * 1024 * 1024, "Separate process allocation appears in private memory");
                Check(item.Browser, "Configured browser subprocesses are included in the browser total");
                Check(ResourceMonitor.CanEndTask(item), "Ordinary user application can be ended");
                ProcessUsage stale = new ProcessUsage { Id = item.Id, Started = item.Started - 1 };
                Check(!ResourceMonitor.CanEndTask(stale), "Stale process identity cannot target a reused PID");
                denied = false;
                try { ResourceMonitor.EndTask(stale); } catch (InvalidOperationException) { denied = true; }
                Check(denied && !fixture.HasExited, "Stale end-task request leaves the actual process running");
                ResourceMonitor.EndTask(item);
                Check(fixture.WaitForExit(5000), "End task terminates only the selected disposable test process");
                Check(!ResourceMonitor.CanEndTask(item), "Exited process is no longer actionable");
            }
            finally { if (!fixture.HasExited) fixture.Kill(); }
        }
        Console.WriteLine("Resource monitoring: " + passed + " checks passed");
        return 0;
    }
}
