using System.Diagnostics;
using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using TestFrameworkLocalIO;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

public class IOContracts_StepDeclaredIO(ITestOutputHelper outputHelper)
{
    // Steps declare their own IO internally via DeclareIO().
    // The IOContractValidator checks, before execution, that every required
    // input is either produced by a prior step or supplied externally via AddVariable().
    // Users do not declare IO in the builder — they only supply variable values.

    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(LocalIO.Trigger.Cmd(Var.Ref<string>("cmdCommand")))
        // CmdTrigger.DeclareIO → Inputs: "cmdCommand" (string)
        //                      → Outputs: "out" (int, via DoesReturn)
        .SetVariable("ExitCode", Var.Ref<int>("out"))
        // SetVariableStep.DeclareIO → Inputs: "out" → Outputs: "ExitCode"
        .Build();

    [Fact]
    public async Task Run()
    {
        var run = await this._timeline.SetupRun(outputHelper)
            .AddVariable("cmdCommand", "echo hello")   // satisfies CmdTrigger's declared input
            .RunAsync();
        run.EnsureRanToCompletion();
        Debug.Assert(run.VariableStore.GetVariable<int>("ExitCode") == 0);
    }
}

public class IOContracts_RunExclusively(ITestOutputHelper outputHelper)
{
    // .RunExclusively() is the only user-facing step option remaining.
    // All IO is handled by each step's own DeclareIO.

    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(LocalIO.Trigger.Cmd(Var.Ref<string>("cmdCommand")))
        .RunExclusively()
        .Build();

    [Fact]
    public async Task Run()
    {
        var run = await this._timeline.SetupRun(outputHelper)
            .AddVariable("cmdCommand", "echo exclusive")
            .RunAsync();
        run.EnsureRanToCompletion();
    }
}

