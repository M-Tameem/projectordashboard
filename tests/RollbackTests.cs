using System;
using System.IO;
using System.Reflection;
using ProjectorDash;

internal static class RollbackTests
{
    private static int _checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _checks++;
    }
    private static object Call(string name, params object[] args)
    {
        return typeof(SelfUpdater).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, args);
    }
    public static int Main()
    {
        string target = typeof(SelfUpdater).Assembly.Location;
        string folder = Path.GetDirectoryName(target);
        string fixture = Path.Combine(folder, "previous", "Dashboard.exe");
        string previous = target + ".previous", rollback = target + ".rollback";
        // Reproduce an old updater installing this build. The new build must
        // retain .previous before the old helper sees its ready marker.
        File.Copy(fixture, previous, true);
        string marker = Path.Combine(Path.GetTempPath(), "ProjectorDashboard-UpdateReady-" + Guid.NewGuid().ToString("N") + ".tmp");
        SelfUpdater.SignalUpdatedBuildReady(marker);
        Check(File.Exists(marker), "Ready marker created");
        File.Delete(marker);
        Check(File.Exists(rollback) && File.Exists(rollback + ".sha256"), "Previous build retained with checksum");
        File.Delete(previous); // old helper's cleanup
        Check(SelfUpdater.RollbackVersion == "1.4.0", "Offline rollback survives old helper cleanup");
        UpdateResult prepared = SelfUpdater.PrepareRollback(null);
        Check(prepared.Kind == UpdateResultKind.Ready && prepared.IsRollback && prepared.ReleaseVersion == "1.4.0",
            "Saved rollback prepares without network");
        Check((string)Call("ComputeSha256", prepared.DownloadedPath) == prepared.ExpectedSha256,
            "Staged rollback hash matches");
        SelfUpdater.DiscardDownload(prepared);
        Check(!File.Exists(prepared.DownloadedPath) && File.Exists(rollback), "Cancel removes staging and keeps rollback");
        File.WriteAllText(rollback + ".sha256", new string('0', 64));
        Check(SelfUpdater.RollbackVersion == "", "Corrupt rollback checksum rejected");
        File.WriteAllText(rollback + ".sha256", (string)Call("ComputeSha256", rollback));
        File.Copy(target, previous, true);
        Call("PreserveRollback", target);
        Check(SelfUpdater.RollbackVersion == "1.4.0", "Same-version backup cannot overwrite useful rollback");

        // Use disposable files to exercise atomic install and restoration.
        string swapTarget = Path.Combine(folder, "swap-target");
        string swapUpdate = Path.Combine(folder, "swap-update");
        string swapBackup = Path.Combine(folder, "swap-backup");
        File.WriteAllText(swapTarget, "old"); File.WriteAllText(swapUpdate, "new");
        File.WriteAllText(swapBackup, "stale");
        Call("ReplaceWithRetry", swapTarget, swapUpdate, swapBackup);
        Check(File.ReadAllText(swapTarget) == "new" && File.ReadAllText(swapBackup) == "old",
            "Atomic replacement preserves current version even with stale backup");
        Call("RestoreBackup", swapTarget, swapBackup);
        Check(File.ReadAllText(swapTarget) == "old" && !File.Exists(swapBackup), "Failed install can restore original");
        object[] valid = new object[] { "v1.4.0", null };
        Check((bool)Call("TryParseReleaseVersion", valid), "Stable version accepted");
        object[] prerelease = new object[] { "v1.4.0-beta", null };
        Check(!(bool)Call("TryParseReleaseVersion", prerelease), "Prerelease tag rejected");
        Console.WriteLine("Passed " + _checks + " rollback checks.");
        return 0;
    }
}
