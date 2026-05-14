using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Assertions;
using TestFramework.Core.Variables;
using TestFramework.LocalIO;
using TestFramework.Simple;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

public class Variables(ITestOutputHelper outputHelper)
{
    // Variables are how one static timeline stops behaving like a cardboard cutout.
    // Same structure, different inputs, no duplicate timeline definitions every time a string gets ambitious.

    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(SimpleExt.Trigger.MessageBox(Var.Ref<string>("cmdCommand")))
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

public class Variables_Assert(ITestOutputHelper outputHelper)
{
    // Variables are not decoration. They are runtime data, and runtime data gets
    // inspected like everything else worth trusting. Confidence is not a data type.

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

public class Variables_Transforms(ITestOutputHelper outputHelper)
{
    // Variables can be transformed at the point of use. The source stays simple.
    // The consumer still gets exactly the shape it needs. Suspiciously efficient, but we allow it.

    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(SimpleExt.Trigger.MessageBox(Var.Ref<string>("cmdCommand").Transform(x => x + ". And it is even Transformed!")))
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

public class Variables_RunExclusively(ITestOutputHelper outputHelper)
{
    // Some steps deserve solitude. Mark them exclusive and the scheduler learns
    // that sharing time with other work is no longer an option. Think quarantine, but for concurrency.

    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(SimpleExt.Trigger.MessageBox(Var.Ref<string>("cmdCommand")))
        .RunExclusively()
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