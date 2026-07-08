using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using TestFramework.LocalIO;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

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
}

public class ArtifactLifecycle_DeclareThenPopulate(ITestOutputHelper outputHelper)
{
    // Lifecycle is where the showroom stops talking in theory and starts
    // proving whether an artifact survives being declared before it exists.
    //
    // Step 1: declare the slot in the timeline.
    // Step 2: populate it during SetupRun(...) with actual data.

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

public class ArtifactLifecycle_RegisterThenAssert(ITestOutputHelper outputHelper)
{
    // RegisterArtifact is the after-the-fact path: the step creates the thing,
    // and the timeline starts tracking it once it exists. No prophecy required.

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
            .AddVariable("cmdCreate", $"echo registered at runtime > {artifactFileName}")
            .AddVariable("cwd", ArtifactLifecycleSamplePaths.BuildOutput)
            .AddVariable("artifactPath", artifactPath)
            .RunAsync();

        run.EnsureRanToCompletion();
        run.FileArtifact("createdFile").Utf8Text().Should().Be("registered at runtime \r\n");
    }
}

public class ArtifactLifecycle_FindThenAssert(ITestOutputHelper outputHelper)
{
    // FindArtifacts is the discovery path: the timeline searches for resources
    // created elsewhere and gives the results predictable names for later use.

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
}
