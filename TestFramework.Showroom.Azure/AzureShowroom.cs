using System.Net.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FunctionApp;
using TestFramework.Azure.Configuration;
using TestFramework.Azure.Configuration.SpecificConfigs;
using TestFramework.Azure.Extensions;
using TestFramework.Config;
using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Builder.TimelineRunBuilder;
using TestFramework.Container.Azure;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Azure;

internal static class AzureShowroom
{
    private static readonly StorageAccountConfig MainStorageConfig = new()
    {
        ConnectionString = "UseDevelopmentStorage=true",
        BlobContainerName = "showroom-blob",
        QueueContainerName = "showroom-queue",
        TableContainerName = "MainTable",
    };

    private static readonly CosmosContainerDbConfig MainCosmosConfig = new()
    {
        ConnectionString = "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;",
        DatabaseName = "BaseDB",
        ContainerName = "BaseContainer",
    };

    private static readonly SqlDatabaseConfig MainSqlConfig = new()
    {
        ConnectionString = "Server=localhost;Database=master;User Id=sa;Password=TestFramework_Container1!;TrustServerCertificate=True",
        DatabaseName = "master",
    };

    private static readonly FunctionAppConfig DefaultFunctionAppConfig = new()
    {
        BaseUrl = "http://localhost/",
        Code = "unused",
        AdminCode = "unused",
    };

    internal static ConfigInstance BuildConfig(Action<IServiceCollection, IConfiguration>? configureAdditionalServices = null)
    {
        return ConfigInstance.Create()
            .AddService((services, configuration) =>
            {
                RegisterAzureStores(services);
                services.ConfigureCosmosClientOptions(_ => new CosmosClientOptions
                {
                    ConnectionMode = ConnectionMode.Gateway,
                    LimitToEndpoint = true,
                    HttpClientFactory = () => new HttpClient(new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                    }),
                });

                configureAdditionalServices?.Invoke(services, configuration);
            })
            .Build();
    }

    internal static ITimelineRunBuilder SetupRun(Timeline timeline, IServiceProvider serviceProvider, ITestOutputHelper outputHelper)
    {
        return timeline.SetupRun(serviceProvider, outputHelper)
            .SetEnv(new DockerAzureEnvironment(new DockerAzureEnvironmentOptions
            {
                RequiredCosmosIdentifiers = ["MainDb"],
                ServiceBusTopologyConfigPath = Path.Combine("ShowroomAzure", "ServiceBus", "config.json"),
                FunctionApps =
                [
                    DockerFunctionAppRegistration.Create<AnalysisProcessor>("Default", app => app
                        .UseStorage("MainStorage")
                        .UseCosmos("MainDb")
                        .UseServiceBusTrigger("SampleSubmission")
                        .UseServiceBusReply("ProcessingReply"))
                ],
            }));
    }

    private static void RegisterAzureStores(IServiceCollection services)
    {
        services.AddSingleton(ConfigStore<StorageAccountConfig>.Create("MainStorage", MainStorageConfig));

        services.AddSingleton(ConfigStore<CosmosContainerDbConfig>.Create("MainDb", MainCosmosConfig));

        services.AddSingleton(ConfigStore<SqlDatabaseConfig>.Create("MainSql", MainSqlConfig));

        services.AddSingleton(ConfigStore<FunctionAppConfig>.Create("Default", DefaultFunctionAppConfig));

        ConfigStore<ServiceBusConfig> serviceBusStore = new();
        serviceBusStore.AddConfig("MainSBQueue", new ServiceBusConfig
        {
            ConnectionString = "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=local",
            QueueName = "sbq-main",
            TopicName = null,
            SubscriptionName = null,
            RequiredSession = false,
        });
        serviceBusStore.AddConfig("MainSBTopic", new ServiceBusConfig
        {
            ConnectionString = "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=local",
            QueueName = null,
            TopicName = "sbt-main",
            SubscriptionName = "Default",
            RequiredSession = false,
        });
        serviceBusStore.AddConfig("SampleSubmission", new ServiceBusConfig
        {
            ConnectionString = "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=local",
            QueueName = null,
            TopicName = "sbt-int-in",
            SubscriptionName = "Default",
            RequiredSession = false,
        });
        serviceBusStore.AddConfig("ProcessingReply", new ServiceBusConfig
        {
            ConnectionString = "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=local",
            QueueName = null,
            TopicName = "sbt-int-out",
            SubscriptionName = "Default",
            RequiredSession = false,
        });
        services.AddSingleton(serviceBusStore);
    }
}