using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using TestFramework.LocalIO;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

//doc: Chapter 05 introduced artifacts. This one is about the three ways one gets in front of a test, side
//doc: by side, because the difference between them is only ever *when the thing exists*:
//doc:
//doc: 1. **Declare, then populate.** `SetupArtifact` reserves the name; the run supplies the data. The
//doc:    artifact does not exist until the run makes it.
//doc: 2. **Register, then assert.** A step creates it; `RegisterArtifact` starts tracking it once it
//doc:    exists. No prophecy required.
//doc: 3. **Find, then assert.** Nobody tells the timeline the key at all - a finder locates what matches.
//doc:
//doc: What they do *not* differ in is teardown. All three are deleted at the end unless the declaring step
//doc: says `MarkReadonly()`, which `SetupArtifact` cannot even offer. Chapters W2 and W5 are where that
//doc: distinction has consequences; here every file was created by the test, so deleting it is the whole
//doc: point.

//doc: The shim, with one more helper than before: a folder, because the third path needs something to
//doc: search. The `>>` footnote is the same one as in chapter 05 and just as load-bearing.

file static class ArtifactLifecycleSamplePaths
{
    public static string BuildOutput => AppContext.BaseDirectory;

    public static string UniqueFile(string prefix)
        => Path.Combine(BuildOutput, $"{prefix}-{Guid.NewGuid():N}.txt");

    public static string UniqueFolder(string prefix)
    {
        string path = Path.Combine(BuildOutput, $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    // The lifecycle is the lesson; the shell is plumbing. The file name is unique per run, so
    // appending and creating amount to the same thing. Note there is no space before `>>`: with
    // one, cmd's echo writes the space into the file too and ExpectedLine stops being exact.
    public static string AppendLine(string text, string file)
        => OperatingSystem.IsWindows() ? $"echo {text}>> {file}" : $"printf '%s\\n' '{text}' >> {file}";

    public static string ExpectedLine(string text)
        => OperatingSystem.IsWindows() ? $"{text}\r\n" : $"{text}\n";
}

//doc: Path one. The timeline declares a slot and knows nothing else - not the path, not the content. Both
//doc: arrive on the run. That separation is what lets one timeline seed a different file per run without
//doc: being edited.

public class ArtifactLifecycle_DeclareThenPopulate(ITestOutputHelper outputHelper)
{
    private readonly Timeline _timeline = Timeline.Create()
        .SetupArtifact("declaredFile")
        .Build();

    [Fact]
    public async Task Run()
    {
        string artifactPath = ArtifactLifecycleSamplePaths.UniqueFile("showroom-lifecycle-declare");

        var run = await this._timeline.SetupRun(outputHelper)
            .AddFileArtifact("declaredFile", artifactPath, "declared then populated")
            .RunAsync();

        run.EnsureRanToCompletion();
        run.FileArtifact("declaredFile").Utf8Text().Should().Be("declared then populated");
    }
}

//doc: Path two. The command writes the file; the register step adopts it by reference and reads it, which
//doc: is what makes the assertion possible at all. Compare the two Stage Plans in the panels: this one has
//doc: an `Act` step the previous chapter did not, because here something actually had to happen first.

public class ArtifactLifecycle_RegisterThenAssert(ITestOutputHelper outputHelper)
{
    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(LocalIOExt.Trigger.Cmd(Var.Ref<string>("cmdCreate"), Var.Ref<string>("cwd")))
        .RegisterArtifact("createdFile", LocalIOExt.Artifacts.FileRef(Var.Ref<string>("artifactPath")))
        .Build();

    [Fact]
    public async Task Run()
    {
        string artifactPath = ArtifactLifecycleSamplePaths.UniqueFile("showroom-lifecycle-register");
        string artifactFileName = Path.GetFileName(artifactPath);

        var run = await this._timeline.SetupRun(outputHelper)
            .AddVariable("cmdCreate", ArtifactLifecycleSamplePaths.AppendLine("registered at runtime", artifactFileName))
            .AddVariable("cwd", ArtifactLifecycleSamplePaths.BuildOutput)
            .AddVariable("artifactPath", artifactPath)
            .RunAsync();

        run.EnsureRanToCompletion();
        run.FileArtifact("createdFile").Utf8Text().Should().Be(ArtifactLifecycleSamplePaths.ExpectedLine("registered at runtime"));
    }
}

//doc: Path three: discovery. `FindArtifacts` takes a base name and a finder, and every match becomes its
//doc: own tracked artifact named `foundFile_0`, `foundFile_1`, and so on - predictable names, so the
//doc: assertions stay readable.
//doc:
//doc: The numbering follows the order the finder returned things in, which is why this chapter asserts that
//doc: two artifacts exist rather than which of them is which. If it matters which is which, assert on the
//doc: content, not on the index. Chapter 15 shows what happens when a finder returns a different number of
//doc: matches than the timeline said it would.

public class ArtifactLifecycle_FindThenAssert(ITestOutputHelper outputHelper)
{
    private readonly Timeline _timeline = Timeline.Create()
        .FindArtifacts("foundFile", new FileArtifactFolderFinder(Var.Ref<string>("folder")))
        .Build();

    [Fact]
    public async Task Run()
    {
        string folder = ArtifactLifecycleSamplePaths.UniqueFolder("showroom-lifecycle-find");
        File.WriteAllText(Path.Combine(folder, "alpha.txt"), "alpha");
        File.WriteAllText(Path.Combine(folder, "beta.txt"), "beta");

        var run = await this._timeline.SetupRun(outputHelper)
            .AddVariable("folder", folder)
            .RunAsync();

        run.EnsureRanToCompletion();
        run.FileArtifact("foundFile_0").Should().Exist();
        run.FileArtifact("foundFile_1").Should().Exist();
    }

    //doc: And note what teardown does here, because it is the rule and not an accident: both discovered
    //doc: files are deleted. The test wrote them a moment ago, so that is correct - but "found" never meant
    //doc: "borrowed", and the run log says so out loud.
}
