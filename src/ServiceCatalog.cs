using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ProjectorDash
{
    public sealed class HostedService
    {
        public string Name;
        public string DisplayName;
        public override string ToString() { return DisplayName + " (" + Name + ")"; }
    }

    // Read-only SCM enumeration: no WMI provider, tasklist process, admin prompt
    // or changes to service configuration. Called only by the visible monitor.
    public static class ServiceCatalog
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct ServiceStatus
        {
            public uint Type, State, Controls, Win32Exit, ServiceExit, Checkpoint, WaitHint, ProcessId, Flags;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct ServiceEntry
        {
            public IntPtr Name, DisplayName;
            public ServiceStatus Status;
        }
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenSCManager(string machine, string database, uint access);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool EnumServicesStatusEx(IntPtr manager, int level, uint type, uint state,
            IntPtr buffer, uint size, out uint needed, out uint returned, ref uint resume, string group);
        [DllImport("advapi32.dll")]
        private static extern bool CloseServiceHandle(IntPtr handle);

        public static Dictionary<int, List<HostedService>> Read(out string error)
        {
            error = "";
            Dictionary<int, List<HostedService>> services = new Dictionary<int, List<HostedService>>();
            IntPtr manager = OpenSCManager(null, null, 4); // SC_MANAGER_ENUMERATE_SERVICE
            if (manager == IntPtr.Zero) { error = new Win32Exception(Marshal.GetLastWin32Error()).Message; return services; }
            IntPtr buffer = IntPtr.Zero;
            try
            {
                const int capacity = 256 * 1024;
                buffer = Marshal.AllocHGlobal(capacity);
                uint resume = 0;
                int entrySize = Marshal.SizeOf(typeof(ServiceEntry));
                for (int page = 0; page < 32; page++)
                {
                    uint needed, count;
                    bool complete = EnumServicesStatusEx(manager, 0, 0x30, 3, buffer, capacity, out needed, out count, ref resume, null);
                    int code = complete ? 0 : Marshal.GetLastWin32Error();
                    if (!complete && code != 234) { error = new Win32Exception(code).Message; break; }
                    for (uint i = 0; i < count && (i + 1) * entrySize <= capacity; i++)
                    {
                        ServiceEntry entry = (ServiceEntry)Marshal.PtrToStructure(IntPtr.Add(buffer, (int)i * entrySize), typeof(ServiceEntry));
                        if (entry.Status.ProcessId == 0 || (entry.Status.State != 4 && entry.Status.State != 7)) continue;
                        int pid = (int)entry.Status.ProcessId;
                        List<HostedService> hosted;
                        if (!services.TryGetValue(pid, out hosted)) services[pid] = hosted = new List<HostedService>();
                        hosted.Add(new HostedService { Name = Marshal.PtrToStringUni(entry.Name) ?? "",
                            DisplayName = Marshal.PtrToStringUni(entry.DisplayName) ?? "" });
                    }
                    if (complete) break;
                    if (resume == 0 || page == 31) { error = "Windows returned an incomplete service list."; break; }
                }
                foreach (List<HostedService> hosted in services.Values)
                    hosted.Sort(delegate(HostedService a, HostedService b) { return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase); });
                return services;
            }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                CloseServiceHandle(manager);
            }
        }
    }
}
