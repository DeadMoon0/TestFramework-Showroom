using System.Diagnostics;
using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using TestFrameworkLocalIO;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

public class Variables(ITestOutputHelper outputHelper)
{
    // To add variants to each run, you can use variables. This is how! BB static and hello variable-unpredictability.

    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(Simple.Simple.Trigger.MessageBox(Var.Ref<string>("cmdCommand")))
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
    // Assertions are nice - and useful for variables too. Assert your dreams!

    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(LocalIO.Trigger.Cmd(Var.Ref<string>("cmdCommand")))
        .SetVariable("CmdExitCode", Var.Ref<int>("out"))
        .Build();

    [Fact]
    public async Task Run()
    {
        var run = await this._timeline.SetupRun(outputHelper)
            .AddVariable("cmdCommand", "echo Hello from Test via Var")
            .RunAsync();
        run.EnsureRanToCompletion();

        Debug.Assert(run.VariableStore.GetVariable<int>("CmdExitCode") == 0);
        //               ^ Every variable is stored in the VariableStore. Get and assert!
    }
}

public class Variables_Transforms(ITestOutputHelper outputHelper)
{
    // If the variable's version is too boring: first, let it be — it's doing its best. Then make it better, stronger, MOREEEEEEEEEE.

    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(Simple.Simple.Trigger.MessageBox(Var.Ref<string>("cmdCommand").Transform(x => x + ". And it is even Transformed!")))
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
    // Mark a step with .RunExclusively() so the future parallel scheduler knows
    // it must never run concurrently with any other step.

    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(Simple.Simple.Trigger.MessageBox(Var.Ref<string>("cmdCommand")))
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