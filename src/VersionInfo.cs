using System.Reflection;

[assembly: AssemblyTitle("Projector Dashboard")]
[assembly: AssemblyProduct("Projector Dashboard")]
[assembly: AssemblyCompany("M-Tameem")]
[assembly: AssemblyCopyright("Copyright © M-Tameem")]
[assembly: AssemblyVersion(ProjectorDash.VersionInfo.AssemblyVersion)]
[assembly: AssemblyFileVersion(ProjectorDash.VersionInfo.AssemblyVersion)]
[assembly: AssemblyInformationalVersion(ProjectorDash.VersionInfo.ProductVersion)]

namespace ProjectorDash
{
    /// <summary>
    /// The release workflow replaces these two constants from a v1.2.3 tag
    /// immediately before compiling. Keep the checked-in development version
    /// valid so build.bat also produces a correctly versioned executable.
    /// </summary>
    public static class VersionInfo
    {
        public const string ProductVersion = "1.0.0";
        public const string AssemblyVersion = "1.0.0.0";
    }
}
