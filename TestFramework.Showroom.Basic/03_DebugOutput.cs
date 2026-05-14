using TestFramework.Core.Timelines;
using TestFramework.Simple;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

public class DebugOutput(ITestOutputHelper outputHelper)
{
    // Give the run an output helper and it stops suffering in silence. That is
    // the entire bargain here. More visibility, less interpretive guessing, fewer speeches about "probably the environment."

    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(SimpleExt.Trigger.MessageBox("Hello from Test"))
        .Build();

    [Fact]
    public async Task Run()
    {
        var run = await this._timeline.SetupRun(outputHelper).RunAsync();
        run.EnsureRanToCompletion();

        // The useful part lands in the xUnit output stream: step order, timing,
        // and enough breadcrumbs to diagnose what happened without clairvoyance or ritual chanting.
    }
}