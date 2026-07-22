using System;
using System.Diagnostics;
using System.IO;

namespace ProjectorDash
{
    /// <summary>
    /// Optional physical projector standby through libCEC. Ordinary PC HDMI
    /// outputs generally do not expose CEC, so this activates only when the
    /// free cec-client utility (normally installed with a USB-CEC adapter) is
    /// present. The existing Windows display-off action remains independent.
    /// </summary>
    public static class CecControl
    {
        public static string FindClient()
        {
            string besideApp = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "cec-client.exe");
            if (File.Exists(besideApp)) return besideApp;

            string[] roots = new string[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            };
            string[] relative = new string[]
            {
                Path.Combine("Pulse-Eight", "USB-CEC Adapter", "cec-client.exe"),
                Path.Combine("Pulse-Eight", "libCEC", "cec-client.exe"),
                Path.Combine("VideoLAN", "VLC", "cec-client.exe")
            };
            foreach (string root in roots)
            {
                if (string.IsNullOrEmpty(root)) continue;
                foreach (string part in relative)
                {
                    string path = Path.Combine(root, part);
                    if (File.Exists(path)) return path;
                }
            }
            return "";
        }

        public static bool SendProjectorStandby(out string message)
        {
            string path = FindClient();
            if (path.Length == 0)
            {
                message = "No HDMI-CEC adapter was found. This tablet can turn off the Windows video signal, but physical projector standby needs a CEC-capable HDMI output or a USB-CEC adapter with the free libCEC client installed.";
                return false;
            }
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = path;
                info.Arguments = "-s -d 1";
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardInput = true;
                info.RedirectStandardOutput = false;
                info.RedirectStandardError = false;
                Process process = Process.Start(info);
                if (process == null)
                {
                    message = "Windows could not start cec-client.";
                    return false;
                }
                process.StandardInput.WriteLine("standby 0");
                process.StandardInput.WriteLine("q");
                process.StandardInput.Close();
                process.Dispose();
                message = "HDMI-CEC standby sent.";
                return true;
            }
            catch (Exception ex)
            {
                message = "HDMI-CEC could not start: " + ex.Message;
                return false;
            }
        }
    }
}
