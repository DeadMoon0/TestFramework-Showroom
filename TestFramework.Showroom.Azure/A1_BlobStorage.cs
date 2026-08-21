using Microsoft.Extensions.DependencyInjection;
using System.Text;
using TestFramework.Azure.Extensions;
using TestFramework.Config;
using TestFramework.Container.Azure;
using TestFramework.Core.Timelines;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Azure;

// ══════════════════════════════════════════════════════════════════════════════
//  CLOUD INFRASTRUCTURE DIVISION - PARTICIPANT ORIENTATION MODULE A1
//  "Put Bytes In Storage. Then Demand Proof."
//
//  Blob Storage is where the showroom starts because it is the simplest useful
//  contract: put some bytes somewhere remote, then verify they actually arrived
//  instead of merely inspiring confidence on the local machine. Confidence, as
//  we have learned, is not a transport protocol.
//
//  The second lesson matters just as much as the first: test data gets cleaned
//  up automatically. Manual cleanup is how temporary experiments become unpaid
//  infrastructure archaeology with a follow-up budget meeting.
// ══════════════════════════════════════════════════════════════════════════════

public class BlobStorage_BasicUpload(ITestOutputHelper outputHelper)
{
    // First example: upload one blob and let the framework own its lifecycle.
    // No ceremony. No manual teardown. Just the contract in its cleanest form.
    // Very neat. Almost suspiciously neat.

    private static readonly Timeline _timeline = Timeline.Create()
        .SetupArtifact("blob")
        // ^ The artifact is created during setup and removed during cleanup.
        //   That is the pattern. Get used to it. It saves lives and storage accounts.
        .Build();

    [DockerFact]
    [Trait("Category", "DockerSmoke")]
    public async Task Run()
    {
        var configSub = ConfigInstance.Create().LoadDockerAzureConfig().Build();

        // The provider is owned by this test, so it is named and disposed rather than
        // built inline and abandoned. BuildServiceProvider returns the concrete
        // ServiceProvider to make that ownership visible.
        await using ServiceProvider provider = configSub.BuildServiceProvider();

        var run = await _timeline
            .SetupRun(provider, outputHelper)
            .SetEnv(AzureShowroom.CreateEnvironment())
            .AddBlobArtifact(
                "blob",                                    // artifact name — used later to assert against
                "MainStorage",                             // shared Azure showroom storage identifier
                "showroom/greetings.txt",                  // path inside the container
                Encoding.UTF8.GetBytes("Hello, Blob!"))    // The payload. Small, blunt, sufficient.
            .RunAsync();

        run.EnsureRanToCompletion();
        // ^ If upload or cleanup contract setup failed, this is where the run
        //   stops pretending things are fine and begins speaking in exceptions.
    }
}

public class BlobStorage_WithMetadata(ITestOutputHelper outputHelper)
{
    // Second example: same blob mechanics, now with metadata. The bytes matter,
    // but the labels around the bytes often drive the real behavior. Think of
    // them as sticky notes that survive power cycles and human incompetence.

    private static readonly Timeline _timeline = Timeline.Create()
        .SetupArtifact("blob")
        .Build();

    [DockerFact]
    [Trait("Category", "DockerSmoke")]
    public async Task Run()
    {
        var configSub = ConfigInstance.Create().LoadDockerAzureConfig().Build();

        // The provider is owned by this test, so it is named and disposed rather than
        // built inline and abandoned. BuildServiceProvider returns the concrete
        // ServiceProvider to make that ownership visible.
        await using ServiceProvider provider = configSub.BuildServiceProvider();

        var run = await _timeline
            .SetupRun(provider, outputHelper)
            .SetEnv(AzureShowroom.CreateEnvironment())
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

        // Assert the blob arrived intact and carrying the metadata we assigned.
        run.BlobArtifact("blob").Should().Exist();

        run.BlobArtifact("blob")
            .Metadata("department")
            .Should().Be("showroom");
        //              ^ Read the stored metadata directly from the captured artifact.
        //                Revolutionary, only because some teams still do screenshots.

        run.BlobArtifact("blob")
            .Utf8Text()
            .Should().Be("Quarterly synergy alignment achieved.");
        // ^ Verify the payload too. Metadata without payload integrity is just a
        //   well-labeled mistake in a nice jacket.
    }
}
