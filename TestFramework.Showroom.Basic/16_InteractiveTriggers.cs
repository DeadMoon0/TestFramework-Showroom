using System.Runtime.Versioning;
using TestFramework.Core.Timelines;
using TestFramework.Simple;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

//doc: Every earlier chapter used `SimpleExt.Trigger.Message(...)`, which writes to the run logger. There is
//doc: a second trigger with the same shape that opens a real Windows message box, and this chapter is where
//doc: it belongs: after you already know what a timeline is, rather than in chapter 02 where a modal dialog
//doc: would be the very first thing the framework ever did to you.
//doc:
//doc: `MessageBox` is genuinely useful when you are watching a run by hand and want it to stop and wait for
//doc: you at a chosen point. It is exactly as useful in an unattended run as a doorbell is in an empty
//doc: house: on Windows the suite blocks until someone clicks OK, and everywhere else the P/Invoke has
//doc: nothing to call.
//doc:
//doc: Hence the `Skip`, which is why the panel below reports the chapter as skipped and quotes that reason
//doc: back at you. Remove it, run this one test on Windows, dismiss the dialog, put it back.
//doc:
//doc: Worth taking the general lesson too: a test that cannot run unattended should say so in the runner
//doc: rather than in a comment nobody reads. `Skip` with a sentence, or the lane's own `[DockerFact]`, both
//doc: turn "this failed on the build server" into "this was never going to run here, and here is why".

public class InteractiveTriggers(ITestOutputHelper output)
{
    [Fact(Skip = "Interactive: run manually on Windows.")]
    [SupportedOSPlatform("windows")]
    public async Task Run_ShowsARealDialog()
    {
        Timeline timeline = Timeline.Create()
            .Trigger(SimpleExt.Trigger.MessageBox("Hello from Test", "Showroom"))
            .Build();

        TimelineRun run = await timeline.SetupRun(output).RunAsync();
        run.EnsureRanToCompletion();
    }
}
