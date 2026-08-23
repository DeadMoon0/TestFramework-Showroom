using Xunit;

namespace TestFramework.Showroom.UI;

/// <summary>
/// Decides whether this machine can run browser-against-container chapters, and tells the runner why
/// not.
/// </summary>
/// <remarks>
/// <para>
/// This lane needs two things at once: a Docker daemon to host the storefront, and a browser opted in
/// through <c>TESTFRAMEWORK_UI_BROWSER</c> to drive it. Either one missing is a skip with its reason,
/// never a failure - a fresh clone goes green on a bare <c>dotnet test</c>.
/// </para>
/// <para>
/// The Docker probe is copied from the web lane's gate, named-pipe repair included. That is the third
/// copy in this repository, which is the standing argument for shipping the gate as a small
/// <c>TestFramework.Xunit</c> package.
/// </para>
/// </remarks>
internal static class UiShowroomGate
{
    private const string DockerHostEnvironmentVariable = "DOCKER_HOST";
    private const string BrowserEnvironmentVariable = "TESTFRAMEWORK_UI_BROWSER";

    /// <summary>The browser the run asked for, for example <c>msedge</c> or <c>chromium</c>.</summary>
    public static string? RequestedBrowser => Environment.GetEnvironmentVariable(BrowserEnvironmentVariable);

    public static bool TryEnable(out string reason)
    {
        if (string.IsNullOrWhiteSpace(RequestedBrowser))
        {
            reason = $"Requires a browser, opted in through {BrowserEnvironmentVariable} (for example 'msedge').";
            return false;
        }

        return TryEnableDockerHost(out reason);
    }

    private static bool TryEnableDockerHost(out string reason)
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DockerHostEnvironmentVariable)))
        {
            reason = string.Empty;
            return true;
        }

        if (OperatingSystem.IsWindows())
        {
            foreach (string candidate in new[]
                     {
                         "npipe://./pipe/docker_engine",
                         "npipe://./pipe/dockerDesktopLinuxEngine",
                     })
            {
                if (!NamedPipeExists(candidate))
                {
                    continue;
                }

                Environment.SetEnvironmentVariable(DockerHostEnvironmentVariable, candidate);
                reason = string.Empty;
                return true;
            }

            reason = "Requires Docker Desktop or another reachable Windows Docker named pipe.";
            return false;
        }

        if (File.Exists("/var/run/docker.sock"))
        {
            reason = string.Empty;
            return true;
        }

        reason = "Requires a reachable Docker host or local Docker socket.";
        return false;
    }

    private static bool NamedPipeExists(string dockerHost)
    {
        const string prefix = "npipe://./pipe/";
        if (!dockerHost.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string pipeName = dockerHost[prefix.Length..];
        return File.Exists($@"\\.\pipe\{pipeName}");
    }
}

/// <summary>
/// A <see cref="FactAttribute"/> that skips itself, with a reason, when the machine has no Docker
/// daemon or no browser opt-in.
/// </summary>
internal sealed class UiDockerFactAttribute : FactAttribute
{
    public UiDockerFactAttribute()
    {
        if (!UiShowroomGate.TryEnable(out string reason))
        {
            Skip = reason;
        }
    }
}
