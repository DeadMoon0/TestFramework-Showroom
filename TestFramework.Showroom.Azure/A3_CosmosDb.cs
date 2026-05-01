using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using TestFramework.Azure;
using TestFramework.Azure.Extensions;
using TestFramework.Config;
using TestFramework.Core.Timelines;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Azure;

// ══════════════════════════════════════════════════════════════════════════════
//  CLOUD INFRASTRUCTURE DIVISION — PARTICIPANT ORIENTATION MODULE A3
//  "Cosmos DB: The Document Store For People Who Find Tables Too Limiting"
//
//  Good news: Cosmos DB stores your data as JSON documents.
//  More good news: it scales to billions of documents.
//  The catch? You need a PartitionKey. Always. No exceptions. We tried exceptions.
//  The throughput bill arrived. We don't talk about that period anymore.
//
//  The identifier from this module: "MainDb"
//  The container from this module: "BaseContainer" in the "BaseDB" database.
//  Both configured in your settings file, which you are treating very carefully.
// ══════════════════════════════════════════════════════════════════════════════

// Your document model. Annotate with Newtonsoft JsonProperty — Cosmos uses it.
// Every document MUST have an "id" field. This is non-negotiable.
// We negotiated once. It did not go well for either party.
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

[Collection("AzureShowroom")]
public class CosmosDb_BasicUpsert(ITestOutputHelper outputHelper)
{
    // The simplest possible Cosmos operation: put something in. Verify it's in. Clean it up.
    // You will notice we said "clean it up." We meant: the framework cleans it up.
    // You are here to write assertions. That is your one job. Do it proudly.

    private static readonly Timeline _timeline = Timeline.Create()
        .SetupArtifact("cosmosDoc")
        // ^ The framework will UPSERT this document before the test runs.
        //   It will DELETE it in the cleanup stage.
        //   This is the deal. We shake on it every run.
        .Build();

    [Fact]
    public async Task Run()
    {
        var configSub = AzureShowroom.BuildConfig();

        var run = await _timeline
            .SetupRun(configSub.BuildServiceProvider(), outputHelper)
            .SetEnv(TestFramework.Container.Azure.DockerAzureEnvironment.For<AzureShowroom.DefaultFunctionAppDefinition>())
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
        // ^ Assert it got in. If it didn't, the exception above already told you.
        //   This is your belt AND your suspenders. We believe in redundancy at scale.
    }
}

[Collection("AzureShowroom")]
public class CosmosDb_QueryFinder(ITestOutputHelper outputHelper)
{
    // When you need to find documents you didn't track by ID, use a query.
    // The query language is SQL-adjacent. "Adjacent" is doing a lot of work in that sentence.
    // Point is: SELECT * FROM c WHERE c.score = 42. You know how this goes.

    private static readonly Timeline _timeline = Timeline.Create()
        .SetupArtifact("candidate1")
        .SetupArtifact("candidate2")
        .SetupArtifact("candidate3")
        // Three items inserted. One query to find the high-scorers.
        // The rest are cleaned up too. We are thorough. Thoroughness is a core value.
        // It's in our brochure. The brochure is very thorough.
        .FindArtifactMulti(
            ["topScorers"],
            AzureTF.ArtifactFinder.DB.CosmosQuery<CosmosShowroomItem>(
                "MainDb",
                new QueryDefinition("SELECT * FROM c WHERE c.score = 99 AND c.PartitionKey = 'showroom-query'")))
        .Build();

    [Fact]
    public async Task Run()
    {
        var configSub = AzureShowroom.BuildConfig();

        var run = await _timeline
            .SetupRun(configSub.BuildServiceProvider(), outputHelper)
            .SetEnv(TestFramework.Container.Azure.DockerAzureEnvironment.For<AzureShowroom.DefaultFunctionAppDefinition>())
            .AddCosmosItemArtifact("candidate1", "MainDb",
                new CosmosShowroomItem { Id = "q-001", PartitionKey = "showroom-query", Name = "High Achiever A", Score = 99 })
            .AddCosmosItemArtifact("candidate2", "MainDb",
                new CosmosShowroomItem { Id = "q-002", PartitionKey = "showroom-query", Name = "High Achiever B", Score = 99 })
            .AddCosmosItemArtifact("candidate3", "MainDb",
                new CosmosShowroomItem { Id = "q-003", PartitionKey = "showroom-query", Name = "Average Achiever", Score = 40 })
            //                                                                                                              ^ 40 is a completely respectable score.
            //                                                                                                                We would never filter someone out for scoring 40.
            //                                                                                                                (We filtered them out.)
            .RunAsync();

        run.EnsureRanToCompletion();

        // Results use the base name for the first match, then _1, _2, ... for subsequent matches.
        run.CosmosArtifact<CosmosShowroomItem>("topScorers").Should().Exist();
        run.CosmosArtifact<CosmosShowroomItem>("topScorers_1").Should().Exist();
        // Candidate 3 (score: 40) should not appear in results.
        // We trust the query. The query has never let us down.
        // We say this with full awareness that the query ran maybe four times.
    }
}
