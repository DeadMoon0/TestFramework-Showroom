using Microsoft.Azure.Functions.Worker;
using Azure.Data.Tables;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// ── Integration function dependencies ────────────────────────────────────────
// CosmosClient for Phase 1 (creates document) and Phase 2 (reads document).
builder.Services.AddSingleton(sp =>
{
    string cs = builder.Configuration["CosmosDbConnectionString"]
        ?? throw new InvalidOperationException("CosmosDbConnectionString app setting is required.");
    return new CosmosClient(cs);
});

// TableServiceClient for Phase 2 (writes Table entity as its output artifact).
builder.Services.AddSingleton(sp =>
{
    string cs = builder.Configuration["StorageAccountConnectionString"]
        ?? throw new InvalidOperationException("StorageAccountConnectionString app setting is required.");
    return new TableServiceClient(cs);
});

// ServiceBusClient and a dedicated sender for the reply topic.
// The trigger consumes FROM sbt-int-in.  The sender writes TO sbt-int-out.
// Keeping them separate avoids functions accidentally consuming their own replies.
builder.Services.AddSingleton(sp =>
{
    string cs = builder.Configuration["ServiceBusReplyConnectionString"]
        ?? throw new InvalidOperationException("ServiceBusReplyConnectionString app setting is required.");
    return new ServiceBusClient(cs);
});
builder.Services.AddSingleton<ServiceBusSender>(sp =>
{
    string topicName = builder.Configuration["ServiceBusReplyTopicName"]
        ?? throw new InvalidOperationException("ServiceBusReplyTopicName app setting is required.");
    return sp.GetRequiredService<ServiceBusClient>().CreateSender(topicName);
});

builder.Build().Run();
