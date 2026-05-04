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

public sealed record SampleIngestionRequest(
    string RunId,
    string ReplyCorrelationId);

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
// Registers the candidate profile in Cosmos.

public class SampleIngestionFunction(
    CosmosClient cosmosClient,
    ServiceBusClient serviceBusClient,
    IConfiguration configuration)
{
    [Function("SampleIngestion")]
    public async Task Run(
        [ServiceBusTrigger("%ServiceBusTriggerTopicName%", "%ServiceBusTriggerSubscriptionName%", Connection = "ServiceBusTriggerConnection")] string body,
        CancellationToken cancellationToken)
    {
        SampleIngestionRequest? request = JsonSerializer.Deserialize<SampleIngestionRequest>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (request is null)
            throw new InvalidOperationException("Invalid sample ingestion payload. Expected RunId and ReplyCorrelationId.");

        string runId = request.RunId;

        string databaseName = GetRequiredSetting("CosmosDatabaseName");
        string containerName = GetRequiredSetting("CosmosContainerName");
        Database database = await cosmosClient.CreateDatabaseIfNotExistsAsync(databaseName, cancellationToken: cancellationToken);
        Container container = await database.CreateContainerIfNotExistsAsync(containerName, "/PartitionKey", cancellationToken: cancellationToken);

        // Register the candidate profile so the test can discover it via FindArtifacts.
        var profile = new
        {
            id           = $"sample-{runId}",
            PartitionKey = "samples",
            runId        = runId,
            stage        = "ingested",
            status       = "registered",
            processedAt  = DateTime.UtcNow.ToString("O"),
        };
        await container.UpsertItemAsync(profile, new PartitionKey("samples"), cancellationToken: cancellationToken);

        string replyEntityName = GetRequiredSetting("ServiceBusReplyTopicName");
        await using ServiceBusSender sender = serviceBusClient.CreateSender(replyEntityName);
        await sender.SendMessageAsync(new ServiceBusMessage($"Sample ingested for run {runId}")
        {
            CorrelationId = request.ReplyCorrelationId,
            Subject = "sample-ingested",
        }, cancellationToken).ConfigureAwait(false);
    }

    private string GetRequiredSetting(string key)
    {
        return configuration[key] ?? throw new InvalidOperationException($"The required Function App setting '{key}' was not configured.");
    }
}

// ─── Analysis Processor: HTTP trigger ────────────────────────────────────────
// Called by the test via AzureTF.Trigger.FunctionApp.Http.
// Reads the Cosmos candidate profile and writes the analysis result to Table Storage.

public class AnalysisProcessor(
    CosmosClient cosmosClient,
    TableServiceClient tableServiceClient,
    ServiceBusClient serviceBusClient,
    IConfiguration configuration)
{
    [Function("AnalysisProcessing")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        using StreamReader reader = new(req.Body);
        string body = await reader.ReadToEndAsync();

        SampleAnalysisRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<SampleAnalysisRequest>(
                body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid payload. Expected RunId, SampleDocId, AnalysisReplyCorrelationId.");
        }

        if (request is null
            || string.IsNullOrWhiteSpace(request.RunId)
            || string.IsNullOrWhiteSpace(request.SampleDocId)
            || string.IsNullOrWhiteSpace(request.AnalysisReplyCorrelationId))
            return new BadRequestObjectResult("Invalid payload. Expected RunId, SampleDocId, AnalysisReplyCorrelationId.");

        string databaseName = GetRequiredSetting("CosmosDatabaseName");
        string containerName = GetRequiredSetting("CosmosContainerName");
        Container container = cosmosClient.GetDatabase(databaseName).GetContainer(containerName);

        // Read the candidate profile — proves the analysis has data from the ingestion step.
        await container.ReadItemAsync<CandidateProfileDoc>(
            request.SampleDocId,
            new PartitionKey("samples"));

        // Write the analysis result to Table Storage.
        string tableName = GetRequiredSetting("StorageTableName");
        TableClient tableClient = tableServiceClient.GetTableClient(tableName);
        await tableClient.CreateIfNotExistsAsync();
        await tableClient.UpsertEntityAsync(new TableEntity("samples", request.RunId)
        {
            ["Status"]      = "analysed",
            ["SampleDocId"] = request.SampleDocId,
            ["ProcessedAt"] = DateTime.UtcNow.ToString("O"),
        });

        string replyEntityName = GetRequiredSetting("ServiceBusReplyTopicName");
        await using ServiceBusSender sender = serviceBusClient.CreateSender(replyEntityName);
        await sender.SendMessageAsync(new ServiceBusMessage($"Analysis complete for run {request.RunId}")
        {
            CorrelationId = request.AnalysisReplyCorrelationId,
            Subject = "analysis-complete",
        });

        return new OkObjectResult($"Analysis complete for run {request.RunId}");
    }

    private string GetRequiredSetting(string key)
    {
        return configuration[key] ?? throw new InvalidOperationException($"The required Function App setting '{key}' was not configured.");
    }
}

