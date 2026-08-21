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

// ══════════════════════════════════════════════════════════════════════════════
//  CLOUD INFRASTRUCTURE DIVISION - PARTICIPANT ORIENTATION MODULE A3
//  "JSON Documents, Partition Keys, And The Price Of Pretending They Are Optional"
//
//  Cosmos is where the showroom starts dealing in documents instead of rows.
//  Flexible schema, powerful queries, serious scaling potential. All of that is
//  true. So is the partition key requirement, which arrives like tax law and stays longer.
//
//  Ignore partitioning strategy long enough and eventually the system writes a
//  very expensive letter to whoever thought it was somebody else's problem. It may use metrics.
// ══════════════════════════════════════════════════════════════════════════════

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

public class CosmosDb_BasicUpsert(ITestOutputHelper outputHelper)
{
    // First example: upsert one document, verify it exists, and let cleanup take
    // responsibility for removing it afterward before it gets comfortable.

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

public class CosmosDb_QueryFinder(ITestOutputHelper outputHelper)
{
    // Second example: query for documents by property rather than exact id. That
    // is where Cosmos stops being a key-value shelf and starts being a search surface with opinions.

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
