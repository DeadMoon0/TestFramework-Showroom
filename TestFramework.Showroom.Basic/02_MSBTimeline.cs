using TestFramework.Core.Timelines;

namespace TestFramework.Showroom.Basic;

public class MSBTimeline
{
    // A simple sample to show how you can trigger stuff. In this case, it's a MSBox command. You can say "Hello" back. :]

    /* Flow of a Timeline
     * -> First, every step is ran. Defined by the Timeline builder — in other words, after "Timeline.Create()" and before ".Build()".
     * -> Then everything is cleaned up.
     * -> Done :)
     */

    // You first define a structure in the form of a timeline; every run will follow it but with its own twist.
    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(Simple.Simple.Trigger.MessageBox("Hello from Test"))
        .Build();

    [Fact]
    public async Task Run()
    {
        var run = await this._timeline.SetupRun().RunAsync(); // Every run is isolated and fully independent.
        run.EnsureRanToCompletion();
    }
}