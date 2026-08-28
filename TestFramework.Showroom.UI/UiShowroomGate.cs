using System;
using System.IO;
using TestFramework.Container;
using TestFramework.UI.Browser;
using TestFramework.UI.Browser.Runtime;
using Xunit;

namespace TestFramework.Showroom.UI;

/// <summary>
/// Decides whether this machine can run browser-against-container chapters, and tells the runner why
/// not.
/// </summary>
/// <remarks>
/// <para>
/// This lane needs two things at once: a Docker daemon to host the storefront, and a browser to drive it.
/// Either one missing is a skip with its reason, never a failure - a fresh clone goes green on a bare
/// <c>dotnet test</c>.
/// </para>
/// <para>
/// The browser is <em>found</em>, not asked for. This gate used to skip unless
/// <c>TESTFRAMEWORK_UI_BROWSER</c> named one, which meant the chapters sat out on every machine that had
/// Edge installed and had simply never been told - the common case. The variable still overrides the
/// choice; it no longer decides whether the lane runs at all.
/// </para>
/// <para>
/// The Docker probe belongs to <c>TestFramework.Container</c> - <c>ContainerDockerHost</c> is public and
/// knows both pipes Docker Desktop uses. This file used to carry its own copy of it, which read as
/// duplication worth extracting somewhere; what was duplicated was already shipped, one dependency away.
/// </para>
/// </remarks>
internal static class UiShowroomGate
{
    private const string DockerHostEnvironmentVariable = "DOCKER_HOST";
    private const string BrowserEnvironmentVariable = "TESTFRAMEWORK_UI_BROWSER";

    private static readonly Lazy<UiAvailableBrowser?> Available = new Lazy<UiAvailableBrowser?>(Choose);

    /// <summary>
    /// The browser these chapters will drive, or null when this machine has none.
    /// </summary>
    /// <remarks>
    /// Resolved once per process, and the single answer both the gate and the configuration read - deriving
    /// it twice is how the skip reason and the browser actually launched could disagree.
    /// </remarks>
    public static UiAvailableBrowser? Browser => Available.Value;

    public static bool TryEnable(out string reason)
    {
        if (Browser is null)
        {
            reason = Environment.GetEnvironmentVariable(BrowserEnvironmentVariable) is { Length: > 0 } requested
                ? $"{BrowserEnvironmentVariable} asks for '{requested}', which is not installed on this machine."
                : "Requires a browser. Install Edge or Chrome, or run 'playwright install chromium' once.";

            return false;
        }

        return TryEnableDockerHost(out reason);
    }

    private static UiAvailableBrowser? Choose()
        => Environment.GetEnvironmentVariable(BrowserEnvironmentVariable) is { Length: > 0 } requested
            ? BrowserExt.Tooling.FindAvailableBrowser(requested)
            : BrowserExt.Tooling.FindAvailableBrowser();

    private static bool TryEnableDockerHost(out string reason)
    {
        // Repairs the run rather than only judging it: a machine with Docker running but no DOCKER_HOST
        // set would otherwise be skipped with the daemon right there.
        ContainerDockerHost.EnsureConfigured();

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DockerHostEnvironmentVariable)))
        {
            reason = string.Empty;

            return true;
        }

        if (!OperatingSystem.IsWindows() && File.Exists("/var/run/docker.sock"))
        {
            reason = string.Empty;

            return true;
        }

        reason = OperatingSystem.IsWindows()
            ? "Requires Docker Desktop or another reachable Windows Docker named pipe."
            : "Requires a reachable Docker host or local Docker socket.";

        return false;
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
