using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using TestFramework.LocalIO;
using TestFramework.Simple;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

//doc: Variables are values. Artifacts are *things*: files, blobs, rows - anything heavy enough to need
//doc: creating before the run and removing after it, and just annoying enough to be dangerous when
//doc: abandoned. The framework tracks them so that removal is not a teardown script somebody has to
//doc: remember to write.
//doc:
//doc: Four chapters, in the order the question usually arrives:
//doc:
//doc: 1. The test creates it - `SetupArtifact` in the timeline, `AddFileArtifact` on the run.
//doc: 2. Something else creates it mid-run - `RegisterArtifact` with a reference.
//doc: 3. Once tracked, it can be asserted on.
//doc: 4. And it can be pinned at more than one point in time.
//doc:
//doc: One rule spans all four, and it is worth having before the code: **teardown deletes a tracked
//doc: artifact by default.** The declaring verb does not change that - registering is not "borrowing".
//doc: `MarkReadonly()` is the single opt-out, and the compiler offers it only on the verbs that adopt or
//doc: discover something (`RegisterArtifact`, `FindArtifact`, `FindArtifacts`, `FindArtifactsAs`), never
//doc: on `SetupArtifact`. An artifact the test created is the test's to remove. Chapters W2 and W5 in the
//doc: web lane are where the opt-out earns its keep.

//doc: First, the plumbing. These chapters are about artifacts, not about shells, so the platform
//doc: differences live in one place instead of at every call site. Two details in here are the kind that
//doc: make an assertion pass on one machine and fail on another, which is why they are written down
//doc: rather than remembered.

file static class ArtifactSamplePaths
{
    public static string BuildOutput => AppContext.BaseDirectory;

    public static string UniqueFile(string prefix)
        => Path.Combine(BuildOutput, $"{prefix}-{Guid.NewGuid():N}.txt");

    // The chapters below are about artifacts, not about shells, so the portability lives here
    // instead of in every call site. Note there is no space before `>>`: cmd's echo copies
    // everything between the text and the redirect into the file, trailing space included, which
    // is exactly the sort of detail that makes an assertion pass on one machine and fail on another.
    // On Unix, printf beats echo because the bash builtin and /bin/sh disagree about escapes.
    public static string AppendLine(string text, string file)
        => OperatingSystem.IsWindows() ? $"echo {text}>> {file}" : $"printf '%s\\n' '{text}' >> {file}";

    public static string ExpectedLine(string text)
        => OperatingSystem.IsWindows() ? $"{text}\r\n" : $"{text}\n";
}

//doc: The declare-then-populate path. `SetupArtifact("msgFile")` reserves the name in the plan; the run
//doc: supplies the actual file and its content. The timeline never mentions a path, because a path is a
//doc: per-run detail and the plan is not.

public class Artifacts_Setup(ITestOutputHelper outputHelper)
{
    private readonly Timeline _timeline = Timeline.Create()
        .SetupArtifact("msgFile") // Register the artifact slot up front so the run knows this file matters and not just spiritually.
        .Trigger(SimpleExt.Trigger.Message(Var.Ref<string>("cmdCommand")))
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

//doc: Sometimes the artifact appears in the middle of the run rather than before it. Fine. Register the
//doc: reference and the framework can still track it, instead of you staring at the aftermath like a
//doc: detective in overbudget shoes.
//doc:
//doc: The reference is the address, and it is variable-backed like everything else - so the timeline
//doc: says *how to find* the file without knowing where it will be.

public class Artifacts_Register(ITestOutputHelper outputHelper)
{
    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(LocalIOExt.Trigger.Cmd(Var.Ref<string>("cmdCreate"), Var.Ref<string>("cwd")))
        .RegisterArtifact("newFile", LocalIOExt.Artifacts.FileRef(Var.Ref<string>("artifactPath")))
        //                           ^ A reference is the address. Without it,
        //                             you do not have tracking, you have gossip and blame allocation.
        .Trigger(SimpleExt.Trigger.Message(Var.Ref<string>("cmdShow")))
        .Build();

    [Fact]
    public async Task Run()
    {
        string artifactPath = ArtifactSamplePaths.UniqueFile("showroom-basic-register");
        string artifactFileName = Path.GetFileName(artifactPath);

        var run = await this._timeline.SetupRun(outputHelper)
            .AddVariable("cmdCreate", ArtifactSamplePaths.AppendLine("Hello from the new Artifact", artifactFileName))
            .AddVariable("cmdShow", "Hello from the new Artifact")
            .AddVariable("cwd", ArtifactSamplePaths.BuildOutput)
            .AddVariable("artifactPath", artifactPath)
            .RunAsync();
        run.EnsureRanToCompletion();
    }
}

//doc: Once an artifact is tracked, it graduates from side effect to testable evidence. That promotion is
//doc: the whole reason to bother: untracked side effects are how meetings get longer.
//doc:
//doc: In the report below, look at the Register Artifact step's outputs. The artifact is listed with a
//doc: state - `Setup` while the run holds it, `Cleaned` once the Cleanup Stage has been through - so the
//doc: run tells you what it did to your file rather than leaving you to check.

public class Artifacts_Assert(ITestOutputHelper outputHelper)
{
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
            .AddVariable("cmdCreate", ArtifactSamplePaths.AppendLine("Hello from the new Artifact", artifactFileName))
            .AddVariable("cwd", ArtifactSamplePaths.BuildOutput)
            .AddVariable("artifactPath", artifactPath)
            .RunAsync();
        run.EnsureRanToCompletion();

        run.FileArtifact("newFile").Utf8Text().Should().Be(ArtifactSamplePaths.ExpectedLine("Hello from the new Artifact"));
        //                    ^ The run stores artifact versions in one place you
        //                      can inspect instead of re-deriving them from chaos and stale confidence.
    }
}

//doc: Artifacts change over time, and pretending otherwise is how you lose the exact moment something
//doc: became wrong. `CaptureArtifactVersion` reads the artifact again and files the result under a name,
//doc: so the test can compare two points in the same run.
//doc:
//doc: Here the same append command runs twice. `First` is the state the register step captured; the named
//doc: version is the state after the second append. That beats "the earlier one, but not the first
//doc: earlier one", and it beats historical fiction disguised as debugging.

public class Artifacts_Versions(ITestOutputHelper outputHelper)
{
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
            .AddVariable("cmdAppend", ArtifactSamplePaths.AppendLine("Some Log", artifactFileName))
            .AddVariable("cwd", ArtifactSamplePaths.BuildOutput)
            .AddVariable("artifactPath", artifactPath)
            .RunAsync();
        run.EnsureRanToCompletion();

        Assert.Equal(ArtifactSamplePaths.ExpectedLine("Some Log"), run.ArtifactStore.GetFileArtifact("newFile").First.DataAsUtf8String);
        Assert.Equal(ArtifactSamplePaths.ExpectedLine("Some Log") + ArtifactSamplePaths.ExpectedLine("Some Log"), run.ArtifactStore.GetFileArtifact("newFile")["laterVersion"].DataAsUtf8String);
        //                                                       ^ Named versions pin the exact state you care about, which beats "the earlier one, but not the first earlier one."
    }
}
