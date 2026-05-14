using TestFramework.Core.Timelines;
using TestFramework.Simple;

namespace TestFramework.Showroom.Basic;

public class MessageBoxTimeline
{
    // This chapter adds one actual trigger and nothing else. Deliberately.
    // The goal is to show the timeline shape with exactly one moving part, not
    // to drown a first lesson in twenty advanced options and a preventable identity crisis.

    /* Flow of a Timeline
     * -> First, the builder defines the structure between Timeline.Create() and Build().
     * -> Then a run executes that structure.
     * -> Then cleanup happens and the run becomes a result you can interrogate.
     */

    // The timeline is the blueprint. Each run is one fresh attempt to carry out
    // that blueprint without dragging leftovers from the previous attempt like contraband through customs.
    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(SimpleExt.Trigger.MessageBox("Hello from Test"))
        .Build();

    [Fact]
    public async Task Run()
    {
        TimelineRun run = await this._timeline.SetupRun().RunAsync(); // Fresh run. Fresh state. No mysterious residue on the walls.
        run.EnsureRanToCompletion();
    }
}