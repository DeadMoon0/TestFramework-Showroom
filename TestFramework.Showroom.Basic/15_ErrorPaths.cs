using TestFramework.Core.Exceptions;
using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using TestFramework.LocalIO;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

//doc: Every chapter so far showed something working. This one shows three things not working, on purpose,
//doc: because a framework is only as good as the sentence it produces when the answer is no.
//doc:
//doc: The three failures are genuinely different, and telling them apart is most of debugging:
//doc:
//doc: 1. **A step failed at run time** - the plan was fine, the world did not cooperate. Here: a wait that
//doc:    times out.
//doc: 2. **Discovery found the wrong number of things** - the run shape was fine, but the folder had a
//doc:    different number of opinions than the contract allowed.
//doc: 3. **The plan was never valid** - rejected before anything ran, which is the cheapest failure there
//doc:    is.
//doc:
//doc: All three tests pass. Read that twice: the panels below say `passed`, and the reports inside the
//doc: first two say `1 TIMEOUT` and `1 FAIL`. That is what a test *about* a failure looks like, and it is
//doc: the shape you want for a negative case in your own suite - assert that the run failed the way you
//doc: meant it to, rather than hoping nobody deletes the assertion.
//doc:
//doc: Two things to notice in both of those reports. A timed-out step reports `TIMEOUT`, not `FAIL` - the
//doc: run distinguishes "it went wrong" from "it never answered". And in both, the Cleanup Stage still ran
//doc: and still passed: a failure stops what was scheduled after it, never the teardown.

//doc: Failure one. The wait is given 150 ms and a file that will never appear, so `RunAsync()` returns
//doc: normally - a failed run is still a run - and `EnsureRanToCompletion()` is what throws.
//doc: `TimelineRunFailedException` carries the failed steps, so the test can name which step died and with
//doc: what: one failure, the file-exists event, a `TimeoutException`.
//doc:
//doc: Note what is asserted and what is not. The exception *type* is checked; its wording is not. Two
//doc: timeouts are racing here - the step modifier's deadline and the event's own - and they phrase the
//doc: failure differently, so a substring match on one of the two sentences is a coin flip. Assert on what
//doc: is stable.

public class ErrorPaths_TimeoutExhaustion(ITestOutputHelper outputHelper)
{
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
        Assert.IsType<TimeoutException>(exception.FailedSteps[0].StepException);

        // Assert on the exception type, not on its wording. Two timeouts are racing here — the step
        // modifier's and the event's own — and each phrases the failure differently, so a substring
        // match on one of the two sentences is a coin flip. The type is the same either way.
        Assert.Contains(nameof(TimeoutException), exception.Message, StringComparison.Ordinal);
    }
}

//doc: Failure two. `FindArtifactsAs` differs from `FindArtifacts` in one respect that matters here: it
//doc: takes the exact names you expect, so it also states how many matches there must be. One name, two
//doc: files, and the run fails with `FindArtifactsAs` and `expected 1` in the message.
//doc:
//doc: It surfaces the same way everything else does - the Find Artifact step reports `FAIL` with an
//doc: `ArtifactCountMismatchException` - but the diagnosis is different in kind. Nothing in the environment
//doc: misbehaved and no step is broken. The test said "there will be exactly one" and there was not, and
//doc: the exception says so along with the count it did find and what to do about it.

public class ErrorPaths_DiscoveryCountMismatch(ITestOutputHelper outputHelper)
{
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

//doc: Failure three is chapter 10's rejection again, looked at from the other side. Chapter 10 checked that
//doc: the message names the missing variable; this one checks the formatting the framework wraps around it:
//doc: a `[FRAMEWORK ERROR]` marker and a `Recovery:` line telling you what to do about it.
//doc:
//doc: That is worth testing rather than trusting. A recovery hint that quietly stops being emitted is a
//doc: worse outcome than never having had one. And like chapter 10, the panel is empty - the plan was
//doc: rejected before the run began, so there is no report to show.

public class ErrorPaths_MissingInputDiagnosis(ITestOutputHelper outputHelper)
{
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
