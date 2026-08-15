using Xunit;

namespace TestFramework.Showroom.Azure;

/// <summary>
/// Decides whether this machine can run Docker-backed chapters, and tells the runner why not.
/// </summary>
/// <remarks>
/// <para>
/// Every chapter in this lane needs a container daemon. Without the gate the whole project failed
/// with raw container errors before the first line of any sample was reached, which taught a reader
/// nothing except that the showroom is broken.
/// </para>
/// <para>
/// The probe is not only a check. On Windows, Docker Desktop publishes its engine on one of two
/// named pipes depending on the backend, and a machine that has Docker running but no DOCKER_HOST
/// set will otherwise fail with the daemon right there. Setting DOCKER_HOST from whichever pipe
/// exists fixes those runs instead of skipping them.
/// </para>
/// <para>
/// This file is duplicated in <c>TestFramework.Showroom.Web</c> and again in ConsumerScenarios. That
/// duplication is the argument for shipping the gate as a small <c>TestFramework.Xunit</c> package:
/// a Docker-backed timeline is exactly the kind of test every consumer of this framework writes.
/// </para>
/// </remarks>
internal static class ShowroomEnvironmentGate
{
    private const string DockerHostEnvironmentVariable = "DOCKER_HOST";

    public static bool TryEnableDockerHost(out string reason)
    {
        if (IsDockerOptedInViaHost())
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

    private static bool IsDockerOptedInViaHost()
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DockerHostEnvironmentVariable));

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
/// A <see cref="FactAttribute"/> that skips itself, with a reason, when no Docker daemon answers.
/// </summary>
/// <remarks>
/// The trait and the skip answer different questions. <c>Category=DockerSmoke</c> answers "should
/// the fast lane run this?" and needs the runner to remember a filter; the skip answers "will this
/// fail environmentally?" and needs nothing from anybody. Both are applied throughout this lane.
/// </remarks>
internal sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        if (!ShowroomEnvironmentGate.TryEnableDockerHost(out string reason))
        {
            Skip = reason;
        }
    }
}
