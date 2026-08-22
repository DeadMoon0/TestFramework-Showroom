using TestFramework.Core.Timelines;
using TestFramework.Simple;

namespace TestFramework.Showroom.Basic;

//doc: Chapter 01 ran a timeline with nothing in it. This one adds a single moving part - one trigger -
//doc: and nothing else. Deliberately. The shape of a timeline is easiest to see when exactly one thing
//doc: is happening inside it, rather than twenty advanced options and a preventable identity crisis.
//doc:
//doc: Every timeline has the same three beats:
//doc:
//doc: 1. The builder defines the structure, between `Timeline.Create()` and `Build()`.
//doc: 2. A run executes that structure.
//doc: 3. Cleanup happens, and the run becomes a result you can interrogate.

public class MessageTimeline
{
    //doc: The timeline is the blueprint, and `SimpleExt.Trigger.Message` is the smallest thing that can
    //doc: be written into one: a step that logs a line. It needs nothing from your machine, which is why
    //doc: the first two chapters use it instead of something that could fail for its own reasons.

    // The timeline is the blueprint. Each run is one fresh attempt to carry out
    // that blueprint without dragging leftovers from the previous attempt like contraband through customs.
    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(SimpleExt.Trigger.Message("Hello from Test"))
        .Build();

    //doc: This run is set up without an output helper, and that is the one thing to notice here. The
    //doc: message is written to the run logger either way, but with nowhere to forward it the output
    //doc: panel below is empty - the run happened, and you cannot see any of it.
    //doc:
    //doc: Chapter 03 is that missing line, and every chapter after it takes the helper.

    [Fact]
    public async Task Run()
    {
        TimelineRun run = await this._timeline.SetupRun().RunAsync(); // Fresh run. Fresh state. No mysterious residue on the walls.
        run.EnsureRanToCompletion();
    }
}
