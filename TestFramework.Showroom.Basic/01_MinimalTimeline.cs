using TestFramework.Core.Timelines;

namespace TestFramework.Showroom.Basic;

public class MinimalTimeline
{
    // This is the smallest legal timeline. Strip away one more thing and you do
    // not have a framework example anymore, you have an empty opinion.
    // Those are cheaper to produce, but much harder to execute.

    private readonly Timeline _timeline = Timeline.Create()
        .Build();

    [Fact]
    public async Task Run()
    {
        // Build once, run once, verify completion. Every larger example is just
        // this skeleton wearing more equipment and making bigger promises.
        var run = await this._timeline.SetupRun().RunAsync();
        run.EnsureRanToCompletion();
    }
}