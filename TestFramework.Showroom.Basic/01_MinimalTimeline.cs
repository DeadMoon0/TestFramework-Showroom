using TestFramework.Core.Timelines;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

//doc: Every TestFramework test has the same two halves: a timeline you build once, and a run that
//doc: executes it. This chapter is the smallest legal version of both. Strip away one more thing and
//doc: you do not have a framework example anymore, you have an empty opinion.
//doc:
//doc: The output helper comes in through the constructor. It is optional - a test that leaves it out
//doc: still passes - but a run you cannot read is a run you cannot debug, so every chapter takes it.

public class MinimalTimeline(ITestOutputHelper output)
{
    //doc: The timeline itself. `Timeline.Create()` opens the builder and `Build()` freezes it. Nothing
    //doc: has run yet: a timeline is a plan, and this one plans nothing at all.

    private readonly Timeline _timeline = Timeline.Create()
        .Build();

    //doc: Now the run. `SetupRun` turns the frozen plan into something executable, `RunAsync()`
    //doc: executes it, and `EnsureRanToCompletion()` is the assertion - it throws unless every stage
    //doc: and step finished.
    //doc:
    //doc: Note that the plan is a field and the run is local. That split is the whole model: one
    //doc: timeline, many runs, each isolated from every other.

    [Fact]
    public async Task Run()
    {
        // Build once, run once, verify completion. Every larger example is just
        // this skeleton wearing more equipment and making bigger promises.
        var run = await this._timeline.SetupRun(output).RunAsync();
        run.EnsureRanToCompletion();
    }

    //doc: The output below is worth reading even with nothing to execute: the stage exists, it is
    //doc: empty, and it completed. Every chapter after this one puts steps into that same stage.
}
