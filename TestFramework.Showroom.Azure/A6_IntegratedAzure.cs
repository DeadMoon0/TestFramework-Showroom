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
using TestFramework.Azure.DB.SqlServer;
using TestFramework.Azure.Extensions;
using TestFramework.Config;
using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Azure;

// ══════════════════════════════════════════════════════════════════════════════
//  LABORATORY SAMPLE PROCESSING SYSTEM — MODULE A6
//  "Full-Stack Orchestration: From Submission to Analysis"
//
//  Modules A1 through A5 demonstrated each Azure service in isolation.
//  Controlled environments. Predictable. Comfortable. Nothing breaking.
//  This module is the uncontrolled environment. Welcome.
//
//  A6 orchestrates a complete sample processing pipeline across six services:
//    1. Setup:    The test submits a sample manifest (Blob) and work order (SQL).
//    2. Trigger:  A Service Bus message initiates sample ingestion (Phase 1 Function).
//    3. Wait:     Phase 1 registers the candidate profile in Cosmos and confirms.
//    4. Trigger:  An HTTP call drives the Analysis Processor (Phase 2 Function),
//                 which reads the profile, writes the result to Table Storage, and confirms.
//    5. Collect:  The framework retrieves artifacts from every storage layer.
//    6. Validate: Every result is asserted. None are assumed.
//
//  INFRASTRUCTURE PREREQUISITES (beyond A1–A5):
//    In Azure:
//      • Service Bus Topic "sbt-int-in"  (non-session) + subscription "Default"  — ingestion trigger
//      • Service Bus Topic "sbt-int-out" (non-session) + subscription "Default"  — processing replies
//      • Function App with SampleIngestion + AnalysisProcessing deployed
//    In Azure Function App settings:
//      ServiceBusTriggerConnection        → SB namespace connection string
//      ServiceBusTriggerTopicName         → sbt-int-in
//      ServiceBusTriggerSubscriptionName  → Default
//      ServiceBusReplyConnectionString    → SB namespace connection string
//      ServiceBusReplyTopicName           → sbt-int-out
//      CosmosDbConnectionString           → Cosmos account connection string
//      CosmosDatabaseName                 → BaseDB
//      CosmosContainerName                → BaseContainer
//      StorageAccountConnectionString     → Storage account connection string
//      IntegrationTableName               → MainTable
//    In local.testSettings.json:
//      FunctionApp:Default:BaseUrl + Code → deployed function app URL and key
//      ServiceBus:SampleSubmission        → sbt-int-in  (non-session)
//      ServiceBus:ProcessingReply         → sbt-int-out (non-session)
// ══════════════════════════════════════════════════════════════════════════════

// ─── Data models ──────────────────────────────────────────────────────────────

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

// ─── Shared config helper (same pattern as ShowroomSqlSetup in A5) ────────────
internal static class LabSqlSetup
{
    internal static ConfigInstance BuildConfig(ConfigInstance root) =>
        root.SetupSubInstance()
            .LoadAzureConfig()
            .AddService((services, config) =>
            {
                services.AddDbContext<LabDbContext>(opts =>
                    opts.UseSqlServer(config["SqlDatabase:MainSql:ConnectionString"]));

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

[Collection("LabOrchestration")]
public class LabOrchestration_CapabilityTour(ITestOutputHelper outputHelper)
{
    private static readonly ConfigInstance _config =
        ConfigInstance.FromJsonFile("local.testSettings.json").Build();

    // ── Static Timeline ───────────────────────────────────────────────────────
    // Declared once for all invocations. Per-run values (runId, correlation IDs,
    // queries, HTTP payloads) are injected at run time via Var.Ref<T>.
    private static readonly Timeline _timeline = Timeline.Create()

        // ═══ Step 1: Setup — artifacts the test controls ═════════════════════

        .SetupArtifact("sampleManifest")
        // ^ Blob Storage: the sample submission document.
        //   Uploaded before any function runs. Tagged with the sample ID.
        //   Represents the initial intake record for the batch.

        .SetupArtifact("workOrder")
        // ^ SQL Server: scheduled work order for this sample batch.
        //   Written by the test to represent the lab assignment.
        //   Status stays "pending" — this module validates orchestration, not SQL mutations.
        //   SQL mutations are Module A5's domain. We respect the boundaries.

        .RegisterArtifact("analysisResult",
            AzureTF.Artifact.StorageAccount.TableRef<AnalysisResult>(
                "MainStorage",
                Var.Ref<string>("tableName"),
                Var.Ref<string>("tablePartitionKey"),
                Var.Ref<string>("tableRowKey")))
        // ^ Table Storage: the result the Analysis Processor will write.
        //   Registered by reference now (partition key + row key injected at runtime).
        //   The entity doesn't exist yet — the framework fetches it via
        //   CaptureArtifactVersion after the Analysis Processor completes.

        // ═══ Step 2: Trigger — Service Bus message → Sample Ingestion function ═

        .Trigger(
            AzureTF.Trigger.ServiceBus.Send(
                "SampleSubmission",
                Var.Ref<ServiceBusMessage>("submissionMsg")))
        // ^ Sends the ingestion trigger to "sbt-int-in".
        //   The Sample Ingestion function consumes it, registers the candidate profile
        //   in Cosmos, and sends a confirmation reply to "sbt-int-out".

        // ═══ Step 3: Wait — ingestion confirmation from Sample Ingestion ═══════

        .WaitForEvent(
            AzureTF.Event.ServiceBus.MessageReceived(
                "ProcessingReply",
                correlationId:   Var.Ref<string>("ingestionCorrelation"),
                completeMessage: true))
            .WithTimeOut(TimeSpan.FromSeconds(60))
        // ^ Waits on "sbt-int-out" for the ingestion confirmation.
        //   When this unblocks, the Cosmos candidate profile exists.

        .FindArtifactMulti(
            ["sample"],
            AzureTF.ArtifactFinder.DB.CosmosQuery<CandidateProfile>(
                "MainDb",
                Var.Ref<string>("tableRowKey").Transform(key =>
                    new QueryDefinition(
                        "SELECT * FROM c WHERE c.runId = @rid AND c.stage = 'ingested' AND c.PartitionKey = 'samples'")
                        .WithParameter("@rid", key))))
        // ^ Queries Cosmos for the profile the Sample Ingestion function registered.
        //   Query is derived at run time from tableRowKey (=runId) via Transform.
        //   Results arrive as: sample_0, sample_1, etc.

        // ═══ Step 4: Trigger — HTTP call to Analysis Processor function ═══════

        .Trigger(
            AzureTF.Trigger.FunctionApp
                .Http("Default")
                .SelectEndpointWithMethod<AnalysisProcessor>(nameof(AnalysisProcessor.Run))
                .WithBody(Var.Ref<string>("analysisRequest"))
                .Call())
        // ^ HTTP POST to the Analysis Processor.
        //   Payload: { RunId, SampleDocId, AnalysisReplyCorrelationId }.
        //   The function reads the Cosmos profile (cross-service proof), writes the
        //   Table result, and sends a completion confirmation.

        .WaitForEvent(
            AzureTF.Event.ServiceBus.MessageReceived(
                "ProcessingReply",
                correlationId:   Var.Ref<string>("analysisCorrelation"),
                completeMessage: true))
            .WithTimeOut(TimeSpan.FromSeconds(60))
        // ^ Waits for the analysis completion reply.
        //   When this unblocks, the Table entity has been written.
        //   The full chain is confirmed: SB → Cosmos → HTTP → Table → SB.

        // ═══ Step 5: Collect — fetch the Table entity now that it exists ══════

        .CaptureArtifactVersion("analysisResult")
        // ^ Reads the Table entity the Analysis Processor wrote.
        //   Transitions "analysisResult" from a reference pointer to populated data.

        .Build();

    [Fact]
    public async Task Run()
    {
        // ── Per-run identity ──────────────────────────────────────────────────
        // All artifact keys, Cosmos queries, and correlation IDs are derived
        // from the run ID. Concurrent test runs remain fully isolated.
        string runId         = Guid.NewGuid().ToString("N")[..12];
        string sampleDocId   = $"sample-{runId}";    // matches SampleIngestion's UpsertItemAsync id
        string ingestionCorr = $"ing-{runId}";        // SampleIngestion echoes this on its reply
        string analysisCorr  = $"ana-{runId}";        // AnalysisProcessor echoes this on its reply

        var configSub = LabSqlSetup.BuildConfig(_config);

        var run = await _timeline.SetupRun(configSub.BuildServiceProvider(), outputHelper)

            // ── Step 1: Setup artifacts ───────────────────────────────────────

            .AddBlobArtifact(
                "sampleManifest",
                "MainStorage",
                $"samples/{runId}/manifest.json",
                Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new { runId })),
                new Dictionary<string, string> { ["sample_id"] = runId })
            // ^ Sample manifest: lightweight JSON tagged with the sample ID.
            //   Represents the initial intake document for the batch.

            .AddSqlArtifact(
                "workOrder",
                "MainSql",
                new LabWorkOrder { RunId = runId, Stage = "lab", Status = "pending" },
                Var.Const(runId))
            // ^ Work order: scheduled lab assignment. Status = "pending" (set at insert, unchanged).

            // ── Table artifact reference variables ────────────────────────────
            .AddVariable("tableName",         "MainTable")
            .AddVariable("tablePartitionKey", "samples")
            .AddVariable("tableRowKey",       runId)
            // ^ Tells the framework where the Analysis Processor will write the Table entity.
            //   PartitionKey = "samples" (shared across all A6 runs, unique by RowKey).
            //   RowKey = runId (one entity per run, no collisions, clean cleanup).

            // ── Step 2: Sample ingestion trigger message ──────────────────────
            .AddVariable("submissionMsg",
                new ServiceBusMessage(runId)
                {
                    CorrelationId = ingestionCorr,   // SampleIngestion echoes this on its reply
                    Subject       = "sample.submitted",
                    MessageId     = $"sub-{runId}",
                })
            // ^ Trigger message. Body = runId (used by the function to key the Cosmos profile).
            //   CorrelationId = ingestionCorr (the test waits for this on the reply topic).

            // ── Step 3: Ingestion wait correlation ────────────────────────────
            .AddVariable("ingestionCorrelation", ingestionCorr)
            // ^ Cosmos query is derived at step time from tableRowKey via Transform (see Timeline above).

            // ── Step 4: Analysis request ──────────────────────────────────────
            .AddVariable("analysisRequest",
                System.Text.Json.JsonSerializer.Serialize(new SampleAnalysisRequest(
                    RunId:                      runId,
                    SampleDocId:                sampleDocId,
                    AnalysisReplyCorrelationId: analysisCorr)))
            .AddVariable("analysisCorrelation", analysisCorr)
            // ^ Tells the Analysis Processor: which run, which Cosmos profile to read,
            //   and what correlation ID to stamp on the completion reply.

            .RunAsync();

        run.EnsureRanToCompletion();
        // ^ Six services. Two functions. Two async confirmations. Zero ambiguity.
        //   Any failure throws with the full execution log: step, reason, elapsed time.

        // ════════════════════════════════════════════════════════════════════
        // Steps 5 + 6: Get Artifacts and Validate
        // ════════════════════════════════════════════════════════════════════

        // ── Blob: sample submission manifest ─────────────────────────────────
        run.BlobArtifact("sampleManifest").Should().Exist();
        run.BlobArtifact("sampleManifest")
            .Select(d => d.MetaData["sample_id"])
            .Should().Be(runId);

        // ── Cosmos: candidate profile registered by Sample Ingestion ──────────
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

        // ── SQL: lab work order ───────────────────────────────────────────────
        run.SqlArtifact<LabWorkOrder>("workOrder").Should().Exist();
        run.SqlArtifact<LabWorkOrder>("workOrder")
            .Select(d => d.Row.Stage)
            .Should().Be("lab");
        run.SqlArtifact<LabWorkOrder>("workOrder")
            .Select(d => d.Row.Status)
            .Should().Be("pending");

        // ── Table: analysis result written by Analysis Processor ─────────────
        var tableData = run.ArtifactStore
            .GetTableEntityArtifact<AnalysisResult>("analysisResult").Last;
        Assert.NotNull(tableData);
        Assert.Equal("analysed",  tableData.Entity.Status);
        Assert.Equal(sampleDocId, tableData.Entity.SampleDocId);
        // ^ Status = "analysed" proves the Analysis Processor completed.
        //   SampleDocId proves the processor read and used the Cosmos profile.
        //   That is the data flow: Blob → SB → Cosmos → HTTP → Table → SB.
        //   Every link confirmed. Real infrastructure. Real latency. Real confidence.

        // ── Service Bus: final reply confirmation ─────────────────────────────
        run.Variable<ServiceBusReceivedMessage>("out")
            .Should().Exist()
            .And().Match(
                m => m!.Subject == "analysis.complete",
                "The final reply from the Analysis Processor must carry Subject 'analysis.complete'.");
    }
}
