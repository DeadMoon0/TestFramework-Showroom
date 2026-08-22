using Microsoft.Extensions.DependencyInjection;
using Azure;
using Azure.Data.Tables;
using TestFramework.Azure;
using TestFramework.Azure.Extensions;
using TestFramework.Config;
using TestFramework.Container.Azure;
using TestFramework.Core.Timelines;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Azure;

//doc: PartitionKey. RowKey. Consequences.
//doc:
//doc: Table Storage is what happens when you want structured rows without dragging in the whole relational
//doc: ceremony. The price of that simplicity is clarity: your partition key and row key have to actually mean
//doc: something rather than just look busy. If they do, this chapter is straightforward. If they do not, the
//doc: system will still work, but your future self will eventually hold a meeting about you and bring charts.
//doc:
//doc: Two chapters: one row by exact key, and a set of rows by query. Both run against `azurite` -
//doc: `components [azure-reset, azurite]`, the same emulator chapter A1 used, because a table and a blob are
//doc: the same storage account as far as the environment is concerned.

//doc: The entity type comes first, and it implements `ITableEntity` because Azure insists on a predictable
//doc: contract. Azure is correct about this one, which is irritating but survivable. The four required
//doc: members are the contract; everything after them is yours - keep it practical, because this is storage,
//doc: not a creative writing exercise for nested object graphs and emotional complexity.

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

//doc: One row, upserted on setup and removed on teardown. The artifact name ties the three moments together:
//doc: `SetupArtifact("tableRow")` declares it, `AddTableEntityArtifact("tableRow", …)` supplies it, and
//doc: `TableArtifact<ShowroomTableEntity>("tableRow")` reads it back.
//doc:
//doc: `Entity(entity => entity.Payload)` is the general shape of an artifact assertion in this framework:
//doc: select a member of the captured thing, then assert on that. Not because the row cannot be dumped
//doc: wholesale, but because a failure message that names one field is worth ten that name an object.

public class TableStorage_BasicUpsert(ITestOutputHelper outputHelper)
{
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

        // The provider is owned by this test, so it is named and disposed rather than
        // built inline and abandoned. BuildServiceProvider returns the concrete
        // ServiceProvider to make that ownership visible.
        await using ServiceProvider provider = configSub.BuildServiceProvider();

        var run = await _timeline
            .SetupRun(provider, outputHelper)
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

//doc: Now find rows by query rather than by exact key - useful when the test cares about a subset of
//doc: entities, not one preselected address and its autobiography.
//doc:
//doc: Three rows are seeded, then the finder queries the partition they share. Every match becomes its own
//doc: tracked artifact - `foundRows_0`, `foundRows_1`, `foundRows_2` - which is what makes them assertable
//doc: individually instead of arriving as loose data drifting by like escaped paperwork.
//doc:
//doc: All three rows are the test's own here, so ownership is not the lesson. It is worth knowing anyway,
//doc: because it is the same rule as everywhere else: a discovered table entity is deleted at teardown like
//doc: any other, and `MarkReadonly()` on the `FindArtifacts` call is the way to say you only came to look.

public class TableStorage_QueryFinder(ITestOutputHelper outputHelper)
{
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

        // The provider is owned by this test, so it is named and disposed rather than
        // built inline and abandoned. BuildServiceProvider returns the concrete
        // ServiceProvider to make that ownership visible.
        await using ServiceProvider provider = configSub.BuildServiceProvider();

        var run = await _timeline
            .SetupRun(provider, outputHelper)
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
