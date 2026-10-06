using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace TypeSafeSharp;

// Header values that identify this client. Never "typesafe-sdk/...": that would make an
// unofficial client look like official traffic (ADR-0002).
internal static class SdkInfo
{
    public static readonly string Version = ReadVersion();

    public static readonly string UserAgent = "TypeSafeSharp/" + Version;

    // FrameworkDescription, not Environment.Version, which is 4.0.30319.42000 on every .NET Framework.
    public static readonly string Runtime = Ascii(RuntimeInformation.FrameworkDescription + " (" + OperatingSystem() + "; " + RuntimeInformation.OSArchitecture + ")");

    private static string ReadVersion()
    {
        var informational = typeof(SdkInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var plus = informational.IndexOf("+", StringComparison.Ordinal);
        return plus >= 0 ? informational.Substring(0, plus) : informational;
    }

    private static string OperatingSystem()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "windows";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "linux";
        }

        return RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx" : "other";
    }

    // Header values must be ASCII; a localised framework description might not be.
    private static string Ascii(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(c is >= ' ' and <= '~' ? c : '_');
        }

        return builder.ToString();
    }
}
