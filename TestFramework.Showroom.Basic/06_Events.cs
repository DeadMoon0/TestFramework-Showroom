using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using TestFrameworkLocalIO;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

file static class EventSampleTempDirectories
{
    public static string Create()
    {
        string path = Path.Combine(Path.GetTempPath(), $"showroom-events-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}

public class Events(ITestOutputHelper outputHelper)
{
    // Wait—wait—wait. Ah, there you are. Great. You don't have to wait while someone fetches tea; let me do it for you.

    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(LocalIO.Trigger.Cmd(Var.Ref<string>("cmdCreate"), Var.Ref<string>("cwd")))
        .WaitForEvent(LocalIO.Events.FileExists(Var.Ref<string>("artifactPath")))
        //                           ^ Just pass in the correct event handler - this will wait until the event is resolved.
        .RegisterArtifact("newFile", LocalIO.Artifacts.FileRef(Var.Ref<string>("artifactPath")))
        .Trigger(Simple.Simple.Trigger.MessageBox(Var.Ref<string>("cmdShow")))
        .Build();

    [Fact]
    public async Task Run()
    {
        string tempDir = EventSampleTempDirectories.Create();
        string artifactPath = Path.Combine(tempDir, "outNew.txt");

        try
        {
            var run = await this._timeline.SetupRun(outputHelper)
                .AddVariable("cmdCreate", "timeout /t 5 /nobreak >nul & echo Hello from the new Artifact >> outNew.txt")
                //                                    ^ Force a 5-second delay before the artifact is created.
                .AddVariable("cmdShow", "Hello from the new Artifact")
                .AddVariable("cwd", tempDir)
                .AddVariable("artifactPath", artifactPath)
                .RunAsync();
            run.EnsureRanToCompletion();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}