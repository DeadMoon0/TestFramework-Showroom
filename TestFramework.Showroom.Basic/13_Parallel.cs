using TestFramework.Core.Artifacts;
using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Assertions;
using TestFramework.Core.Variables;
using TestFramework.LocalIO;
using TestFramework.Simple;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

//doc: Parallel execution is not a moral virtue, and it is not something you opt into. It is a scheduling
//doc: decision the planner makes from the frozen plan, and the run report shows you what it decided.
//doc:
//doc: Two authored steps share a **layer** only when all four of these hold:
//doc:
//doc: 1. They are in the same phase, and that phase is mergeable. `Prepare` and `Materialize` are;
//doc:    `Act` and `Observe` are not.
//doc: 2. Their declared IO does not conflict.
//doc: 3. Neither is marked `DoNotParallelize()`.
//doc: 4. They do not share a serialised setup resource - an artifact type can declare that its setup runs
//doc:    one at a time, keyed by what it touches, which is why seeding two rows into one database is
//doc:    serialised while seeding into two databases is not.
//doc:
//doc: Fail any one and they run in order. The reason `Act` and `Observe` never merge is that test intent
//doc: lives in their ordering: if two `Act` steps could overlap, "create the order, then cancel it" would
//doc: stop meaning anything specific. `Prepare` and `Materialize` are the phases where the free
//doc: parallelism is, because assigning a variable and registering a result are order-independent by
//doc: nature.
//doc:
//doc: Read the three panels below for the header line on each stage: `steps: N | layers: N | peak
//doc: parallel: N`. That line is this whole chapter, measured.

//doc: The plumbing, one more time, and smaller this time - nothing here is about shells.

file static class ParallelSamplePaths
{
    public static string BuildOutput => AppContext.BaseDirectory;

    public static string UniqueFile(string prefix)
        => Path.Combine(BuildOutput, $"{prefix}-{Guid.NewGuid():N}.txt");
}

//doc: Two `SetVariable` steps, both `Prepare`, no conflicting IO, neither exclusive. So: **2 steps, 1
//doc: layer, peak parallel 2** - both report `Layer: L0`. The scheduler is not trying to impress you, it
//doc: is refusing to escort each assignment through the building like fragile royalty.

public class Parallel_SetVariablePrepareLayers(ITestOutputHelper outputHelper)
{
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

//doc: Now the same shape with one step opted out. `DoNotParallelize()` makes a step a barrier inside its
//doc: own phase, and the arithmetic is worth noticing: three steps become **three layers, peak parallel 1**.
//doc: Not two. The exclusive step cannot share with the step before it or the step after it, so a single
//doc: barrier in the middle serialises the lot.
//doc:
//doc: Reach for it when a step touches something the IO contract cannot express - a process-wide setting, a
//doc: shared file, an environment variable. If every step demanded private seating, the planner would just
//doc: become a queue with delusions of grandeur.

public class Parallel_DoNotParallelizeBarrier(ITestOutputHelper outputHelper)
{
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

//doc: Last, the same rules applied to something that is not a variable assignment. `SetupArtifact` is
//doc: `Prepare` work too, so two of them merge exactly like the two assignments did - **2 steps, 1 layer,
//doc: peak parallel 2**. Same scheduler, same stage planning, a different kind of resource paperwork.
//doc:
//doc: This is where rule 4 would show up if the artifacts were rows in one database. Two local files share
//doc: nothing, so they do not serialise; two SQL rows in one database would.

public class Parallel_SetupArtifacts(ITestOutputHelper outputHelper)
{
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

    //doc: One consequence worth carrying out of this chapter: a parallel layer's log lines can appear in a
    //doc: different order from run to run. That is not a defect in the report, it is what happened. If you
    //doc: need a fixed order, say so with `DoNotParallelize()` rather than hoping.
}
