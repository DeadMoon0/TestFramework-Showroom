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
    // Events are how the run waits for the world to catch up without turning the
    // test into a pile of hand-written polling loops, regret, and measurable caffeine abuse.

    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(LocalIO.Trigger.Cmd(Var.Ref<string>("cmdCreate"), Var.Ref<string>("cwd")))
        .WaitForEvent(LocalIO.Events.FileExists(Var.Ref<string>("artifactPath")))
        //                           ^ Name the condition the world must satisfy,
        //                             then let the framework do the waiting while you pretend patience was always the plan.
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
                //                                    ^ Delay on purpose so the
                //                                      wait has real work to do and does not start feeling ornamental.
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