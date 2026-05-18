using TestFramework.Core.Artifacts;
using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Assertions;
using TestFramework.Core.Variables;
using TestFramework.LocalIO;
using TestFramework.Simple;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

file static class ParallelSamplePaths
{
    public static string BuildOutput => AppContext.BaseDirectory;

    public static string UniqueFile(string prefix)
        => Path.Combine(BuildOutput, $"{prefix}-{Guid.NewGuid():N}.txt");
}

public class Parallel_SetVariablePrepareLayers(ITestOutputHelper outputHelper)
{
    // Parallel execution is not a moral virtue. It is a scheduling decision.
    // These SetVariable steps are both Prepare work, so the planner can place
    // them in one layer instead of escorting each assignment through the building like fragile royalty.

    private readonly Timeline _timeline = Timeline.Create()
        .SetVariable("greeting", Var.Const("Good morning"))
            .Name("set greeting")
        .SetVariable("subject", Var.Const("test subject"))
            .Name("set subject")
        .Build();

    [Fact]
    public async Task Run()
    {
        TimelineRun run = await _timeline.SetupRun(outputHelper).RunAsync();

        run.EnsureRanToCompletion();
        run.Variable<string>("greeting").Should().Exist().And().Be("Good morning");
        run.Variable<string>("subject").Should().Exist().And().Be("test subject");

        // Run this with xUnit output open and the debug view should show both
        // SetVariable steps inside the same Prepare layer. The scheduler is not trying to impress you. It is just refusing to waste time.
    }
}

public class Parallel_DoNotParallelizeBarrier(ITestOutputHelper outputHelper)
{
    // Sometimes one step needs elbow room even inside a mergeable phase. That
    // is what DoNotParallelize is for: not panic, not superstition, just a very direct instruction that this step gets the hallway to itself.

    private readonly Timeline _timeline = Timeline.Create()
        .SetVariable("intro", Var.Const("Facility memo:"))
            .Name("set intro")
        .SetVariable("exclusive", Var.Const("Do not stand near the unstable science."))
            .Name("exclusive bulletin")
            .DoNotParallelize()
        .SetVariable("outro", Var.Const("You are now fully briefed."))
            .Name("set outro")
        .Build();

    [Fact]
    public async Task Run()
    {
        TimelineRun run = await _timeline.SetupRun(outputHelper).RunAsync();

        run.EnsureRanToCompletion();
        run.Variable<string>("intro").Should().Exist().And().Be("Facility memo:");
        run.Variable<string>("exclusive").Should().Exist().And().Be("Do not stand near the unstable science.");
        run.Variable<string>("outro").Should().Exist().And().Be("You are now fully briefed.");

        // The output should now split the Prepare work into multiple layers
        // around the exclusive step. That is the whole point. If every step demanded private seating, the planner would just become a queue with delusions of grandeur.
    }
}

public class Parallel_SetupArtifacts(ITestOutputHelper outputHelper)
{
    // Artifact setup is also scheduler-visible work. The artifact exists before
    // the run starts, but SetupArtifact is the point where the timeline makes it ready for use instead of just assuming the outside world behaved itself.

    private readonly Timeline _timeline = Timeline.Create()
        .SetupArtifact("alpha")
            .Name("setup alpha")
        .SetupArtifact("beta")
            .Name("setup beta")
        .Build();

    [Fact]
    public async Task Run()
    {
        string alphaPath = ParallelSamplePaths.UniqueFile("showroom-parallel-alpha");
        string betaPath = ParallelSamplePaths.UniqueFile("showroom-parallel-beta");

        TimelineRun run = await _timeline.SetupRun(outputHelper)
            .AddFileArtifact("alpha", alphaPath, "alpha ready")
            .AddFileArtifact("beta", betaPath, "beta ready")
            .RunAsync();

        run.EnsureRanToCompletion();
        run.FileArtifact("alpha").Utf8Text().Should().Be("alpha ready");
        run.FileArtifact("beta").Utf8Text().Should().Be("beta ready");

        // With output enabled, these setup steps show up as Prepare work just
        // like SetVariable did. Same scheduler, same stage planning, different kind of resource paperwork.
    }
}