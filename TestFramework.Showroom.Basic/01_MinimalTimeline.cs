using TestFramework.Core.Timelines;

namespace TestFramework.Showroom.Basic;

public class MinimalTimeline
{
    // The minimal timeline. Any less and you can remove the import.

    private readonly Timeline _timeline = Timeline.Create()
        .Build();

    [Fact]
    public async Task Run()
    {
        var run = await this._timeline.SetupRun().RunAsync();
        run.EnsureRanToCompletion();
    }
}