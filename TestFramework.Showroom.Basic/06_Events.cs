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

    // This chapter is about waiting, not about shells, so the platform differences live here.
    //
    // Two things had to change. The `&` that used to join the two commands is a cmd separator, but
    // means "run this in the background" to a Unix shell; `&&` means "then" in both. And Windows'
    // `timeout` refuses to run at all when the console is redirected — which is every test host —
    // so it printed an error and, once joined by `&&`, the file was never written and the wait
    // never ended. `ping -n {N+1} 127.0.0.1` is the delay that survives a redirected console.
    public static string Sleep(int seconds)
        => OperatingSystem.IsWindows() ? $"ping -n {seconds + 1} 127.0.0.1 >nul" : $"sleep {seconds}";

    public static string AppendLine(string text, string file)
        => OperatingSystem.IsWindows() ? $"echo {text}>> {file}" : $"printf '%s\\n' '{text}' >> {file}";
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
        .Trigger(SimpleExt.Trigger.Message(Var.Ref<string>("cmdShow")))
        .Build();

    [Fact]
    public async Task Run()
    {
        string artifactPath = EventSamplePaths.UniqueFile("showroom-events");
        string artifactFileName = Path.GetFileName(artifactPath);

        var run = await this._timeline.SetupRun(outputHelper)
            .AddVariable("cmdCreate", $"{EventSamplePaths.Sleep(5)} && {EventSamplePaths.AppendLine("Hello from the new Artifact", artifactFileName)}")
            //                                            ^ Delay on purpose so the
            //                                              wait has real work to do and does not start feeling ornamental.
            .AddVariable("cmdShow", "Hello from the new Artifact")
            .AddVariable("cwd", EventSamplePaths.BuildOutput)
            .AddVariable("artifactPath", artifactPath)
            .RunAsync();
        run.EnsureRanToCompletion();
    }
}