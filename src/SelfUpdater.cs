using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace ProjectorDash
{
    public enum UpdateResultKind
    {
        Ready,
        UpToDate,
        NoRelease,
        Error
    }

    public sealed class UpdateResult
    {
        public UpdateResultKind Kind;
        public string Message = "";
        public string ReleaseVersion = "";
        public string DownloadedPath = "";
    }

    /// <summary>
    /// Small, dependency-free updater for the public GitHub release channel.
    /// A release is accepted only when it contains both Dashboard.exe and a
    /// matching Dashboard.exe.sha256 asset, and the downloaded executable has
    /// the exact version declared by the release tag.
    /// </summary>
    public static class SelfUpdater
    {
        public const string RepositoryUrl =
            "https://github.com/M-Tameem/projectordashboard";
        private const string LatestReleaseApi =
            "https://api.github.com/repos/M-Tameem/projectordashboard/releases/latest";
        private const string ExecutableAssetName = "Dashboard.exe";
        private const string ChecksumAssetName = "Dashboard.exe.sha256";
        private const long MaxExecutableBytes = 32L * 1024L * 1024L;

        private sealed class ReleaseAsset
        {
            public string name { get; set; }
            public string browser_download_url { get; set; }
            public long size { get; set; }
        }

        private sealed class ReleaseDescription
        {
            public string tag_name { get; set; }
            public string html_url { get; set; }
            public List<ReleaseAsset> assets { get; set; }
        }

        public static string CurrentVersion
        {
            get
            {
                Version version = Assembly.GetExecutingAssembly().GetName().Version;
                if (version == null) return VersionInfo.ProductVersion;
                return string.Format("{0}.{1}.{2}", version.Major,
                    version.Minor, Math.Max(0, version.Build));
            }
        }

        /// <summary>
        /// Runs on a worker thread. The callback is informational and may be
        /// invoked from that worker, so UI callers must marshal it themselves.
        /// </summary>
        public static UpdateResult CheckAndDownload(Action<string> progress)
        {
            string updatePath = "";
            try
            {
                EnableTls12();
                Report(progress, "Checking GitHub…");

                string json;
                try
                {
                    json = DownloadString(LatestReleaseApi);
                }
                catch (WebException ex)
                {
                    HttpWebResponse response = ex.Response as HttpWebResponse;
                    if (response != null && response.StatusCode == HttpStatusCode.NotFound)
                    {
                        return Result(UpdateResultKind.NoRelease,
                            "No GitHub release has been published yet.");
                    }
                    throw;
                }

                ReleaseDescription release =
                    new JavaScriptSerializer().Deserialize<ReleaseDescription>(json);
                if (release == null || string.IsNullOrEmpty(release.tag_name))
                    return Result(UpdateResultKind.Error,
                        "GitHub returned a release without a version tag.");

                Version latest;
                if (!TryParseReleaseVersion(release.tag_name, out latest))
                    return Result(UpdateResultKind.Error,
                        "The latest release tag must look like v1.2.3.");

                Version current = Assembly.GetExecutingAssembly().GetName().Version;
                if (current != null && latest.CompareTo(current) <= 0)
                {
                    UpdateResult upToDate = Result(UpdateResultKind.UpToDate,
                        "Projector Dashboard " + CurrentVersion + " is already current.");
                    upToDate.ReleaseVersion = FormatVersion(latest);
                    return upToDate;
                }

                ReleaseAsset executable = FindAsset(release.assets, ExecutableAssetName);
                ReleaseAsset checksum = FindAsset(release.assets, ChecksumAssetName);
                if (executable == null || checksum == null)
                    return Result(UpdateResultKind.Error,
                        "Release " + release.tag_name + " is missing Dashboard.exe or Dashboard.exe.sha256.");
                if (executable.size <= 0 || executable.size > MaxExecutableBytes)
                    return Result(UpdateResultKind.Error,
                        "The release executable has an unexpected size.");

                string currentPath = Assembly.GetExecutingAssembly().Location;
                string currentDirectory = Path.GetDirectoryName(currentPath);
                if (string.IsNullOrEmpty(currentDirectory))
                    return Result(UpdateResultKind.Error,
                        "The dashboard installation folder could not be located.");
                if (!CanWriteDirectory(currentDirectory))
                    return Result(UpdateResultKind.Error,
                        "Windows will not let the dashboard write to its installation folder. Move it to a writable folder such as C:\\Dashboard.");

                updatePath = currentPath + ".update";
                TryDelete(updatePath);

                Report(progress, "Downloading " + FormatVersion(latest) + "…");
                string expectedHash = ParseSha256(DownloadString(checksum.browser_download_url));
                if (string.IsNullOrEmpty(expectedHash))
                    return Result(UpdateResultKind.Error,
                        "The release checksum file is invalid.");

                using (WebClient client = CreateClient())
                {
                    client.DownloadFile(executable.browser_download_url, updatePath);
                }

                FileInfo downloaded = new FileInfo(updatePath);
                if (!downloaded.Exists || downloaded.Length <= 0 ||
                    downloaded.Length > MaxExecutableBytes)
                    throw new InvalidDataException("The downloaded executable has an unexpected size.");

                Report(progress, "Verifying update…");
                string actualHash = ComputeSha256(updatePath);
                if (!string.Equals(expectedHash, actualHash,
                    StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The downloaded executable failed its SHA-256 checksum.");

                AssemblyName downloadedAssembly = AssemblyName.GetAssemblyName(updatePath);
                Version downloadedVersion = downloadedAssembly.Version;
                AssemblyName currentAssembly = Assembly.GetExecutingAssembly().GetName();
                if (!string.Equals(downloadedAssembly.Name, currentAssembly.Name,
                    StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The release asset is not Projector Dashboard.");
                if (downloadedVersion == null || downloadedVersion.CompareTo(latest) != 0)
                    throw new InvalidDataException(
                        "The executable version does not match release " + release.tag_name + ".");

                UpdateResult ready = Result(UpdateResultKind.Ready,
                    "Projector Dashboard " + FormatVersion(latest) + " is ready to install.");
                ready.ReleaseVersion = FormatVersion(latest);
                ready.DownloadedPath = updatePath;
                return ready;
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrEmpty(updatePath)) TryDelete(updatePath);
                return Result(UpdateResultKind.Error, FriendlyNetworkError(ex));
            }
        }

        /// <summary>
        /// Copies the currently running build to the temp directory and starts
        /// that copy in helper mode. The helper waits for this process to exit,
        /// replaces the real exe, and relaunches it.
        /// </summary>
        public static bool BeginInstall(UpdateResult result, out string error)
        {
            error = "";
            try
            {
                if (result == null || result.Kind != UpdateResultKind.Ready ||
                    string.IsNullOrEmpty(result.DownloadedPath) ||
                    !File.Exists(result.DownloadedPath))
                {
                    error = "The downloaded update is no longer available.";
                    return false;
                }

                string currentPath = Assembly.GetExecutingAssembly().Location;
                string helperPath = Path.Combine(Path.GetTempPath(),
                    "ProjectorDashboard-Updater-" + Guid.NewGuid().ToString("N") + ".exe");
                File.Copy(currentPath, helperPath, true);

                int processId;
                using (Process current = Process.GetCurrentProcess())
                    processId = current.Id;

                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = helperPath;
                start.Arguments = "--apply-update " + processId.ToString() + " " +
                    QuoteArgument(currentPath) + " " +
                    QuoteArgument(result.DownloadedPath) + " " +
                    QuoteArgument(result.ReleaseVersion);
                start.UseShellExecute = false;
                start.CreateNoWindow = true;
                start.WindowStyle = ProcessWindowStyle.Hidden;
                Process helper = Process.Start(start);
                if (helper == null)
                {
                    error = "Windows could not start the update helper.";
                    TryDelete(helperPath);
                    return false;
                }
                helper.Dispose();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>Entry point used only by the temporary updater copy.</summary>
        public static void RunApplyMode(string[] args)
        {
            string targetPath = args.Length > 2 ? args[2] : "";
            string updatePath = args.Length > 3 ? args[3] : "";
            string releaseVersion = args.Length > 4 ? args[4] : "";
            string helperPath = Assembly.GetExecutingAssembly().Location;
            string backupPath = targetPath + ".previous";
            try
            {
                int parentId;
                if (args.Length < 5 || !int.TryParse(args[1], out parentId) ||
                    string.IsNullOrEmpty(targetPath) || string.IsNullOrEmpty(updatePath))
                    throw new InvalidDataException("The update helper arguments are invalid.");

                WaitForProcess(parentId, 30000);
                // A successful prior update normally removes this backup. If
                // one survived a power loss but the currently running target
                // still exists, that target is the known-good rollback copy.
                if (File.Exists(targetPath)) TryDelete(backupPath);
                ReplaceWithRetry(targetPath, updatePath, backupPath);

                string readyMarker = Path.Combine(Path.GetTempPath(),
                    "ProjectorDashboard-UpdateReady-" + Guid.NewGuid().ToString("N") + ".tmp");
                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = targetPath;
                start.Arguments = "--update-ready " + QuoteArgument(readyMarker);
                start.WorkingDirectory = Path.GetDirectoryName(targetPath);
                start.UseShellExecute = true;
                Process updated = Process.Start(start);
                if (updated == null)
                    throw new InvalidOperationException("The updated dashboard could not be started.");

                bool ready = WaitForReady(updated, readyMarker, 60000);
                if (!ready && updated.HasExited)
                {
                    RestoreBackup(targetPath, backupPath);
                    Process.Start(targetPath);
                    WriteLog("Update " + releaseVersion +
                        " rolled back because the new build exited during startup.");
                }
                else
                {
                    TryDelete(backupPath);
                    WriteLog("Updated successfully to " + releaseVersion + ".");
                }
                TryDelete(readyMarker);
                updated.Dispose();
            }
            catch (Exception ex)
            {
                try
                {
                    if (!string.IsNullOrEmpty(backupPath) && File.Exists(backupPath))
                    {
                        RestoreBackup(targetPath, backupPath);
                        Process.Start(targetPath);
                    }
                }
                catch { }
                WriteLog("Update failed: " + ex.Message);
            }
            finally
            {
                TryDelete(updatePath);
                ScheduleDeleteOnRestart(helperPath);
            }
        }

        public static void SignalUpdatedBuildReady(string markerPath)
        {
            try
            {
                if (string.IsNullOrEmpty(markerPath)) return;
                string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\') + "\\";
                string marker = Path.GetFullPath(markerPath);
                if (!marker.StartsWith(temp, StringComparison.OrdinalIgnoreCase)) return;
                if (!Path.GetFileName(marker).StartsWith(
                    "ProjectorDashboard-UpdateReady-", StringComparison.OrdinalIgnoreCase)) return;
                File.WriteAllText(marker, "ready", Encoding.ASCII);
            }
            catch { }
        }

        private static UpdateResult Result(UpdateResultKind kind, string message)
        {
            return new UpdateResult { Kind = kind, Message = message };
        }

        private static void Report(Action<string> callback, string status)
        {
            if (callback != null) callback(status);
        }

        private static void EnableTls12()
        {
            // TLS 1.2 is value 3072 even on framework builds where the named
            // enum member is unavailable at compile time.
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
        }

        private static WebClient CreateClient()
        {
            WebClient client = new WebClient();
            client.Headers[HttpRequestHeader.UserAgent] =
                "ProjectorDashboard/" + CurrentVersion;
            client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            return client;
        }

        private static string DownloadString(string url)
        {
            using (WebClient client = CreateClient())
                return client.DownloadString(url);
        }

        private static ReleaseAsset FindAsset(List<ReleaseAsset> assets, string name)
        {
            if (assets == null) return null;
            foreach (ReleaseAsset asset in assets)
            {
                if (asset != null && string.Equals(asset.name, name,
                    StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(asset.browser_download_url))
                    return asset;
            }
            return null;
        }

        private static bool TryParseReleaseVersion(string tag, out Version version)
        {
            version = null;
            if (string.IsNullOrEmpty(tag)) return false;
            Match match = Regex.Match(tag.Trim(), @"^[vV](\d+)\.(\d+)\.(\d+)$");
            if (!match.Success) return false;
            return Version.TryParse(string.Format("{0}.{1}.{2}.0",
                match.Groups[1].Value, match.Groups[2].Value,
                match.Groups[3].Value), out version);
        }

        private static string FormatVersion(Version version)
        {
            return string.Format("{0}.{1}.{2}", version.Major,
                version.Minor, Math.Max(0, version.Build));
        }

        private static string ParseSha256(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            Match match = Regex.Match(text, @"(?i)(?<![0-9a-f])[0-9a-f]{64}(?![0-9a-f])");
            return match.Success ? match.Value.ToLowerInvariant() : "";
        }

        private static string ComputeSha256(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(stream);
                StringBuilder text = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash) text.Append(value.ToString("x2"));
                return text.ToString();
            }
        }

        private static bool CanWriteDirectory(string directory)
        {
            string test = Path.Combine(directory,
                ".dashboard-update-write-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (FileStream stream = new FileStream(test, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None))
                    stream.WriteByte(1);
                File.Delete(test);
                return true;
            }
            catch
            {
                TryDelete(test);
                return false;
            }
        }

        private static string FriendlyNetworkError(Exception ex)
        {
            WebException web = ex as WebException;
            if (web != null)
                return "Update check failed. Check the tablet's internet connection and Windows date/time.\n\n" + web.Message;
            return "Update failed.\n\n" + ex.Message;
        }

        private static void WaitForProcess(int processId, int timeoutMs)
        {
            try
            {
                using (Process process = Process.GetProcessById(processId))
                    process.WaitForExit(timeoutMs);
            }
            catch { }
            Thread.Sleep(300);
        }

        private static void ReplaceWithRetry(string target, string update, string backup)
        {
            Exception last = null;
            for (int attempt = 0; attempt < 60; attempt++)
            {
                try
                {
                    if (!File.Exists(update))
                        throw new FileNotFoundException("The downloaded update disappeared.", update);
                    if (File.Exists(target) && !File.Exists(backup))
                        File.Move(target, backup);
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(update, target);
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    Thread.Sleep(250);
                }
            }
            throw new IOException("Windows could not replace Dashboard.exe.", last);
        }

        private static bool WaitForReady(Process process, string marker, int timeoutMs)
        {
            int elapsed = 0;
            while (elapsed < timeoutMs)
            {
                if (File.Exists(marker)) return true;
                try { if (process.HasExited) return false; }
                catch { return false; }
                Thread.Sleep(250);
                elapsed += 250;
            }
            // If the app is alive but setup is waiting for touch input, do not
            // roll back a healthy process merely because the marker is late.
            try { return !process.HasExited; }
            catch { return false; }
        }

        private static void RestoreBackup(string target, string backup)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    if (File.Exists(target)) File.Delete(target);
                    if (File.Exists(backup)) File.Move(backup, target);
                    return;
                }
                catch { Thread.Sleep(250); }
            }
        }

        private static string QuoteArgument(string value)
        {
            if (value == null) return "\"\"";
            // All values passed here are app-generated filesystem paths or a
            // numeric version, so quotes cannot legitimately occur in them.
            return "\"" + value.Replace("\"", "") + "\"";
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
            }
            catch { }
        }

        private static void WriteLog(string message)
        {
            try
            {
                Directory.CreateDirectory(AppConfig.ConfigDir());
                File.AppendAllText(Path.Combine(AppConfig.ConfigDir(), "update.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message +
                    Environment.NewLine);
            }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll",
            CharSet = System.Runtime.InteropServices.CharSet.Unicode,
            SetLastError = true)]
        private static extern bool MoveFileEx(string existingFile,
            string newFile, int flags);

        private static void ScheduleDeleteOnRestart(string path)
        {
            try { MoveFileEx(path, null, 0x00000004); }
            catch { }
        }
    }
}
