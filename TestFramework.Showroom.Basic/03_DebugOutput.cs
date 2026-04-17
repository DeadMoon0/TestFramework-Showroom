using TestFramework.Core.Timelines;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

public class DebugOutput(ITestOutputHelper outputHelper)
{
    // For your debugging pleasure, I will log every step I take (pun intended). For this, just give me the ITestOutputHelper and let's go...

    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(Simple.Simple.Trigger.MessageBox("Hello from Test"))
        .Build();

    [Fact]
    public async Task Run()
    {
        var run = await this._timeline.SetupRun(outputHelper).RunAsync();
        run.EnsureRanToCompletion(); //    ^ There :]

        // Results will show in XUnit Output Page
    }
}