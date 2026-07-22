using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace ProjectorDash
{
    public class ShortcutItem
    {
        public string Name;
        public string Target;   // http(s) URL, .exe path, document, or anything ShellExecute understands
        public string Args;     // optional command-line arguments

        // Web-shortcut launch options (applied when opening in Supermium).
        // Each GPU workaround is independent and off by default because sites
        // and drivers can benefit from different combinations.
        public bool GpuDisableDirectComposition;
        public bool GpuUseD3d9;
        public bool GpuDisableVsync;

        // Legacy setting from older builds. Load() expands a true value into
        // all three individual switches, then clears this field on the next
        // save so an existing user's choice is preserved once.
        public bool GpuCompat;
        public bool Incognito;
        // Discreet shortcuts show only their chosen alias on the dashboard;
        // their real address remains editable inside Settings.
        public bool HideTarget;

        public ShortcutItem() { }

        public ShortcutItem(string name, string target)
        {
            Name = name;
            Target = target;
            Args = "";
        }

        public bool IsWeb()
        {
            if (string.IsNullOrEmpty(Target)) return false;
            string t = Target.Trim().ToLowerInvariant();
            return t.StartsWith("http://") || t.StartsWith("https://");
        }
    }

    public class AppConfig
    {
        // Which physical display device is which (System.Windows.Forms.Screen.DeviceName)
        public string TabletDevice = "";
        public string ProjectorDevice = "";

        // Launch and control everything on the tablet itself. The saved
        // projector assignment is deliberately retained for instant switching
        // back to the normal two-screen setup.
        public bool TabletOnlyMode = false;

        // Area in the bottom-right of the tablet kept free for TouchMousePointer, in pixels.
        public int ReservedWidth = 420;
        public int ReservedHeight = 340;

        // "clock" or "blank"
        public string ProjectorMode = "clock";

        // One-time manual location for weather and the overhead display. We
        // deliberately do not use Windows Location Services: it is unreliable
        // on the target Windows 8.1 tablet and would add another permission/
        // hardware dependency. FacingDegrees is true-north clockwise and lets
        // bearings also be described relative to the view from bed.
        public bool LocationConfigured = false;
        public string LocationName = "";
        public double Latitude = 0.0;
        public double Longitude = 0.0;
        public int FacingDegrees = 0;
        public bool UseFahrenheit = false;

        // How launched shortcuts occupy the projector. Fullscreen removes the
        // normal window frame; false keeps the frame and maximizes the window.
        // Field initializer intentionally migrates older config files to the
        // new, projector-first behaviour.
        public bool LaunchFullscreen = true;

        // Lightweight daily alarm (24-hour clock). Disabled by default.
        public bool AlarmEnabled = false;
        public int AlarmHour = 8;
        public int AlarmMinute = 0;

        // Persistent render-endpoint choices. Windows itself remembers each
        // endpoint's independent volume and mute state.
        public string AudioDeviceId = "";
        public string AlarmAudioDeviceId = "";

        // Daily Windows workstation lock (same effect as Win+L).
        public bool AutoLockEnabled = false;
        public int AutoLockHour = 1;
        public int AutoLockMinute = 0;

        // Browser used for web shortcuts (Supermium). Auto-detected on first
        // run and editable in Settings. Website launches require this path so
        // monitor targeting and fullscreen behaviour stay deterministic.
        public string BrowserPath = "";

        // Probes the usual Supermium install locations.
        public static string DetectSupermium()
        {
            string[] roots = new string[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData), "Programs")
            };
            foreach (string root in roots)
            {
                if (string.IsNullOrEmpty(root)) continue;
                string p1 = Path.Combine(root, "Supermium", "chrome.exe");
                if (File.Exists(p1)) return p1;
                string p2 = Path.Combine(root, "Supermium", "supermium.exe");
                if (File.Exists(p2)) return p2;
            }
            return "";
        }

        public List<ShortcutItem> Shortcuts = new List<ShortcutItem>();

        public static string ConfigDir()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ProjectorDashboard");
        }

        private static string ConfigPath()
        {
            return Path.Combine(ConfigDir(), "config.xml");
        }

        public bool DisplaysConfigured()
        {
            if (string.IsNullOrEmpty(TabletDevice)) return false;
            if (!ScreenUtil.DeviceExists(TabletDevice)) return false;
            if (TabletOnlyMode) return true;
            // Projector may legitimately be absent (single-screen fallback); only
            // insist that it exists when one was configured and more than one
            // screen is attached right now.
            if (!string.IsNullOrEmpty(ProjectorDevice)
                && ScreenUtil.ScreenCount() > 1
                && !ScreenUtil.DeviceExists(ProjectorDevice)) return false;
            return true;
        }

        public static AppConfig Load()
        {
            try
            {
                string path = ConfigPath();
                if (File.Exists(path))
                {
                    XmlSerializer ser = new XmlSerializer(typeof(AppConfig));
                    AppConfig cfg;
                    using (FileStream fs = File.OpenRead(path))
                    {
                        cfg = (AppConfig)ser.Deserialize(fs);
                    }
                    bool changed = false;
                    if (cfg.Shortcuts == null || cfg.Shortcuts.Count == 0)
                    {
                        cfg.Shortcuts = DefaultShortcuts();
                        changed = true;
                    }
                    changed |= RemoveEmptyShortcuts(cfg.Shortcuts);
                    changed |= MigrateGpuCompat(cfg.Shortcuts);
                    changed |= EnsureBuiltInShortcuts(cfg.Shortcuts);
                    // One-time migration from the original touch-pad default.
                    if (cfg.ReservedWidth == 480 && cfg.ReservedHeight == 290)
                    {
                        cfg.ReservedWidth = 420;
                        cfg.ReservedHeight = 340;
                        changed = true;
                    }
                    cfg.ClampReserved();
                    cfg.ClampAlarm();
                    cfg.ClampAutoLock();
                    cfg.ClampLocation();
                    if (string.IsNullOrEmpty(cfg.BrowserPath))
                    {
                        cfg.BrowserPath = DetectSupermium();
                        if (!string.IsNullOrEmpty(cfg.BrowserPath)) changed = true;
                    }
                    if (changed) cfg.Save();
                    return cfg;
                }
            }
            catch
            {
                // fall through to defaults; a broken config must never brick the appliance
            }

            AppConfig fresh = new AppConfig();
            fresh.Shortcuts = DefaultShortcuts();
            fresh.BrowserPath = DetectSupermium();
            return fresh;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir());
                ClampReserved();
                ClampAlarm();
                ClampAutoLock();
                ClampLocation();
                XmlSerializer ser = new XmlSerializer(typeof(AppConfig));
                using (FileStream fs = File.Create(ConfigPath()))
                {
                    ser.Serialize(fs, this);
                }
            }
            catch
            {
                // never crash on save
            }
        }

        private void ClampReserved()
        {
            if (ReservedWidth < 200) ReservedWidth = 200;
            if (ReservedWidth > 900) ReservedWidth = 900;
            if (ReservedHeight < 160) ReservedHeight = 160;
            if (ReservedHeight > 500) ReservedHeight = 500;
        }

        private void ClampAlarm()
        {
            if (AlarmHour < 0) AlarmHour = 0;
            if (AlarmHour > 23) AlarmHour = 23;
            if (AlarmMinute < 0) AlarmMinute = 0;
            if (AlarmMinute > 59) AlarmMinute = 59;
        }

        private void ClampAutoLock()
        {
            if (AutoLockHour < 0) AutoLockHour = 0;
            if (AutoLockHour > 23) AutoLockHour = 23;
            if (AutoLockMinute < 0) AutoLockMinute = 0;
            if (AutoLockMinute > 59) AutoLockMinute = 59;
        }

        private void ClampLocation()
        {
            if (Latitude < -90.0) Latitude = -90.0;
            if (Latitude > 90.0) Latitude = 90.0;
            if (Longitude < -180.0) Longitude = -180.0;
            if (Longitude > 180.0) Longitude = 180.0;
            FacingDegrees %= 360;
            if (FacingDegrees < 0) FacingDegrees += 360;
        }

        private static List<ShortcutItem> DefaultShortcuts()
        {
            List<ShortcutItem> list = new List<ShortcutItem>();
            list.Add(new ShortcutItem("Crunchyroll", "https://www.crunchyroll.com"));
            list.Add(new ShortcutItem("YouTube", "https://www.youtube.com"));
            list.Add(new ShortcutItem("Netflix", "https://www.netflix.com"));
            list.Add(new ShortcutItem("Twitch", "https://www.twitch.tv"));
            list.Add(new ShortcutItem("Caedrel", "https://www.twitch.tv/caedrel"));
            list.Add(new ShortcutItem("Miruro", "https://www.miruro.tv"));
            list.Add(new ShortcutItem("Jellyfin", "http://localhost:8096"));
            return list;
        }

        // Existing users keep their customized shortcut order, but receive
        // the three requested built-ins once. Matching by URL also avoids a
        // duplicate when a user has already added one under a different name.
        private static bool EnsureBuiltInShortcuts(List<ShortcutItem> list)
        {
            bool changed = false;
            changed |= AddIfMissing(list, "Twitch", "https://www.twitch.tv");
            changed |= AddIfMissing(list, "Caedrel", "https://www.twitch.tv/caedrel");
            changed |= AddIfMissing(list, "Miruro", "https://www.miruro.tv");
            return changed;
        }

        private static bool AddIfMissing(List<ShortcutItem> list, string name, string target)
        {
            foreach (ShortcutItem item in list)
            {
                if (item != null && string.Equals(
                    (item.Target ?? "").TrimEnd('/'), target.TrimEnd('/'),
                    StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            list.Add(new ShortcutItem(name, target));
            return true;
        }

        private static bool RemoveEmptyShortcuts(List<ShortcutItem> list)
        {
            bool changed = false;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                ShortcutItem item = list[i];
                string target = item == null ? "" : (item.Target ?? "").Trim();
                if (target.Length == 0 || target == "https://" || target == "http://")
                {
                    list.RemoveAt(i);
                    changed = true;
                }
            }
            return changed;
        }

        private static bool MigrateGpuCompat(List<ShortcutItem> list)
        {
            bool changed = false;
            foreach (ShortcutItem item in list)
            {
                if (item == null || !item.GpuCompat) continue;
                item.GpuDisableDirectComposition = true;
                item.GpuUseD3d9 = true;
                item.GpuDisableVsync = true;
                item.GpuCompat = false;
                changed = true;
            }
            return changed;
        }
    }
}
