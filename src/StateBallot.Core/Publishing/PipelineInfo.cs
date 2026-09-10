using System.Diagnostics;
using System.Reflection;

namespace StateBallot.Core.Publishing;

/// <summary>Which pipeline build produced an output. Stamped into run.json.</summary>
public static class PipelineInfo
{
    public static string Version =>
        typeof(PipelineInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(PipelineInfo).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    /// <summary>
    /// The pipeline commit: HEAD of the git checkout the executable runs from, else the
    /// commit the SDK stamped into the assembly version at build time, else null.
    /// </summary>
    public static string? GitCommit()
    {
        var fromCheckout = HeadOfCheckout(AppContext.BaseDirectory);
        if (fromCheckout is not null)
            return fromCheckout;

        var plus = Version.IndexOf('+');
        if (plus >= 0 && Version.Length - plus - 1 >= 40)
        {
            var sha = Version.Substring(plus + 1, 40);
            if (sha.All(Uri.IsHexDigit))
                return sha.ToLowerInvariant();
        }

        return null;
    }

    private static string? HeadOfCheckout(string startDir)
    {
        string? repoRoot = null;
        for (var d = new DirectoryInfo(startDir); d is not null; d = d.Parent)
        {
            if (Directory.Exists(Path.Combine(d.FullName, ".git")))
            {
                repoRoot = d.FullName;
                break;
            }
        }
        if (repoRoot is null)
            return null;

        try
        {
            var psi = new ProcessStartInfo("git", "rev-parse HEAD")
            {
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var process = Process.Start(psi);
            if (process is null)
                return null;
            var output = process.StandardOutput.ReadToEnd().Trim();
            return process.WaitForExit(5000) && process.ExitCode == 0 && output.Length == 40 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
