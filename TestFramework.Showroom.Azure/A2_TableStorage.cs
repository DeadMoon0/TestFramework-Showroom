using Azure;
using Azure.Data.Tables;
using TestFramework.Azure;
using TestFramework.Azure.Extensions;
using TestFramework.Config;
using TestFramework.Container.Azure;
using TestFramework.Core.Timelines;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Azure;

// ══════════════════════════════════════════════════════════════════════════════
//  CLOUD INFRASTRUCTURE DIVISION — PARTICIPANT ORIENTATION MODULE A2
//  "Table Storage: A Grid. For Your Data. In The Cloud. You're Welcome."
//
//  You asked for structure. We gave you a table.
//  Not a relational table — we want to be clear about that.
//  This is a KEY-VALUE table. It has a PartitionKey and a RowKey.
//  Together, they uniquely identify your row. Like a fingerprint.
//  But for data. In Azure. In a table.
//
//  Please proceed to the first example. The exit is where you came in.
//  The exit does not open during testing.
// ══════════════════════════════════════════════════════════════════════════════

// Your entity must implement ITableEntity. This means four fields.
// We did not invent this requirement. We merely enforce it. Enthusiastically.
public class ShowroomTableEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "";
    public string RowKey       { get; set; } = "";
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    // Add your own columns below. Anything that serializes. Within reason.
    // We define "within reason" loosely. Erring on the side of fewer nested objects.
    public string Payload { get; set; } = "";
    public int Priority { get; set; }
}

[Collection("AzureShowroom")]
public class TableStorage_BasicUpsert(ITestOutputHelper outputHelper)
{
    // Insert a row. Confirm it's there. Watch it vanish at cleanup.
    // That last part was not supposed to sound ominous. It just does.

    private static readonly Timeline _timeline = Timeline.Create()
        .SetupArtifact("tableRow")
        // ^ Upsert on setup, delete on cleanup. Zero manual teardown.
        //   Participants who handled their own teardown previously reported "it went fine."
        //   We have no record of those participants submitting follow-up reports.
        .Build();

    [Fact]
    public async Task Run()
    {
        var configSub = AzureShowroom.BuildConfig();

        var run = await _timeline
            .SetupRun(configSub.BuildServiceProvider(), outputHelper)
            .SetEnv(DockerAzureEnvironment.For<AzureShowroom.DefaultFunctionAppDefinition>())
            .AddTableEntityArtifact(
                "tableRow",               // artifact name
                "MainStorage",            // storage account identifier
                "MainTable",              // table name — must exist in your storage account
                new ShowroomTableEntity
                {
                    PartitionKey = "showroom",
                    RowKey       = "row-001",
                    Payload      = "First contact.",
                    Priority     = 1,
                })
            .RunAsync();

        run.EnsureRanToCompletion();

        // The entity was written. Trust but verify.
        run.TableArtifact<ShowroomTableEntity>("tableRow").Should().Exist();

        run.TableArtifact<ShowroomTableEntity>("tableRow")
            .Select(d => d.Entity.Payload)
            .Should().Be("First contact.");
        // ^ That worked. Note the time. Tell someone.
    }
}

[Collection("AzureShowroom")]
public class TableStorage_QueryFinder(ITestOutputHelper outputHelper)
{
    // Sometimes you want to FIND entities you didn't personally put there.
    // Or entities you DID put there, but arranged so the test doesn't need to know the exact keys.
    // We call this "discovery." Sounds more exciting than "OData filter string."

    private static readonly Timeline _timeline = Timeline.Create()
        .SetupArtifact("row1")
        .SetupArtifact("row2")
        .SetupArtifact("row3")
        // All three rows written. Now we find the ones that match our filter.
        // The framework will wire up the cleanup of the FOUND rows too.
        // Science in action. Automated science. The best kind.
        .FindArtifactMulti(
            ["foundRows"],
            AzureTF.ArtifactFinder.StorageAccount.TableQuery<ShowroomTableEntity>(
                "MainStorage",
                "MainTable",
                "PartitionKey eq 'showroom-query'"))
        //  ^ This OData filter runs against Azure Table Storage.
        //    Everything matching comes back as artifacts you can assert on individually.
        .Build();

    [Fact]
    public async Task Run()
    {
        var configSub = AzureShowroom.BuildConfig();

        var run = await _timeline
            .SetupRun(configSub.BuildServiceProvider(), outputHelper)
            .SetEnv(DockerAzureEnvironment.For<AzureShowroom.DefaultFunctionAppDefinition>())
            .AddTableEntityArtifact("row1", "MainStorage", "MainTable",
                new ShowroomTableEntity { PartitionKey = "showroom-query", RowKey = "r1", Payload = "Alpha", Priority = 10 })
            .AddTableEntityArtifact("row2", "MainStorage", "MainTable",
                new ShowroomTableEntity { PartitionKey = "showroom-query", RowKey = "r2", Payload = "Beta",  Priority = 20 })
            .AddTableEntityArtifact("row3", "MainStorage", "MainTable",
                new ShowroomTableEntity { PartitionKey = "showroom-query", RowKey = "r3", Payload = "Gamma", Priority = 30 })
            .RunAsync();

        run.EnsureRanToCompletion();

        // Query results use the base name for the first hit, then append _1, _2, ... for the rest.
        run.TableArtifact<ShowroomTableEntity>("foundRows").Should().Exist();
        run.TableArtifact<ShowroomTableEntity>("foundRows_1").Should().Exist();
        run.TableArtifact<ShowroomTableEntity>("foundRows_2").Should().Exist();
    }
}
