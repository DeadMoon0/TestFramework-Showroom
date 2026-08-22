using Microsoft.Extensions.DependencyInjection;
using System.Text;
using TestFramework.Azure.Extensions;
using TestFramework.Config;
using TestFramework.Container.Azure;
using TestFramework.Core.Timelines;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Azure;

//doc: Put bytes in storage. Then demand proof.
//doc:
//doc: Blob Storage is where the cloud lane starts because it is the simplest useful contract: put some bytes
//doc: somewhere remote, then verify they actually arrived instead of merely inspiring confidence on the local
//doc: machine. Confidence, as we have learned, is not a transport protocol.
//doc:
//doc: The second lesson matters as much as the first: the test data is removed for you. Manual cleanup is how
//doc: temporary experiments become unpaid infrastructure archaeology with a follow-up budget meeting.
//doc:
//doc: Both chapters run against `azurite`, and the environment step says so - `components [azure-reset,
//doc: azurite]`, because a blob is all these chapters ask for.

//doc: The cleanest form there is: declare the artifact, hand the run its bytes, assert. No ceremony, no
//doc: manual teardown. Very neat. Almost suspiciously neat.
//doc:
//doc: `AddBlobArtifact` takes the four things a blob needs and nothing else - the artifact name the timeline
//doc: declared, the storage identifier from `AzureShowroom.cs`, the path inside the container, and the
//doc: payload. Note that the identifier is a name, not an account: which account `MainStorage` means is the
//doc: environment's business, which is why this same timeline would work against a real storage account.

public class BlobStorage_BasicUpload(ITestOutputHelper outputHelper)
{
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

//doc: Same blob mechanics, now with metadata - because the bytes matter, but the labels around the bytes
//doc: often drive the real behaviour. Think of them as sticky notes that survive power cycles and human
//doc: incompetence.
//doc:
//doc: The assertions are the part to copy. Metadata is read back off the captured artifact rather than
//doc: through a client the test built itself, and the payload is checked as well as the labels: metadata
//doc: without payload integrity is just a well-labelled mistake in a nice jacket.

public class BlobStorage_WithMetadata(ITestOutputHelper outputHelper)
{
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
