using TestFramework.Core.Timelines;
using TestFramework.Simple;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

//doc: One argument. That is the entire difference between chapter 02 and this one, and it is the
//doc: single highest-value habit in the framework: hand `SetupRun` the xUnit output helper and the run
//doc: stops suffering in silence.

public class DebugOutput(ITestOutputHelper outputHelper)
{
    //doc: The trigger writes through the run logger, so `Hello from Test` arrives in the very output
    //doc: stream this chapter is about. The payload and the lesson are the same thing.

    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(SimpleExt.Trigger.Message("Hello from Test"))
        .Build();

    [Fact]
    public async Task Run()
    {
        var run = await this._timeline.SetupRun(outputHelper).RunAsync();
        run.EnsureRanToCompletion();
    }

    //doc: Open the panel below, because that report is what every later chapter is read through. It
    //doc: arrives in a fixed order, and the order is the point - it describes the plan before it
    //doc: describes the execution:
    //doc:
    //doc: - **Variables** and **Stage Plan** - what the run was given, and what it intends to do.
    //doc: - **Dependency Graph** - why the planner ordered the steps the way it did.
    //doc: - Then one block per stage, each with a **Flow Trace** and one box per step: its phase, its
    //doc:   layer, its declared inputs and observed outputs, its log per attempt, and its final state.
    //doc:
    //doc: Two things in there are worth noticing this early. Every run has a **Cleanup Stage** you did
    //doc: not write - here it tears down nothing, because this chapter owns nothing. And a step reports
    //doc: the values it actually resolved, not the templates you typed. Most of "the system is wrong"
    //doc: turns out to be "the input was not what I meant", and this is where that becomes visible.
}
