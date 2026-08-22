using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Assertions;
using TestFramework.Simple;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

//doc: A short chapter with one idea: order of interrogation. A completed run exposes every step result, so
//doc: execution is not a black box with a success flag glued to the top by somebody with confidence and no
//doc: receipts - but the two questions are still different questions, and they are asked in this order.
//doc:
//doc: 1. `EnsureRanToCompletion()` - did the run finish. If it did not, it throws with the failed steps
//doc:    attached, and nothing below it would have been worth reading anyway.
//doc: 2. `run.Step("hello").Should()…` - and was this particular thing true.
//doc:
//doc: Skip the first and you get the failure mode chapter 15 is built around: every assertion passes
//doc: against a run that never worked, because you asserted on the parts that did.

public class StepValidations(ITestOutputHelper outputHelper)
{
    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(SimpleExt.Trigger.Message("Hello from Test"))
            .Name("hello")
        .Build();

    [Fact]
    public async Task Run()
    {
        var run = await this._timeline.SetupRun(outputHelper).RunAsync();
        run.EnsureRanToCompletion(); // First verify the run completed, then interrogate the individual steps like a professional nuisance.

        run.Step("hello").Should().HaveCompleted();
    }
}
