using Microsoft.Azure.Functions.Worker;
using Azure.Data.Tables;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);
var configuration = builder.Configuration;

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Services.AddSingleton(sp =>
{
    string connectionString = GetRequiredSetting("CosmosDbConnectionString");
    return new CosmosClient(connectionString, new CosmosClientOptions
    {
        ConnectionMode = ConnectionMode.Gateway,
        LimitToEndpoint = true,
        HttpClientFactory = () => new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        }),
    });
});

builder.Services.AddSingleton(_ => new TableServiceClient(GetRequiredSetting("StorageAccountConnectionString")));

builder.Services.AddSingleton(_ => new ServiceBusClient(GetRequiredSetting("ServiceBusReplyConnectionString")));

builder.Build().Run();

string GetRequiredSetting(string key)
{
    return configuration[key] ?? throw new InvalidOperationException($"The required Function App setting '{key}' was not configured.");
}
