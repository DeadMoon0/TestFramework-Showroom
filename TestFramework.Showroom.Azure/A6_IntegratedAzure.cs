using System.ComponentModel.DataAnnotations;
using System.Text;
using Azure;
using Azure.Data.Tables;
using Azure.Messaging.ServiceBus;
using FunctionApp;
using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using TestFramework.Azure;
using TestFramework.Azure.Configuration;
using TestFramework.Azure.Configuration.SpecificConfigs;
using TestFramework.Azure.DB.SqlServer;
using TestFramework.Azure.Extensions;
using TestFramework.Config;
using TestFramework.Container.Azure;
using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Azure;

//doc: This is where the services start needing each other.
//doc:
//doc: A1 through A5 were the polite introductions: one service at a time, isolated, easy to reason about.
//doc: That phase is over. Put the safety pamphlet down.
//doc:
//doc: A6 is the integrated system. Messages move, data crosses boundaries, and one component writes
//doc: something another component must deserve to trust. This is where a framework stops being a demo and
//doc: starts proving it can supervise an actual workflow.
//doc:
//doc: The pipeline:
//doc:
//doc: 1. The test seeds the submission manifest (a blob) and the SQL work order.
//doc: 2. A Service Bus message kicks off sample ingestion.
//doc: 3. The ingestion function writes the candidate profile to Cosmos and emits an acknowledgement.
//doc: 4. An HTTP call drives the analysis function.
//doc: 5. The analysis function reads Cosmos, writes Table Storage, and emits its own acknowledgement.
//doc: 6. The test retrieves artifacts from every layer and demands proof.
//doc:
//doc: Nothing here is assumed just because a previous call returned 200. That kind of optimism belongs in
//doc: product demos, not in integration tests or aerospace. Every stage boundary in that list is a wait on
//doc: a correlated acknowledgement, because "the HTTP call returned" and "the work finished" are different
//doc: facts about a distributed system.
//doc:
//doc: This is also the first chapter whose environment resolves to nearly everything -
//doc: `components [azure-reset, azurite, cosmos-emulator, functionapp, …]` - because for the first time
//doc: something in the run actually asks for all of it.

//doc: The data models are plain on purpose. The interesting part is not clever modelling, it is watching
//doc: data survive a trip through several systems without coming back with new opinions. Note that these
//doc: three types are the *contract* between the test and the functions: the ingestion function writes the
//doc: Cosmos document, the analysis function writes the Table entity, and the test asserts on both without
//doc: ever loading the functions' code.

/// <summary>Cosmos document written by the Sample Ingestion function when a new sample is registered.</summary>
public record CandidateProfile
{
    [JsonProperty("id")]
    public string Id { get; init; } = "";

    [JsonProperty("PartitionKey")]
    public string PartitionKey { get; init; } = "";

    [JsonProperty("runId")]
    public string RunId { get; init; } = "";

    [JsonProperty("stage")]
    public string Stage { get; init; } = "";

    [JsonProperty("status")]
    public string Status { get; init; } = "";
}

/// <summary>Table entity written by the Analysis Processor function when processing is complete.</summary>
public class AnalysisResult : ITableEntity
{
    public string PartitionKey { get; set; } = "";
    public string RowKey       { get; set; } = "";
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public string Status      { get; set; } = "";
    public string SampleDocId { get; set; } = "";
    public string ProcessedAt { get; set; } = "";
}

/// <summary>SQL row representing the scheduled work order for a sample batch.</summary>
public class LabWorkOrder
{
    [Key]
    public string RunId  { get; set; } = "";
    public string Stage  { get; set; } = "";
    public string Status { get; set; } = "";
}

public class LabDbContext(DbContextOptions<LabDbContext> options) : DbContext(options)
{
    public DbSet<LabWorkOrder> WorkOrders { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LabWorkOrder>().ToTable("LabWorkOrders");
    }
}

//doc: The config helper is chapter A5's pattern again, with a different `DbContext`. One builder centralises
//doc: the SQL and Azure setup so the test can focus on orchestration instead of building service providers
//doc: like a part-time plumber with trust issues.

// ─── Shared config helper ─────────────────────────────────────────────────────
// One config builder centralizes the SQL + Azure setup so the test can focus on
// orchestration instead of building service providers like a part-time plumber with trust issues.
internal static class LabSqlSetup
{
    internal static ConfigInstance BuildConfig() =>
        ConfigInstance.Create()
        .LoadDockerAzureConfig()
        .AddService(services =>
        {
            // The options handed to this callback already point at the database this run is using, so
            // nothing here reads an address. See chapter A5 for why AddDbContext cannot.
            services.AddSqlArtifactContexts(reg =>
            {
                reg.AddDefault<LabDbContext>(opts => new LabDbContext(opts));
                reg.ApplyMigrationsOnFirstUse();
            });
        })
        .Build();
}

//doc: Now the timeline, and it is the longest one in the Showroom. Read it as four movements - the comments
//doc: mark them - and notice that not one of them mentions an address, a connection string or a URL. Only
//doc: identifiers and variables.
//doc:
//doc: Three techniques in here are worth taking away, and they are all in the setup movement:
//doc:
//doc: - `SetupArtifact` for the two things the test owns. They exist before orchestration starts.
//doc: - `RegisterArtifact` for something that does **not exist yet**. The Table result is registered by
//doc:   reference now and materialised at the end with `CaptureArtifactVersion` - which is why the later
//doc:   capture step has to earn it rather than assume it.
//doc: - `FindArtifacts` with a *transformed* variable for the Cosmos query, so the query text is built from
//doc:   the run's own id rather than hardcoded. That is how a query can be part of a frozen plan and still be
//doc:   specific to one run.

// ══════════════════════════════════════════════════════════════════════════════
//  The Test Class
// ══════════════════════════════════════════════════════════════════════════════

public class LabOrchestration_CapabilityTour(ITestOutputHelper outputHelper)
{
    // One static timeline, many per-run identities. The structure stays fixed.
    // The values that make each run unique arrive later through variables, like good paperwork and bad surprises.
    private static readonly Timeline _timeline = Timeline.Create()

        // ═══ Step 1: Setup - artifacts the test controls ═════════════════════

        .SetupArtifact("sampleManifest")
        // ^ Blob manifest first. If the batch has no intake document, the rest
        //   of the pipeline is just a very expensive rumor with cloud billing.

        .SetupArtifact("workOrder")
        // ^ SQL work order second. The record exists before orchestration starts.
        //   It stays pending here because this chapter is about cross-service
        //   flow, not SQL mutation side quests.

        .RegisterArtifact("analysisResult",
            AzureExt.Artifact.StorageAccount.TableRef<AnalysisResult>(
                "MainStorage",
                Var.Ref<string>("tableName"),
                Var.Ref<string>("tablePartitionKey"),
                Var.Ref<string>("tableRowKey")))
        // ^ Register the future Table result by reference. It does not exist yet.
        //   Good. That means the later capture step will have to earn it.

        // ═══ Step 2: Trigger - Service Bus into ingestion ═════════════════════

        .Trigger(
            AzureExt.Trigger.ServiceBus.Send(
                "SampleSubmission",
                Var.Ref<ServiceBusMessage>("ingestionMessage")))
        // ^ Fire the ingestion message. This is the point where the test stops
        //   preparing and starts making demands of the system.

        .WaitForEvent(
            AzureExt.Event.ServiceBus.MessageReceived(
                "ProcessingReply",
                correlationId: Var.Ref<string>("ingestionReplyCorrelationId"),
                completeMessage: true))
            .WithTimeOut(TimeSpan.FromSeconds(20))
        // ^ Wait for the acknowledgement. Not because waiting is fun. Because if
        //   ingestion has not finished, the next query is just guessing.

        .FindArtifacts(
            "sample",
            AzureExt.ArtifactFinder.DB.CosmosQuery<CandidateProfile>(
                "MainDb",
                Var.Ref<string>("tableRowKey").Transform(key =>
                    new QueryDefinition(
                        "SELECT * FROM c WHERE c.runId = @rid AND c.stage = 'ingested' AND c.PartitionKey = 'samples'")
                        .WithParameter("@rid", key))))
        // ^ Query Cosmos for the profile the ingestion stage claims to have
        //   written. Results come back as numbered artifacts. Evidence, not lore, not vibes.

        // ═══ Step 3: Trigger - HTTP into analysis ═════════════════════════════

        .Trigger(
            AzureExt.Trigger.FunctionApp
                .Http("Default")
                .SelectEndpointWithMethod<AnalysisProcessor>(nameof(AnalysisProcessor.Run))
                .WithBody(Var.Ref<string>("analysisRequest"))
                .Call())
        // ^ Drive the analysis function over HTTP. At this point it must read the
        //   Cosmos profile produced earlier and convert that into a Table result.

        .WaitForEvent(
            AzureExt.Event.ServiceBus.MessageReceived(
                "ProcessingReply",
                correlationId: Var.Ref<string>("analysisReplyCorrelationId"),
                completeMessage: true))
            .WithTimeOut(TimeSpan.FromSeconds(20))
        // ^ Wait again, because a distributed workflow does not become complete
        //   just because an HTTP request returned looking proud of itself.

        // ═══ Step 4: Collect - fetch the Table result ═════════════════════════

        .CaptureArtifactVersion("analysisResult")
        // ^ Promote the Table reference into populated artifact data. This is the
        //   moment the test cashes the promise it registered earlier. Finance would be thrilled.

        .Build();

    //doc: And the run, which is where every identifier in that plan gets its value. The first line is the
    //doc: one to copy: a short per-run id, and everything else derived from it - the blob path, the SQL key,
    //doc: the table row key, both correlation IDs. Concurrent executions then cannot collide and blame each
    //doc: other in the logs like exhausted coworkers.
    //doc:
    //doc: The assertions at the end are deliberately arranged by source rather than by importance: blob, then
    //doc: Cosmos, then SQL, then Table. Each one answers a different question about the same run, and the
    //doc: last two are the load-bearing pair - the Table entity's `SampleDocId` is the id the *test* chose
    //doc: and the *ingestion* function wrote to Cosmos and the *analysis* function read back out. Blob to
    //doc: Service Bus to Cosmos to HTTP to Table, with every link forced to testify under assertion and none
    //doc: of them allowed a lawyer.
    //doc:
    //doc: One assertion is interesting for what it does *not* claim: the SQL work order is still `pending`.
    //doc: This chapter is about cross-service flow, not SQL mutation, and asserting that something correctly
    //doc: stayed unchanged is a real assertion.

    [DockerFact]
    [Trait("Category", "DockerSmoke")]
    public async Task Run()
    {
        // Every identifier hangs off the runId so concurrent executions do not
        // collide and then blame each other in the logs like exhausted coworkers.
        string runId         = Guid.NewGuid().ToString("N")[..12];
        string sampleDocId = $"sample-{runId}";    // matches SampleIngestion's UpsertItemAsync id

        var configSub = LabSqlSetup.BuildConfig();

        // The provider is owned by this test, so it is named and disposed rather than
        // built inline and abandoned. BuildServiceProvider returns the concrete
        // ServiceProvider to make that ownership visible.
        await using ServiceProvider provider = configSub.BuildServiceProvider();

        var run = await _timeline
            .SetupRun(provider, outputHelper)
            .SetEnv(AzureShowroom.CreateEnvironment())

            // ── Step 1: Seed artifacts ────────────────────────────────────────

            .AddBlobArtifact(
                "sampleManifest",
                "MainStorage",
                $"samples/{runId}/manifest.json",
                Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new { runId })),
                new Dictionary<string, string> { ["sample_id"] = runId })
            // ^ Seed the batch manifest. Small payload, important signal.

            .AddSqlArtifact(
                "workOrder",
                "MainSql",
                new LabWorkOrder { RunId = runId, Stage = "lab", Status = "pending" },
                Var.Const(runId))
            // ^ Seed the SQL work order. Still pending. Still honest.

            // ── Table artifact location ───────────────────────────────────────
            .AddVariable("tableName",         "MainTable")
            .AddVariable("tablePartitionKey", "samples")
            .AddVariable("tableRowKey",       runId)
            // ^ Tell the framework where the analysis result must appear. Shared
            //   partition, unique row, zero ambiguity.

            // ── Step 2: Build the ingestion request ───────────────────────────
            .AddVariable("ingestionReplyCorrelationId", $"ingestion-{runId}")
            .AddVariable("analysisReplyCorrelationId",  $"analysis-{runId}")
            .AddVariable("ingestionMessage",
                new ServiceBusMessage(System.Text.Json.JsonSerializer.Serialize(new SampleIngestionRequest(
                    RunId: runId,
                    ReplyCorrelationId: $"ingestion-{runId}")))
                {
                    CorrelationId = $"ingestion-{runId}",
                    Subject = "sample-submission",
                })
            // ^ Build the Service Bus message with the correlation IDs the waits
            //   will demand later.

            // ── Step 3: Build the analysis request ────────────────────────────
            .AddVariable("analysisRequest",
                System.Text.Json.JsonSerializer.Serialize(new SampleAnalysisRequest(
                    RunId:                      runId,
                    SampleDocId:                sampleDocId,
                    AnalysisReplyCorrelationId: $"analysis-{runId}")))
            // ^ Tell the analysis function exactly which profile to consume.

            .RunAsync();

        run.EnsureRanToCompletion();
        // ^ If anything in the orchestration chain lied, this is where the run
        //   would make a scene. Good. Quiet failure is how legends begin.

        // ════════════════════════════════════════════════════════════════════
        // Collect the evidence and make the accusations
        // ════════════════════════════════════════════════════════════════════

        // Blob says the batch was seeded correctly.
        run.BlobArtifact("sampleManifest").Should().Exist();
        run.BlobArtifact("sampleManifest")
            .Metadata("sample_id")
            .Should().Be(runId);

        // Cosmos says ingestion actually registered the profile.
        run.CosmosArtifact<CandidateProfile>("sample_0").Should().Exist();
        run.CosmosArtifact<CandidateProfile>("sample_0")
            .Item(item => item.RunId)
            .Should().Be(runId);
        run.CosmosArtifact<CandidateProfile>("sample_0")
            .Item(item => item.Stage)
            .Should().Be("ingested");
        run.CosmosArtifact<CandidateProfile>("sample_0")
            .Item(item => item.Status)
            .Should().Be("registered");

        // SQL still holds the work order we started with.
        run.SqlArtifact<LabWorkOrder>("workOrder").Should().Exist();
        run.SqlArtifact<LabWorkOrder>("workOrder")
            .Row(row => row.Stage)
            .Should().Be("lab");
        run.SqlArtifact<LabWorkOrder>("workOrder")
            .Row(row => row.Status)
            .Should().Be("pending");

        // Table says the analysis processor finished the job using the profile
        // that came through the earlier stages.
        var tableData = run.ArtifactStore
            .GetTableEntityArtifact<AnalysisResult>("analysisResult").Last;
        Assert.NotNull(tableData);
        Assert.Equal("analysed",  tableData.Entity.Status);
        Assert.Equal(sampleDocId, tableData.Entity.SampleDocId);
        // ^ End to end: Blob to Service Bus to Cosmos to HTTP to Table. Every
        //   link forced to testify under assertion and none of them allowed a lawyer.
    }
}
