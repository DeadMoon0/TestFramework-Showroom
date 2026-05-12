using TestFramework.Core.Exceptions;
using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Assertions;
using TestFramework.Core.Variables;
using TestFrameworkLocalIO;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

public class IOContracts_StepDeclaredIO(ITestOutputHelper outputHelper)
{
    // IO contracts exist so the run can reject nonsense before external work
    // starts. Steps declare what they need and what they produce. The validator
    // checks the plan before the world gets disturbed and before somebody blames the network out of habit.

    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(LocalIO.Trigger.Cmd(Var.Ref<string>("cmdCommand")))
        // Cmd declares it needs cmdCommand and produces out. Very honest of it.
        .SetVariable("ExitCode", Var.Ref<int>("out"))
        // SetVariable then turns that output into a named run variable, because "out" is technically correct and socially terrible.
        .Build();

    [Fact]
    public async Task Run()
    {
        var run = await this._timeline.SetupRun(outputHelper)
            .AddVariable("cmdCommand", "echo hello")   // Supply the declared input and the plan becomes valid. Amazing what standards can do.
            .RunAsync();
        run.EnsureRanToCompletion();

        run.Variable<int>("ExitCode").Should().Exist().And().Be(0);
    }
}

public class IOContracts_RunExclusively(ITestOutputHelper outputHelper)
{
    // IO declaration and execution policy are different jobs. Steps declare IO.
    // The builder still gets to say whether a step must run exclusively. Good policy and good paperwork are not the same department.

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

public class IOContracts_MissingInputDiagnosis(ITestOutputHelper outputHelper)
{
    // Missing inputs should fail during planning, before any command, file, or
    // remote system gets touched. Early rejection is the whole advantage and much cheaper than dramatic runtime confusion.

    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(LocalIO.Trigger.Cmd(Var.Ref<string>("cmdCommand")))
        .Build();

    [Fact]
    public async Task Run_ShowsMissingVariableNameInValidationFailure()
    {
        IOContractViolationException exception = await Assert.ThrowsAsync<IOContractViolationException>(() =>
            this._timeline.SetupRun(outputHelper).RunAsync());

        Assert.Contains("cmdCommand", exception.Message);
    }
}

