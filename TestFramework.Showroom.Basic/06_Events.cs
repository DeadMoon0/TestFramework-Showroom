using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using TestFrameworkLocalIO;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

public class Events(ITestOutputHelper outputHelper)
{
    // Wait—wait—wait. Ah, there you are. Great. You don't have to wait while someone fetches tea; let me do it for you.

    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(LocalIO.Trigger.Cmd(Var.Ref<string>("cmdCreate")))
        .WaitForEvent(LocalIO.Events.FileExists("outNew.txt"))
        //                           ^ Just pass in the correct event handler - this will wait until the event is resolved.
        .RegisterArtifact("newFile", LocalIO.Artifacts.FileRef("outNew.txt"))
        .Trigger(Simple.Simple.Trigger.MessageBox(Var.Ref<string>("cmdShow")))
        .Build();

    [Fact]
    public async Task Run()
    {
        var run = await this._timeline.SetupRun(outputHelper)
            .AddVariable("cmdCreate", "timeout /t 5 /nobreak >nul & echo Hello from the new Artifact >> outNew.txt")
            //                                    ^ Force a 5-second delay before the artifact is created.
            .AddVariable("cmdShow", "Hello from the new Artifact")
            .RunAsync();
        run.EnsureRanToCompletion();
    }
}