using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using TestFramework.LocalIO;
using TestFramework.Simple;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

file static class EventSamplePaths
{
    public static string BuildOutput => AppContext.BaseDirectory;

    public static string UniqueFile(string prefix)
        => Path.Combine(BuildOutput, $"{prefix}-{Guid.NewGuid():N}.txt");
}

public class Events(ITestOutputHelper outputHelper)
{
    // Events are how the run waits for the world to catch up without turning the
    // test into a pile of hand-written polling loops, regret, and measurable caffeine abuse.

    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(LocalIOExt.Trigger.Cmd(Var.Ref<string>("cmdCreate"), Var.Ref<string>("cwd")))
        .WaitForEvent(LocalIOExt.Events.FileExists(Var.Ref<string>("artifactPath")))
        //                           ^ Name the condition the world must satisfy,
        //                             then let the framework do the waiting while you pretend patience was always the plan.
        .RegisterArtifact("newFile", LocalIOExt.Artifacts.FileRef(Var.Ref<string>("artifactPath")))
        .Trigger(SimpleExt.Trigger.MessageBox(Var.Ref<string>("cmdShow")))
        .Build();

    [Fact]
    public async Task Run()
    {
        string artifactPath = EventSamplePaths.UniqueFile("showroom-events");
        string artifactFileName = Path.GetFileName(artifactPath);

        var run = await this._timeline.SetupRun(outputHelper)
            .AddVariable("cmdCreate", $"timeout /t 5 /nobreak >nul & echo Hello from the new Artifact >> {artifactFileName}")
            //                                    ^ Delay on purpose so the
            //                                      wait has real work to do and does not start feeling ornamental.
            .AddVariable("cmdShow", "Hello from the new Artifact")
            .AddVariable("cwd", EventSamplePaths.BuildOutput)
            .AddVariable("artifactPath", artifactPath)
            .RunAsync();
        run.EnsureRanToCompletion();
    }
}