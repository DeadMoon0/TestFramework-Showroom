using System.Runtime.Versioning;
using TestFramework.Core.Timelines;
using TestFramework.Simple;

namespace TestFramework.Showroom.Basic;

public class InteractiveTriggers
{
    // Every earlier chapter used SimpleExt.Trigger.Message(...), which writes to the run logger.
    // There is a second trigger with the same shape that opens a real Windows message box, and
    // this chapter is where it belongs: after you already know what a timeline is, rather than
    // in chapter 02 where a modal dialog would be the very first thing the framework ever did to you.

    // MessageBox is genuinely useful when you are watching a run by hand and want it to stop and
    // wait for you at a chosen point. It is exactly as useful in an unattended run as a doorbell
    // is in an empty house: on Windows the suite blocks until someone clicks OK, and everywhere
    // else the P/Invoke has nothing to call.

    // Hence the Skip. Remove it, run this one test on Windows, dismiss the dialog, put it back.

    [Fact(Skip = "Interactive: run manually on Windows.")]
    [SupportedOSPlatform("windows")]
    public async Task Run_ShowsARealDialog()
    {
        Timeline timeline = Timeline.Create()
            .Trigger(SimpleExt.Trigger.MessageBox("Hello from Test", "Showroom"))
            .Build();

        TimelineRun run = await timeline.SetupRun().RunAsync();
        run.EnsureRanToCompletion();
    }
}
