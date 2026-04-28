using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Assertions;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

public class StepValidations(ITestOutputHelper outputHelper)
{
    // Attention: for real tinkerers only. ... You still there? ... Great — I see =]. You have access to every step and its results and more. Just tinker around.

    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(Simple.Simple.Trigger.MessageBox("Hello from Test")).Name("hello")
        .Build();

    [Fact]
    public async Task Run()
    {
        var run = await this._timeline.SetupRun(outputHelper).RunAsync();
        run.EnsureRanToCompletion(); // This will ensure every stage and every step is completed.

        run.Step("hello").Should().HaveCompleted();
    }
}