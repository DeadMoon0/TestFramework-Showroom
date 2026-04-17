using System.Text;
using TestFramework.Azure.Extensions;
using TestFramework.Config;
using TestFramework.Core.Timelines;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Azure;

// ══════════════════════════════════════════════════════════════════════════════
//  CLOUD INFRASTRUCTURE DIVISION — PARTICIPANT ORIENTATION MODULE A1
//  "Blob Storage: Putting Your Bytes Somewhere Other Than Your Desk"
//
//  Congratulations on reaching Module A1. Statistically, most participants
//  make it this far. We're very proud of those statistics. We generated them
//  ourselves.
//
//  In this module, you will learn to UPLOAD binary data to a remote storage
//  location, VERIFY it arrived, and — this is the important part — CLEAN IT UP
//  automatically when the test ends.
//
//  The cleanup is not optional. We tried optional cleanup once.
//  The cost report was... colorful.
// ══════════════════════════════════════════════════════════════════════════════

public class BlobStorage_BasicUpload(ITestOutputHelper outputHelper)
{
    // STEP ONE: Upload something. Anything.
    // We're not judging the contents. We're definitely not reading the contents.
    // (We have a system that reads the contents. It's for quality assurance.)

    private static readonly ConfigInstance _config = ConfigInstance.FromJsonFile("local.testSettings.json")
        //                                                                         ^ Points to local.testSettings.json in the output directory.
        //                                                                           Fill in your connection strings there.
        //                                                                           Guard it like it's the only copy of your thesis. Because it is.
        .Build();

    private static readonly Timeline _timeline = Timeline.Create()
        .SetupArtifact("blob")
        // ^ The framework will upload this blob before your test runs and DELETE it when it finishes.
        //   You do not need to remember. We remember for you.
        //   Some might call that unsettling. We call it "managed lifecycle."
        .Build();

    [Fact]
    public async Task Run()
    {
        var configSub = _config.SetupSubInstance()
            .LoadAzureConfig()  // Reads CosmosDb, Storage, ServiceBus, SQL — all from your settings file.
            .Build();

        var run = await _timeline.SetupRun(configSub.BuildServiceProvider(), outputHelper)
            .AddBlobArtifact(
                "blob",                                    // artifact name — used later to assert against
                "MainStorage",                             // identifier from local.testSettings.json
                "showroom/greetings.txt",                  // path inside the container
                Encoding.UTF8.GetBytes("Hello, Blob!"))    // the actual bytes. simple. elegant. bytes.
            .RunAsync();

        run.EnsureRanToCompletion();
        // ^ If anything went wrong, this throws. Loudly. With a helpful message.
        //   We've found that "helpful" is subjective. But we tried.
    }
}

public class BlobStorage_WithMetadata(ITestOutputHelper outputHelper)
{
    // STEP TWO: Metadata. The data about your data.
    // Think of it as a sticky note on your bytes.
    // Except the sticky note survives a server reboot.
    // Your actual sticky notes do not. We did a study.

    private static readonly ConfigInstance _config = ConfigInstance.FromJsonFile("local.testSettings.json")
        .Build();

    private static readonly Timeline _timeline = Timeline.Create()
        .SetupArtifact("blob")
        .Build();

    [Fact]
    public async Task Run()
    {
        var configSub = _config.SetupSubInstance().LoadAzureConfig().Build();

        var run = await _timeline.SetupRun(configSub.BuildServiceProvider(), outputHelper)
            .AddBlobArtifact(
                "blob",
                "MainStorage",
                "showroom/tagged-report.txt",
                Encoding.UTF8.GetBytes("Quarterly synergy alignment achieved."),
                new Dictionary<string, string>
                {
                    ["department"] = "showroom",     // tag it
                    ["status"]     = "experimental", // classify it
                    ["clearance"]  = "orange",       // nobody ask what orange means
                })
            .RunAsync();

        run.EnsureRanToCompletion();

        // Assert the blob arrived intact and wearing its metadata.
        run.BlobArtifact("blob").Should().Exist();

        run.BlobArtifact("blob")
            .Select(d => d.MetaData["department"])
            .Should().Be("showroom");
        //              ^ Fluent assertions. Read left to right. Fail with a clear message.
        //                Revolutionary technology. Invented some time ago. We're catching up.

        run.BlobArtifact("blob")
            .Select(d => Encoding.UTF8.GetString(d.Data))
            .Should().Be("Quarterly synergy alignment achieved.");
        // ^ Confirm the bytes you put in are the bytes that came out.
        //   This step feels obvious. It is also the step most people skip.
        //   Those people file a lot of bug reports.
    }
}
