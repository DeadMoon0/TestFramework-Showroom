using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using TestFramework.LocalIO;
using TestFramework.Simple;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

file static class ArtifactSamplePaths
{
    public static string BuildOutput => AppContext.BaseDirectory;

    public static string UniqueFile(string prefix)
        => Path.Combine(BuildOutput, $"{prefix}-{Guid.NewGuid():N}.txt");
}

public class Artifacts_Setup(ITestOutputHelper outputHelper)
{
    // Artifacts are the concrete things a run creates or depends on. Files,
    // blobs, rows, all the evidence heavy enough to need setup and cleanup and just annoying enough to be dangerous when abandoned.

    private readonly Timeline _timeline = Timeline.Create()
        .SetupArtifact("msgFile") // Register the artifact slot up front so the run knows this file matters and not just spiritually.
        .Trigger(SimpleExt.Trigger.MessageBox(Var.Ref<string>("cmdCommand")))
        .Build();

    [Fact]
    public async Task Run()
    {
        string artifactPath = ArtifactSamplePaths.UniqueFile("showroom-basic-msg");

        var run = await this._timeline.SetupRun(outputHelper)
            .AddVariable("cmdCommand", "Hello from an Artifact")
            .AddFileArtifact("msgFile", artifactPath, "Hello from an Artifact") // Real artifact data enters the run here. Actual file. No interpretive dance.
            .RunAsync();
        run.EnsureRanToCompletion();
    }
}

public class Artifacts_Register(ITestOutputHelper outputHelper)
{
    // Sometimes the artifact appears in the middle of the run rather than before
    // it. Fine. Register the reference and the framework can still track it instead of staring at the aftermath like a detective in overbudget shoes.

    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(LocalIOExt.Trigger.Cmd(Var.Ref<string>("cmdCreate"), Var.Ref<string>("cwd")))
        .RegisterArtifact("newFile", LocalIOExt.Artifacts.FileRef(Var.Ref<string>("artifactPath")))
        //                           ^ A reference is the address. Without it,
        //                             you do not have tracking, you have gossip and blame allocation.
        .Trigger(SimpleExt.Trigger.MessageBox(Var.Ref<string>("cmdShow")))
        .Build();

    [Fact]
    public async Task Run()
    {
        string artifactPath = ArtifactSamplePaths.UniqueFile("showroom-basic-register");
        string artifactFileName = Path.GetFileName(artifactPath);

        var run = await this._timeline.SetupRun(outputHelper)
            .AddVariable("cmdCreate", $"echo Hello from the new Artifact >> {artifactFileName}")
            .AddVariable("cmdShow", "Hello from the new Artifact")
            .AddVariable("cwd", ArtifactSamplePaths.BuildOutput)
            .AddVariable("artifactPath", artifactPath)
            .RunAsync();
        run.EnsureRanToCompletion();
    }
}

public class Artifacts_Assert(ITestOutputHelper outputHelper)
{
    // Once an artifact is tracked, it graduates from side effect to testable
    // evidence. That promotion matters. Untracked side effects are how meetings get longer.

    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(LocalIOExt.Trigger.Cmd(Var.Ref<string>("cmdCreate"), Var.Ref<string>("cwd")))
        .RegisterArtifact("newFile", LocalIOExt.Artifacts.FileRef(Var.Ref<string>("artifactPath")))
        .Build();

    [Fact]
    public async Task Run()
    {
        string artifactPath = ArtifactSamplePaths.UniqueFile("showroom-basic-assert");
        string artifactFileName = Path.GetFileName(artifactPath);

        var run = await this._timeline.SetupRun(outputHelper)
            .AddVariable("cmdCreate", $"echo Hello from the new Artifact >> {artifactFileName}")
            .AddVariable("cwd", ArtifactSamplePaths.BuildOutput)
            .AddVariable("artifactPath", artifactPath)
            .RunAsync();
        run.EnsureRanToCompletion();

        run.FileArtifact("newFile").Utf8Text().Should().Be("Hello from the new Artifact \r\n");
        //                    ^ The run stores artifact versions in one place you
        //                      can inspect instead of re-deriving them from chaos and stale confidence.
    }
}

public class Artifacts_Versions(ITestOutputHelper outputHelper)
{
    // Artifacts change over time. Pretending otherwise is how you lose the exact
    // moment something became wrong. Capture versions when the state matters, unless you enjoy historical fiction disguised as debugging.

    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(LocalIOExt.Trigger.Cmd(Var.Ref<string>("cmdAppend"), Var.Ref<string>("cwd")))
        .RegisterArtifact("newFile", LocalIOExt.Artifacts.FileRef(Var.Ref<string>("artifactPath")))
        .Trigger(LocalIOExt.Trigger.Cmd(Var.Ref<string>("cmdAppend"), Var.Ref<string>("cwd")))
        .CaptureArtifactVersion("newFile", "laterVersion")
        .Build();

    [Fact]
    public async Task Run()
    {
        string artifactPath = ArtifactSamplePaths.UniqueFile("showroom-basic-version");
        string artifactFileName = Path.GetFileName(artifactPath);

        var run = await this._timeline.SetupRun(outputHelper)
            .AddVariable("cmdAppend", $"echo Some Log >> {artifactFileName}")
            .AddVariable("cwd", ArtifactSamplePaths.BuildOutput)
            .AddVariable("artifactPath", artifactPath)
            .RunAsync();
        run.EnsureRanToCompletion();

        Assert.Equal("Some Log \r\n", run.ArtifactStore.GetFileArtifact("newFile").First.DataAsUtf8String);
        Assert.Equal("Some Log \r\nSome Log \r\n", run.ArtifactStore.GetFileArtifact("newFile")["laterVersion"].DataAsUtf8String);
        //                                                       ^ Named versions pin the exact state you care about, which beats "the earlier one, but not the first earlier one."
    }
}