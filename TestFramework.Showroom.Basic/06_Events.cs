using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using TestFramework.LocalIO;
using TestFramework.Simple;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

//doc: Everything so far ran the moment the planner reached it. Events are the other case: the run has to
//doc: wait for the world to catch up. `WaitForEvent` is how, and it exists so that waiting does not turn
//doc: into a pile of hand-written polling loops, regret, and measurable caffeine abuse.
//doc:
//doc: You name the condition the world must satisfy. The framework does the waiting, reports the target it
//doc: actually resolved, and gives up on a deadline instead of hanging forever - every step carries a
//doc: timeout, ten minutes if nobody says otherwise, and `WithTimeOut(...)` is how you say otherwise.
//doc: Chapter 15 shows what the failure looks like when the condition never comes true.

//doc: The platform shim again, and this time it earned a footnote. Waiting for a file that is written by a
//doc: shell command means both halves have to survive being run by a test host, and two things in here
//doc: were learned the direct way.

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

//doc: The shape is: cause something, wait for the consequence, then work with it. The command sleeps five
//doc: seconds before writing the file, so the wait has real work to do and does not start feeling
//doc: ornamental - and the register step that follows can count on the file existing.

public class Events(ITestOutputHelper outputHelper)
{
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

    //doc: Two things in the panel are worth the detour. The four steps report four different phases -
    //doc: `Act` for the commands, `Observe` for the wait, `Materialize` for the register - and that is the
    //doc: planner's vocabulary, not decoration; chapter 13 is where it starts deciding what runs together.
    //doc:
    //doc: And the wait's input row carries the path it actually resolved, not the template you wrote. When
    //doc: a wait times out on a path you did not expect, that row is where you find out why. (On this page
    //doc: the path is replaced with a placeholder, so the same page is produced on any machine.)
}
