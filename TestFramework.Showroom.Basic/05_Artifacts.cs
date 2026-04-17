using System.Diagnostics;
using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using TestFrameworkLocalIO;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

public class Artifacts_Setup(ITestOutputHelper outputHelper)
{
    // Artifacts are created or used by a trigger that requires cleanup. And because I know you’ll forget sometimes ;) I will handle this. Keep adding them; I will keep cleaning them.

    private readonly Timeline _timeline = Timeline.Create()
        .SetupArtifact("msgFile") // At this call I will set your Artifact up.
        .Trigger(Simple.Simple.Trigger.MessageBox(Var.Ref<string>("cmdCommand")))
        .Build();

    [Fact]
    public async Task Run()
    {
        var run = await this._timeline.SetupRun(outputHelper)
            .AddVariable("cmdCommand", "Hello from an Artifact")
            .AddFileArtifact("msgFile", "./msg.txt", "Hello from an Artifact") // Every artifact that needs to be set up must be added.
            .RunAsync();
        run.EnsureRanToCompletion();
    }
}

public class Artifacts_Register(ITestOutputHelper outputHelper)
{
    // If you have an artifact mid timeline-run created by an unexpected trigger - no worries. Just tell me and I’ll keep track.

    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(LocalIO.Trigger.Cmd(Var.Ref<string>("cmdCreate")))
        .RegisterArtifact("newFile", LocalIO.Artifacts.FileRef("outNew.txt"))
        //                           ^ An artifact reference is how I can identify your artifact among the wide expanse of data.
        .Trigger(Simple.Simple.Trigger.MessageBox(Var.Ref<string>("cmdShow")))
        .Build();

    [Fact]
    public async Task Run()
    {
        var run = await this._timeline.SetupRun(outputHelper)
            .AddVariable("cmdCreate", "echo Hello from the new Artifact >> outNew.txt")
            .AddVariable("cmdShow", "Hello from the new Artifact")
            .RunAsync();
        run.EnsureRanToCompletion();
    }
}

public class Artifacts_Assert(ITestOutputHelper outputHelper)
{
    // Also, artifacts need to be assertable. There you go.

    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(LocalIO.Trigger.Cmd(Var.Ref<string>("cmdCreate")))
        .RegisterArtifact("newFile", LocalIO.Artifacts.FileRef("outAssert.txt"))
        .Build();

    [Fact]
    public async Task Run()
    {
        var run = await this._timeline.SetupRun(outputHelper)
            .AddVariable("cmdCreate", "echo Hello from the new Artifact >> outAssert.txt")
            .RunAsync();
        run.EnsureRanToCompletion();

        Debug.Assert(run.ArtifactStore.GetFileArtifact("newFile").Last.DataAsUtf8String == "Hello from the new Artifact \r\n");
        //               ^ Just like variables, artifacts are stored in the ArtifactStore.
    }
}

public class Artifacts_Versions(ITestOutputHelper outputHelper)
{
    // It may happen that an artifact transforms and changes over the span of the timeline. This is no problem - just capture the artifact state at this moment.

    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(LocalIO.Trigger.Cmd(Var.Ref<string>("cmdAppend")))
        .RegisterArtifact("newFile", LocalIO.Artifacts.FileRef("outVersion.txt"))
        .Trigger(LocalIO.Trigger.Cmd(Var.Ref<string>("cmdAppend")))
        .CaptureArtifactVersion("newFile", "laterVersion")
        .Build();

    [Fact]
    public async Task Run()
    {
        var run = await this._timeline.SetupRun(outputHelper)
            .AddVariable("cmdAppend", "echo Some Log >> outVersion.txt")
            .RunAsync();
        run.EnsureRanToCompletion();

        Debug.Assert(run.ArtifactStore.GetFileArtifact("newFile").First.DataAsUtf8String == "Some Log \r\n");
        Debug.Assert(run.ArtifactStore.GetFileArtifact("newFile")["laterVersion"].DataAsUtf8String == "Some Log \r\nSome Log \r\n");
        //                                                       ^ Not only via "first" or "last" can artifact versions be gathered - try just using the name.
    }
}