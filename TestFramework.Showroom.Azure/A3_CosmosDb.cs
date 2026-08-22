using Microsoft.Extensions.DependencyInjection;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using TestFramework.Azure;
using TestFramework.Azure.Extensions;
using TestFramework.Config;
using TestFramework.Container.Azure;
using TestFramework.Core.Timelines;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Azure;

//doc: JSON documents, partition keys, and the price of pretending they are optional.
//doc:
//doc: Cosmos is where the lane starts dealing in documents instead of rows. Flexible schema, powerful
//doc: queries, serious scaling potential - all true. So is the partition key requirement, which arrives like
//doc: tax law and stays longer. Ignore partitioning strategy long enough and eventually the system writes a
//doc: very expensive letter to whoever thought it was somebody else's problem. It may use metrics.
//doc:
//doc: Both chapters here run against `components [azure-reset, cosmos-emulator]`. No storage, no Service Bus:
//doc: the same declared facility as every other cloud chapter, resolved down to what these two actually use.

//doc: The document type first. Cosmos documents are plain records with explicit JSON property names, and
//doc: every document gets an `id` - not because it is fashionable, but because Cosmos asks and does not
//doc: negotiate.
//doc:
//doc: Two of these properties are load-bearing rather than decorative. The framework finds the id and the
//doc: partition key by convention: a property called `Id` or `PartitionKey`, or any property whose *mapped*
//doc: JSON name is `id` or `partitionKey`. Name them something else without a mapping and you get a specific
//doc: exception naming your type, not a mystery at insert time. That resolution is also how the container
//doc: gets its partition key path, so the model is the single source of both.

// Cosmos documents are plain records with explicit JSON property names. Also,
// every document gets an id. Not because it is fashionable. Because Cosmos asks and does not negotiate.
public record CosmosShowroomItem
{
    [JsonProperty("id")]
    public string Id { get; init; } = "";

    [JsonProperty("PartitionKey")]
    public string PartitionKey { get; init; } = "";

    [JsonProperty("name")]
    public string Name { get; init; } = "";

    [JsonProperty("score")]
    public int Score { get; init; }
}

//doc: One document, upserted and verified. Setup owns the upsert, cleanup owns the delete, and the
//doc: predictability is both the point and the sales pitch.
//doc:
//doc: `AddCosmosItemArtifact` needs no key arguments, unlike the SQL and Table equivalents: the document
//doc: carries its own `id` and partition key, so the reference reads them off the object it was given. The
//doc: `MainDb` identifier resolves through config exactly like `MainStorage` did - it is the name under
//doc: `CosmosDb:MainDb`, not a connection string in a test file.

public class CosmosDb_BasicUpsert(ITestOutputHelper outputHelper)
{
    private static readonly Timeline _timeline = Timeline.Create()
        .SetupArtifact("cosmosDoc")
        // ^ Setup owns the upsert. Cleanup owns the delete. Predictability is the point and also the sales pitch.
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
            .AddCosmosItemArtifact(
                "cosmosDoc",    // artifact name — ties everything together
                "MainDb",       // identifier found under CosmosDb:MainDb in settings
                new CosmosShowroomItem
                {
                    Id           = "showroom-001",
                    PartitionKey = "showroom",
                    Name         = "First Volunteer",
                    Score        = 100,
                })
            .RunAsync();

        run.EnsureRanToCompletion();

        run.CosmosArtifact<CosmosShowroomItem>("cosmosDoc").Should().Exist();
        // ^ The run captured the inserted document. Now the test has something real to inspect instead of a motivational story.
    }
}

//doc: And querying by property rather than by id - where Cosmos stops being a key-value shelf and starts
//doc: being a search surface with opinions.
//doc:
//doc: Three documents are seeded and the query asks for two of them: `c.score = 99` and the shared partition.
//doc: Matches come back as `topScorers_0` and `topScorers_1`; the third document does not appear, because the
//doc: query contract filtered it out. Brutal, but mathematically fair.
//doc:
//doc: The seeded non-match is the detail worth copying. Seed something the query must *not* return, and the
//doc: assertion proves the filter works instead of proving that seeding works. And note that being excluded
//doc: from the query is not being excluded from cleanup: all three documents were seeded by the test, so all
//doc: three are removed. Fairness is not the same as leniency.

public class CosmosDb_QueryFinder(ITestOutputHelper outputHelper)
{
    private static readonly Timeline _timeline = Timeline.Create()
        .SetupArtifact("candidate1")
        .SetupArtifact("candidate2")
        .SetupArtifact("candidate3")
        // Seed three documents, then query for the high scorers. Matching results
        // come back as tracked artifacts. Non-matches still get cleaned up because fairness is not the same as leniency.
        .FindArtifacts(
            "topScorers",
            AzureExt.ArtifactFinder.DB.CosmosQuery<CosmosShowroomItem>(
                "MainDb",
                new QueryDefinition("SELECT * FROM c WHERE c.score = 99 AND c.PartitionKey = 'showroom-query'")))
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
            .AddCosmosItemArtifact("candidate1", "MainDb",
                new CosmosShowroomItem { Id = "q-001", PartitionKey = "showroom-query", Name = "High Achiever A", Score = 99 })
            .AddCosmosItemArtifact("candidate2", "MainDb",
                new CosmosShowroomItem { Id = "q-002", PartitionKey = "showroom-query", Name = "High Achiever B", Score = 99 })
            .AddCosmosItemArtifact("candidate3", "MainDb",
                new CosmosShowroomItem { Id = "q-003", PartitionKey = "showroom-query", Name = "Average Achiever", Score = 40 })
            //                                                                                                              ^ Not a matching score. Also not a cleanup exemption.
            .RunAsync();

        run.EnsureRanToCompletion();

        // Matching results are named deterministically for direct assertions and low blood pressure.
        run.CosmosArtifact<CosmosShowroomItem>("topScorers_0").Should().Exist();
        run.CosmosArtifact<CosmosShowroomItem>("topScorers_1").Should().Exist();
        // Candidate 3 does not appear because the query contract filtered it out. Brutal, but mathematically fair.
    }
}
