using System.Reflection;

namespace StateBallot.Staging;

/// <summary>
/// The version of the code that ran a capture or pass. The .NET SDK stamps it into every assembly at
/// build time as "&lt;Version&gt;+&lt;git commit&gt;": the version from Directory.Build.props, and the commit
/// the build was made from. Nothing is read at run time, so a container (which has no .git) records the
/// same thing as the host. A build made outside a git repo has a version and no commit.
/// </summary>
public sealed record BuildVersion(string? Version, string? GitSha)
{
    /// <summary>The version this code was built as.</summary>
    public static BuildVersion Current { get; } = Parse(
        typeof(BuildVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>Splits an informational version such as "0.1.0+e0893040…" into its version and commit.</summary>
    public static BuildVersion Parse(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
            return new BuildVersion(null, null);

        var plus = informationalVersion.IndexOf('+');
        if (plus < 0)
            return new BuildVersion(informationalVersion.Trim(), null);

        var version = informationalVersion[..plus].Trim();
        var sha = informationalVersion[(plus + 1)..].Trim().ToLowerInvariant();
        return new BuildVersion(
            version.Length > 0 ? version : null,
            sha.Length == 40 && sha.All(Uri.IsHexDigit) ? sha : null);
    }
}
