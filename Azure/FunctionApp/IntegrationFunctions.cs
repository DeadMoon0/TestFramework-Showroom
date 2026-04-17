using Azure.Data.Tables;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace FunctionApp;

// ─── Sample processing support models ────────────────────────────────────────

/// <summary>
/// JSON payload the test sends to the Analysis Processor's HTTP trigger.
/// </summary>
public sealed record SampleAnalysisRequest(
    string RunId,
    string SampleDocId,
    string AnalysisReplyCorrelationId);

/// <summary>
/// Shape of the Cosmos document that Sample Ingestion registers.
/// Used by the Analysis Processor to verify cross-service data flow.
/// </summary>
internal sealed record CandidateProfileDoc(
    string id,
    string PartitionKey,
    string runId,
    string stage,
    string status);

// ─── Sample Ingestion: Service Bus trigger ────────────────────────────────────
// Triggered by a message on the "sbt-int-in" topic (sent by the test).
// Registers the candidate profile in Cosmos, then confirms on "sbt-int-out".

public class SampleIngestionFunction(
    CosmosClient cosmosClient,
    ServiceBusSender replyQueueSender,
    IConfiguration config)
{
    [Function("SampleIngestion")]
    public async Task Run(
        [ServiceBusTrigger(
            "%ServiceBusTriggerTopicName%",
            "%ServiceBusTriggerSubscriptionName%",
            Connection = "ServiceBusTriggerConnection")]
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions)
    {
        string runId              = message.Body.ToString();
        string replyCorrelationId = message.CorrelationId;

        string databaseName  = config["CosmosDatabaseName"]  ?? "BaseDB";
        string containerName = config["CosmosContainerName"] ?? "BaseContainer";
        Container container  = cosmosClient.GetDatabase(databaseName).GetContainer(containerName);

        // Register the candidate profile so the test can discover it via FindArtifactMulti.
        var profile = new
        {
            id           = $"sample-{runId}",
            PartitionKey = "samples",
            runId        = runId,
            stage        = "ingested",
            status       = "registered",
            processedAt  = DateTime.UtcNow.ToString("O"),
        };
        await container.UpsertItemAsync(profile, new PartitionKey("samples"));

        // Send the ingestion confirmation so the test's first WaitForEvent unblocks.
        await replyQueueSender.SendMessageAsync(new ServiceBusMessage($"sample ingested for run {runId}")
        {
            CorrelationId = replyCorrelationId,
            Subject       = "sample.ingested",
            ContentType   = "text/plain",
        });

        await messageActions.CompleteMessageAsync(message);
    }
}

// ─── Analysis Processor: HTTP trigger ────────────────────────────────────────
// Called by the test via AzureTF.Trigger.FunctionApp.Http.
// Reads the Cosmos candidate profile, writes the analysis result to Table Storage,
// then sends a completion confirmation.

public class AnalysisProcessor(
    CosmosClient cosmosClient,
    TableServiceClient tableServiceClient,
    ServiceBusSender replyQueueSender,
    IConfiguration config)
{
    [Function("AnalysisProcessing")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
    {
        using StreamReader reader = new(req.Body);
        string body = await reader.ReadToEndAsync();

        SampleAnalysisRequest? request = JsonSerializer.Deserialize<SampleAnalysisRequest>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (request is null)
            return new BadRequestObjectResult("Invalid payload. Expected RunId, SampleDocId, AnalysisReplyCorrelationId.");

        string databaseName  = config["CosmosDatabaseName"]  ?? "BaseDB";
        string containerName = config["CosmosContainerName"] ?? "BaseContainer";
        Container container  = cosmosClient.GetDatabase(databaseName).GetContainer(containerName);

        // Read the candidate profile — proves the analysis has data from the ingestion step.
        await container.ReadItemAsync<CandidateProfileDoc>(
            request.SampleDocId,
            new PartitionKey("samples"));

        // Write the analysis result to Table Storage.
        string tableName    = config["IntegrationTableName"] ?? "MainTable";
        TableClient tableClient = tableServiceClient.GetTableClient(tableName);
        await tableClient.CreateIfNotExistsAsync();
        await tableClient.UpsertEntityAsync(new TableEntity("samples", request.RunId)
        {
            ["Status"]      = "analysed",
            ["SampleDocId"] = request.SampleDocId,
            ["ProcessedAt"] = DateTime.UtcNow.ToString("O"),
        });

        // Send the analysis completion reply so the test's second WaitForEvent unblocks.
        await replyQueueSender.SendMessageAsync(new ServiceBusMessage($"analysis complete for run {request.RunId}")
        {
            CorrelationId = request.AnalysisReplyCorrelationId,
            Subject       = "analysis.complete",
            ContentType   = "text/plain",
        });

        return new OkObjectResult($"Analysis complete for run {request.RunId}");
    }
}

