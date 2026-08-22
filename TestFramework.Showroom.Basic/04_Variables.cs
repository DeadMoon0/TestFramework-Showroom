using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Assertions;
using TestFramework.Core.Variables;
using TestFramework.LocalIO;
using TestFramework.Simple;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

//doc: A timeline is built once and held in a field. That is only useful if the same structure can run
//doc: against different inputs, and variables are how. `Var.Ref<T>("name")` puts a hole in the plan;
//doc: `AddVariable` fills it at setup. One structure, many inputs, no duplicated timeline definitions
//doc: every time a string gets ambitious.
//doc:
//doc: Four classes follow, in increasing order of what they ask of the mechanism: reference it, assert
//doc: on what a step produced, transform it at the point of use, and opt a step out of parallelism.

public class Variables(ITestOutputHelper outputHelper)
{
    //doc: The plainest form. The timeline names `cmdCommand` without knowing anything about it - not its
    //doc: value, not where it comes from, only that it must be a string and must be there by the time
    //doc: the step runs.

    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(SimpleExt.Trigger.Message(Var.Ref<string>("cmdCommand")))
        .Build();

    [Fact]
    public async Task Run()
    {
        var run = await this._timeline.SetupRun(outputHelper)
            .AddVariable("cmdCommand", "Hello from Test via Var")
            .RunAsync();
        run.EnsureRanToCompletion();
    }
}

//doc: Variables travel in both directions. A step can produce one as well as consume one, and a
//doc: produced variable is run state you can interrogate afterwards - confidence is not a data type.
//doc:
//doc: `LocalIOExt.Trigger.Cmd` runs a command; `GetExitCode("CmdExitCode")` names the exit code it
//doc: produced so the test can assert on it. In the report below, that pairing shows up as the step's
//doc: declared input and its observed output, side by side.

public class Variables_Assert(ITestOutputHelper outputHelper)
{
    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(LocalIOExt.Trigger.Cmd(Var.Ref<string>("cmdCommand")))
            .GetExitCode("CmdExitCode")
        .Build();

    [Fact]
    public async Task Run()
    {
        var run = await this._timeline.SetupRun(outputHelper)
            .AddVariable("cmdCommand", "echo Hello from Test via Var")
            .RunAsync();
        run.EnsureRanToCompletion();

        run.Variable<int>("CmdExitCode").Should().Exist().And().Be(0);
        //               ^ If a step produced it, the run can surface it for
        //                 assertions without making you rummage through internals with a miner's lamp.
    }
}

//doc: `Transform` shapes a value where it is used rather than where it is supplied. The source stays
//doc: one plain string; the consumer still gets exactly the shape it needs. Suspiciously efficient,
//doc: but we allow it.

public class Variables_Transforms(ITestOutputHelper outputHelper)
{
    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(SimpleExt.Trigger.Message(Var.Ref<string>("cmdCommand").Transform(x => x + ". And it is even Transformed!")))
        .Build();

    [Fact]
    public async Task Run()
    {
        var run = await this._timeline.SetupRun(outputHelper)
            .AddVariable("cmdCommand", "Hello from Test via Var")
            .RunAsync();
        run.EnsureRanToCompletion();
    }
}

//doc: Last, the one modifier that belongs in this chapter rather than in chapter 13: some steps deserve
//doc: solitude. `DoNotParallelize()` tells the planner this step gets a layer to itself - think
//doc: quarantine, but for concurrency.
//doc:
//doc: With one step there is nothing to be exclusive of, which is exactly why it is shown here: the
//doc: modifier is a property of a step, not a property of a busy timeline. Chapter 13 puts it in a
//doc: timeline where the layers visibly split around it.

public class Variables_DoNotParallelize(ITestOutputHelper outputHelper)
{
    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(SimpleExt.Trigger.Message(Var.Ref<string>("cmdCommand")))
            .DoNotParallelize()
        .Build();

    [Fact]
    public async Task Run()
    {
        var run = await this._timeline.SetupRun(outputHelper)
            .AddVariable("cmdCommand", "Hello — I run alone!")
            .RunAsync();
        run.EnsureRanToCompletion();
    }
}
