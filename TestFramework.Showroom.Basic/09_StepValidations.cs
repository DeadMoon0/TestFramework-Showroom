using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Assertions;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

public class StepValidations(ITestOutputHelper outputHelper)
{
    // Every completed run exposes its step results. That means you can inspect
    // exactly what happened instead of treating execution as a black box with a
    // final success flag glued to the top by somebody with confidence and no receipts.

    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(Simple.Simple.Trigger.MessageBox("Hello from Test")).Name("hello")
        .Build();

    [Fact]
    public async Task Run()
    {
        var run = await this._timeline.SetupRun(outputHelper).RunAsync();
        run.EnsureRanToCompletion(); // First verify the run completed, then interrogate the individual steps like a professional nuisance.

        run.Step("hello").Should().HaveCompleted();
    }
}