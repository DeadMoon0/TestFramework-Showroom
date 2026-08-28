using System;
using System.IO;
using TestFramework.Container;
using Xunit;

namespace TestFramework.Showroom.Web;

/// <summary>
/// Decides whether this machine can run Docker-backed chapters, and tells the runner why not.
/// </summary>
/// <remarks>
/// <para>
/// Every chapter in this lane except <c>W3_SchemaFromModels</c> needs a container daemon. W3 stays a
/// plain <c>[Fact]</c> on purpose, so the asymmetry a reader would otherwise have to infer from the
/// README is stated by the code.
/// </para>
/// <para>
/// The probe itself is <c>TestFramework.Container</c>'s and always was - <c>ContainerDockerHost</c> is
/// public, knows both pipes Docker Desktop uses, and has its own cases. This file used to carry its own
/// copy of that logic, which is why it read as duplication worth extracting into a package: what was
/// actually duplicated was already shipped, one dependency away.
/// </para>
/// <para>
/// So what is left here is the only part that is genuinely this repository's: turning "no daemon" into a
/// skip with a sentence, which needs xunit and is exactly what the runtime packages must not depend on.
/// </para>
/// </remarks>
internal static class ShowroomEnvironmentGate
{
    /// <summary>
    /// Finds a Docker daemon, pointing this process at it when it takes finding.
    /// </summary>
    /// <param name="reason">Why this machine cannot, when it cannot; empty otherwise.</param>
    /// <returns>True when a daemon is reachable.</returns>
    public static bool TryEnableDockerHost(out string reason)
    {
        // Repairs the run rather than only judging it: a machine with Docker running but no DOCKER_HOST
        // set would otherwise be skipped with the daemon right there.
        ContainerDockerHost.EnsureConfigured();

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_HOST")))
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
/// A <see cref="FactAttribute"/> that skips itself, with a reason, when no Docker daemon answers.
/// </summary>
/// <remarks>
/// The trait and the skip answer different questions. <c>Category=DockerSmoke</c> answers "should the
/// fast lane run this?" and needs the runner to remember a filter; the skip answers "will this fail
/// environmentally?" and needs nothing from anybody. Both are applied throughout this lane.
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
