using TestFramework.Core.Exceptions;
using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Assertions;
using TestFramework.Core.Variables;
using TestFramework.LocalIO;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

//doc: Every step declares what it needs and what it produces, and the run validates the whole plan against
//doc: what you supplied *before* it starts. That is the IO contract, and it is the cheapest error message
//doc: the framework has: a missing input is a sentence, not a half-finished run and somebody blaming the
//doc: network out of habit.
//doc:
//doc: Three chapters: what a declaration looks like, that declaring IO and choosing execution policy are
//doc: separate decisions, and what the rejection reads like when an input is simply not there.

//doc: `LocalIOExt.Trigger.Cmd` declares one required input, the command, and a second when a working
//doc: directory is given. That is all `DeclareIO` does - it is about the plan.
//doc:
//doc: `GetExitCode("ExitCode")` is a different mechanism: a result binding. It pulls a property off the
//doc: step's result and files it under a name, which is what makes it assertable afterwards. In the panel
//doc: below the two appear side by side as the step's declared input and observed output - and `ExitCode`
//doc: is a name you chose, because `ExitCode` on a result object is technically correct and socially
//doc: terrible to ask a test to reach for.

public class IOContracts_StepDeclaredIO(ITestOutputHelper outputHelper)
{
    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(LocalIOExt.Trigger.Cmd(Var.Ref<string>("cmdCommand")))
            .GetExitCode("ExitCode")
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

//doc: Two different jobs, easily confused. The step declares its IO; the builder decides how the step is
//doc: allowed to run. `DoNotParallelize()` is the second kind, and it changes nothing about the contract -
//doc: good policy and good paperwork are not the same department.

public class IOContracts_DoNotParallelize(ITestOutputHelper outputHelper)
{
    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(LocalIOExt.Trigger.Cmd(Var.Ref<string>("cmdCommand")))
            .DoNotParallelize()
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

//doc: And the failure. The same timeline, run with nothing supplied: `RunAsync()` throws
//doc: `IOContractViolationException`, and the message names `cmdCommand` - the variable, not the step, not
//doc: a stack trace to work backwards from.
//doc:
//doc: This is the one chapter whose output panel is empty on purpose. There is no report because there was
//doc: no run: the plan was rejected before a single command was executed, no file was touched and no remote
//doc: system was disturbed. Early rejection is the whole advantage, and it is much cheaper than dramatic
//doc: runtime confusion. Chapter 15 shows the same failure with the framework's formatted guidance around
//doc: it.

public class IOContracts_MissingInputDiagnosis(ITestOutputHelper outputHelper)
{
    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(LocalIOExt.Trigger.Cmd(Var.Ref<string>("cmdCommand")))
        .Build();

    [Fact]
    public async Task Run_ShowsMissingVariableNameInValidationFailure()
    {
        IOContractViolationException exception = await Assert.ThrowsAsync<IOContractViolationException>(() =>
            this._timeline.SetupRun(outputHelper).RunAsync());

        Assert.Contains("cmdCommand", exception.Message);
    }
}
