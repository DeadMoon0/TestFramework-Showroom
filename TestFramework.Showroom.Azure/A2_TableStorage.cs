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
//  CLOUD INFRASTRUCTURE DIVISION - PARTICIPANT ORIENTATION MODULE A2
//  "PartitionKey. RowKey. Consequences."
//
//  Table Storage is what happens when you want structured rows without dragging
//  in the whole relational ceremony. The price of that simplicity is clarity:
//  your PartitionKey and RowKey must actually mean something and not just look busy.
//
//  If they do, this module is straightforward. If they do not, the system will
//  still work, but your future self will eventually hold a meeting about you and bring charts.
// ══════════════════════════════════════════════════════════════════════════════

// Table entities implement ITableEntity because Azure insists on a predictable
// contract. Azure is correct about this one, which is irritating but survivable.
public class ShowroomTableEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "";
    public string RowKey       { get; set; } = "";
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    // Then add your own columns. Keep them practical. This is storage, not a
    // creative writing exercise for nested object graphs and emotional complexity.
    public string Payload { get; set; } = "";
    public int Priority { get; set; }
}

public class TableStorage_BasicUpsert(ITestOutputHelper outputHelper)
{
    // First example: upsert one row, verify it, and let cleanup erase the test
    // evidence after the lesson is over. A tidy operation. Almost suspiciously tidy.

    private static readonly Timeline _timeline = Timeline.Create()
        .SetupArtifact("tableRow")
        // ^ Setup writes the row. Cleanup removes it. Manual teardown is not a
        //   personality trait, it is a maintenance problem wearing confidence.
        .Build();

    [DockerFact]
    [Trait("Category", "DockerSmoke")]
    public async Task Run()
    {
        var configSub = ConfigInstance.Create().LoadDockerAzureConfig().Build();

        var run = await _timeline
            .SetupRun(configSub.BuildServiceProvider(), outputHelper)
            .SetEnv(AzureShowroom.CreateEnvironment())
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

        // The entity exists now. Good. We still verify it like adults with trust issues.
        run.TableArtifact<ShowroomTableEntity>("tableRow").Should().Exist();

        run.TableArtifact<ShowroomTableEntity>("tableRow")
            .Entity(entity => entity.Payload)
            .Should().Be("First contact.");
        // ^ Row captured, payload verified. Move on before success gets sentimental and starts asking for funding.
    }
}

public class TableStorage_QueryFinder(ITestOutputHelper outputHelper)
{
    // Second example: find rows by query rather than exact key. Useful when the
    // test cares about a subset of entities, not one preselected address and its autobiography.

    private static readonly Timeline _timeline = Timeline.Create()
        .SetupArtifact("row1")
        .SetupArtifact("row2")
        .SetupArtifact("row3")
        // Seed three rows, then query for the interesting partition. The query
        // result becomes tracked artifacts too, not loose data drifting by like escaped paperwork.
        .FindArtifacts(
            "foundRows",
            AzureExt.ArtifactFinder.StorageAccount.TableQuery<ShowroomTableEntity>(
                "MainStorage",
                "MainTable",
                "PartitionKey eq 'showroom-query'"))
        //  ^ Matching rows come back as individual artifacts you can assert on. Civilized behavior from a query engine.
        .Build();

    [DockerFact]
    [Trait("Category", "DockerSmoke")]
    public async Task Run()
    {
        var configSub = ConfigInstance.Create().LoadDockerAzureConfig().Build();

        var run = await _timeline
            .SetupRun(configSub.BuildServiceProvider(), outputHelper)
            .SetEnv(AzureShowroom.CreateEnvironment())
            .AddTableEntityArtifact("row1", "MainStorage", "MainTable",
                new ShowroomTableEntity { PartitionKey = "showroom-query", RowKey = "r1", Payload = "Alpha", Priority = 10 })
            .AddTableEntityArtifact("row2", "MainStorage", "MainTable",
                new ShowroomTableEntity { PartitionKey = "showroom-query", RowKey = "r2", Payload = "Beta",  Priority = 20 })
            .AddTableEntityArtifact("row3", "MainStorage", "MainTable",
                new ShowroomTableEntity { PartitionKey = "showroom-query", RowKey = "r3", Payload = "Gamma", Priority = 30 })
            .RunAsync();

        run.EnsureRanToCompletion();

        // Query hits are named deterministically so assertions stay readable and nobody has to name them by vibes.
        run.TableArtifact<ShowroomTableEntity>("foundRows_0").Should().Exist();
        run.TableArtifact<ShowroomTableEntity>("foundRows_1").Should().Exist();
        run.TableArtifact<ShowroomTableEntity>("foundRows_2").Should().Exist();
    }
}
