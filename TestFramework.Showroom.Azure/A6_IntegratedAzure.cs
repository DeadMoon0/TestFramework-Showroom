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

// ══════════════════════════════════════════════════════════════════════════════
//  LABORATORY SAMPLE PROCESSING SYSTEM - MODULE A6
//  "This Is Where The Services Start Needing Each Other"
//
//  A1 through A5 were the polite introductions. One service at a time. Nice,
//  isolated, easy to reason about. That phase is over. Put the safety pamphlet down.
//
//  A6 is the integrated system. Messages move. Data crosses boundaries. One
//  component writes something another component must deserve to trust. This is
//  where a framework stops being a demo and starts proving it can supervise an
//  actual workflow.
//
//  The pipeline looks like this:
//    1. The test seeds the submission manifest and SQL work order.
//    2. Service Bus kicks off sample ingestion.
//    3. The ingestion function writes the candidate profile to Cosmos and emits
//       an acknowledgement.
//    4. An HTTP call drives the analysis function.
//    5. The analysis function reads Cosmos, writes Table Storage, and emits its
//       own acknowledgement.
//    6. The test retrieves artifacts from every layer and demands proof.
//
//  Nothing in this chapter is assumed just because a previous call returned 200.
//  That kind of optimism belongs in product demos, not integration tests or aerospace.
// ══════════════════════════════════════════════════════════════════════════════

// ─── Data models ──────────────────────────────────────────────────────────────
// The types are plain on purpose. The interesting part is not clever modeling.
// The interesting part is watching data survive a trip through multiple systems
// without coming back with new opinions.

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

// ─── Shared config helper ─────────────────────────────────────────────────────
// One config builder centralizes the SQL + Azure setup so the test can focus on
// orchestration instead of building service providers like a part-time plumber with trust issues.
internal static class LabSqlSetup
{
    internal static ConfigInstance BuildConfig() =>
        ConfigInstance.Create()
        .LoadDockerAzureConfig()
        .AddService((services, _) =>
        {
            services.AddDbContext<LabDbContext>((serviceProvider, opts) =>
                opts.UseSqlServer(serviceProvider.GetRequiredService<ConfigStore<SqlDatabaseConfig>>().GetConfig("MainSql").ConnectionString));

            services.AddSqlArtifactContexts(reg =>
            {
                reg.AddDefault<LabDbContext>();
                reg.ApplyMigrationsOnFirstUse();
            });
        })
        .Build();
}

// ══════════════════════════════════════════════════════════════════════════════
//  The Test Class
// ══════════════════════════════════════════════════════════════════════════════

[Collection("AzureShowroom")]
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
            AzureTF.Artifact.StorageAccount.TableRef<AnalysisResult>(
                "MainStorage",
                Var.Ref<string>("tableName"),
                Var.Ref<string>("tablePartitionKey"),
                Var.Ref<string>("tableRowKey")))
        // ^ Register the future Table result by reference. It does not exist yet.
        //   Good. That means the later capture step will have to earn it.

        // ═══ Step 2: Trigger - Service Bus into ingestion ═════════════════════

        .Trigger(
            AzureTF.Trigger.ServiceBus.Send(
                "SampleSubmission",
                Var.Ref<ServiceBusMessage>("ingestionMessage")))
        // ^ Fire the ingestion message. This is the point where the test stops
        //   preparing and starts making demands of the system.

        .WaitForEvent(
            AzureTF.Event.ServiceBus.MessageReceived(
                "ProcessingReply",
                correlationId: Var.Ref<string>("ingestionReplyCorrelationId"),
                completeMessage: true))
            .WithTimeOut(TimeSpan.FromSeconds(20))
        // ^ Wait for the acknowledgement. Not because waiting is fun. Because if
        //   ingestion has not finished, the next query is just guessing.

        .FindArtifacts(
            "sample",
            AzureTF.ArtifactFinder.DB.CosmosQuery<CandidateProfile>(
                "MainDb",
                Var.Ref<string>("tableRowKey").Transform(key =>
                    new QueryDefinition(
                        "SELECT * FROM c WHERE c.runId = @rid AND c.stage = 'ingested' AND c.PartitionKey = 'samples'")
                        .WithParameter("@rid", key))))
        // ^ Query Cosmos for the profile the ingestion stage claims to have
        //   written. Results come back as numbered artifacts. Evidence, not lore, not vibes.

        // ═══ Step 3: Trigger - HTTP into analysis ═════════════════════════════

        .Trigger(
            AzureTF.Trigger.FunctionApp
                .Http("Default")
                .SelectEndpointWithMethod<AnalysisProcessor>(nameof(AnalysisProcessor.Run))
                .WithBody(Var.Ref<string>("analysisRequest"))
                .Call())
        // ^ Drive the analysis function over HTTP. At this point it must read the
        //   Cosmos profile produced earlier and convert that into a Table result.

        .WaitForEvent(
            AzureTF.Event.ServiceBus.MessageReceived(
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

    [Fact]
    public async Task Run()
    {
        // Every identifier hangs off the runId so concurrent executions do not
        // collide and then blame each other in the logs like exhausted coworkers.
        string runId         = Guid.NewGuid().ToString("N")[..12];
        string sampleDocId = $"sample-{runId}";    // matches SampleIngestion's UpsertItemAsync id

        var configSub = LabSqlSetup.BuildConfig();

        var run = await _timeline
            .SetupRun(configSub.BuildServiceProvider(), outputHelper)
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
            .Select(d => d.MetaData["sample_id"])
            .Should().Be(runId);

        // Cosmos says ingestion actually registered the profile.
        run.CosmosArtifact<CandidateProfile>("sample_0").Should().Exist();
        run.CosmosArtifact<CandidateProfile>("sample_0")
            .Select(d => d.Item.RunId)
            .Should().Be(runId);
        run.CosmosArtifact<CandidateProfile>("sample_0")
            .Select(d => d.Item.Stage)
            .Should().Be("ingested");
        run.CosmosArtifact<CandidateProfile>("sample_0")
            .Select(d => d.Item.Status)
            .Should().Be("registered");

        // SQL still holds the work order we started with.
        run.SqlArtifact<LabWorkOrder>("workOrder").Should().Exist();
        run.SqlArtifact<LabWorkOrder>("workOrder")
            .Select(d => d.Row.Stage)
            .Should().Be("lab");
        run.SqlArtifact<LabWorkOrder>("workOrder")
            .Select(d => d.Row.Status)
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
