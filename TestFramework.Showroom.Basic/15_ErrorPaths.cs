using TestFramework.Core.Exceptions;
using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using TestFramework.LocalIO;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

public class ErrorPaths_TimeoutExhaustion(ITestOutputHelper outputHelper)
{
    // Failure examples should still read like scenarios. This one waits politely
    // until the world fails to answer, then shows the timeout on the normal run path.

    private readonly Timeline _timeline = Timeline.Create()
        .WaitForEvent(LocalIOExt.Events.FileExists(Var.Ref<string>("missingPath")))
        .WithTimeOut(TimeSpan.FromMilliseconds(150))
        .Name("wait-for-never")
        .Build();

    [Fact]
    public async Task Run_ShowsTimeoutInTheNormalFailureSurface()
    {
        string missingPath = Path.Combine(Path.GetTempPath(), $"showroom-timeout-{Guid.NewGuid():N}.txt");

        TimelineRun run = await this._timeline.SetupRun(outputHelper)
            .AddVariable("missingPath", missingPath)
            .RunAsync();

        TimelineRunFailedException exception = Assert.Throws<TimelineRunFailedException>(() => run.EnsureRanToCompletion());

        Assert.Single(exception.FailedSteps);
        Assert.Contains("File Exists Event", exception.FailedSteps[0].StepName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

public class ErrorPaths_DiscoveryCountMismatch(ITestOutputHelper outputHelper)
{
    // Discovery failures are different from runtime assertion failures: the run
    // shape was fine, but the folder had a different number of opinions than the
    // scenario contract allowed.

    private readonly Timeline _timeline = Timeline.Create()
        .FindArtifactsAs(["file0"], new FileArtifactFolderFinder(Var.Ref<string>("folder")))
        .Build();

    [Fact]
    public async Task Run_ShowsFinderCountMismatch()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"showroom-find-mismatch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "alpha.txt"), "alpha");
        File.WriteAllText(Path.Combine(folder, "beta.txt"), "beta");

        try
        {
            TimelineRun run = await this._timeline.SetupRun(outputHelper)
                .AddVariable("folder", folder)
                .RunAsync();

            TimelineRunFailedException exception = Assert.Throws<TimelineRunFailedException>(() => run.EnsureRanToCompletion());

            Assert.Contains("FindArtifactsAs", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("expected 1", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

public class ErrorPaths_MissingInputDiagnosis(ITestOutputHelper outputHelper)
{
    // Planning failures are the cheapest failures. With local project refs this
    // chapter can also prove the formatted framework output directly instead of
    // only checking the plain exception message.

    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(LocalIOExt.Trigger.Cmd(Var.Ref<string>("cmdCommand")))
        .Build();

    [Fact]
    public async Task Run_ShowsFormattedRecoveryGuidance()
    {
        IOContractViolationException exception = await Assert.ThrowsAsync<IOContractViolationException>(() =>
            this._timeline.SetupRun(outputHelper).RunAsync());

        string formatted = exception.ToString();

        Assert.Contains("[FRAMEWORK ERROR]", formatted, StringComparison.Ordinal);
        Assert.Contains("Recovery:", formatted, StringComparison.Ordinal);
        Assert.Contains("cmdCommand", formatted, StringComparison.Ordinal);
    }
}