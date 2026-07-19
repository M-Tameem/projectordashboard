using System;
using System.Management;

namespace ProjectorDash
{
    /// <summary>
    /// Screen backlight brightness via WMI (WmiMonitorBrightness / -Methods).
    /// On the T100TAF this drives the tablet's internal panel; the projector's
    /// brightness is set on the projector itself. If the WMI classes are not
    /// present, Available is false and the UI simply hides the slider.
    /// </summary>
    public sealed class BrightnessControl
    {
        private bool _available;

        public bool Available
        {
            get { return _available; }
        }

        public BrightnessControl()
        {
            try
            {
                // Probe once; the classes are absent on desktops without a
                // controllable backlight.
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness"))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementObject mo in results)
                    {
                        _available = true;
                        mo.Dispose();
                        break;
                    }
                }
            }
            catch
            {
                _available = false;
            }
        }

        /// <summary>0..100, or -1 if unavailable.</summary>
        public int GetBrightness()
        {
            if (!_available) return -1;
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness"))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementObject mo in results)
                    {
                        int value = Convert.ToInt32(mo["CurrentBrightness"]);
                        mo.Dispose();
                        return value;
                    }
                }
            }
            catch { }
            return -1;
        }

        /// <summary>0..100</summary>
        public bool SetBrightness(int percent)
        {
            if (!_available) return false;
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods"))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementObject mo in results)
                    {
                        mo.InvokeMethod("WmiSetBrightness",
                            new object[] { (uint)1, (byte)percent });
                        mo.Dispose();
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }
    }
}
