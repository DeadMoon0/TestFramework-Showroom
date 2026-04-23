using System.Net.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Azure.Configuration;
using TestFramework.Azure.Configuration.SpecificConfigs;
using TestFramework.Azure.Extensions;
using TestFramework.Config;
using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Builder.TimelineRunBuilder;
using TestFramework.Container.AzureDocker;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Azure;

internal static class AzureShowroom
{
    private static readonly Dictionary<string, string?> DefaultConfig = new()
    {
        ["StorageAccount:MainStorage:ConnectionString"] = "UseDevelopmentStorage=true",
        ["StorageAccount:MainStorage:BlobContainerName"] = "showroom-blob",
        ["StorageAccount:MainStorage:QueueContainerName"] = "showroom-queue",
        ["StorageAccount:MainStorage:TableContainerName"] = "MainTable",
        ["CosmosDb:MainDb:ConnectionString"] = "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;",
        ["CosmosDb:MainDb:DatabaseName"] = "BaseDB",
        ["CosmosDb:MainDb:ContainerName"] = "BaseContainer",
        ["CosmosDb:MainDb:PartitionKeyPath"] = "/PartitionKey",
        ["SqlDatabase:MainSql:ConnectionString"] = "Server=localhost;Database=master;User Id=sa;Password=TestFramework_Container1!;TrustServerCertificate=True",
        ["SqlDatabase:MainSql:DatabaseName"] = "master",
        ["ServiceBus:MainSBQueue:ConnectionString"] = "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=local",
        ["ServiceBus:MainSBQueue:QueueName"] = "sbq-main",
        ["ServiceBus:MainSBQueue:RequiredSession"] = "false",
        ["ServiceBus:MainSBTopic:ConnectionString"] = "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=local",
        ["ServiceBus:MainSBTopic:TopicName"] = "sbt-main",
        ["ServiceBus:MainSBTopic:SubscriptionName"] = "Default",
        ["ServiceBus:MainSBTopic:RequiredSession"] = "false",
        ["ServiceBus:SampleSubmission:ConnectionString"] = "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=local",
        ["ServiceBus:SampleSubmission:TopicName"] = "sbt-int-in",
        ["ServiceBus:SampleSubmission:SubscriptionName"] = "Default",
        ["ServiceBus:SampleSubmission:RequiredSession"] = "false",
        ["ServiceBus:ProcessingReply:ConnectionString"] = "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=local",
        ["ServiceBus:ProcessingReply:TopicName"] = "sbt-int-out",
        ["ServiceBus:ProcessingReply:SubscriptionName"] = "Default",
        ["ServiceBus:ProcessingReply:RequiredSession"] = "false",
    };

    private static readonly ConfigInstance RootConfig = ConfigInstance.Create()
        .OverrideConfig(DefaultConfig)
        .Build();

    internal static ConfigInstance BuildConfig(Action<IServiceCollection, IConfiguration>? configureAdditionalServices = null)
    {
        return RootConfig.SetupSubInstance()
            .AddService((services, configuration) =>
            {
                RegisterAzureStores(services);
                services.ConfigureCosmosClientOptions(_ => new CosmosClientOptions
                {
                    ConnectionMode = ConnectionMode.Gateway,
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
            }));
    }

    private static void RegisterAzureStores(IServiceCollection services)
    {
        services.AddSingleton(CreateStore("MainStorage", new StorageAccountConfig
        {
            ConnectionString = DefaultConfig["StorageAccount:MainStorage:ConnectionString"]!,
            BlobContainerName = DefaultConfig["StorageAccount:MainStorage:BlobContainerName"],
            QueueContainerName = DefaultConfig["StorageAccount:MainStorage:QueueContainerName"],
            TableContainerName = DefaultConfig["StorageAccount:MainStorage:TableContainerName"],
        }));

        services.AddSingleton(CreateStore("MainDb", new CosmosContainerDbConfig
        {
            ConnectionString = DefaultConfig["CosmosDb:MainDb:ConnectionString"]!,
            DatabaseName = DefaultConfig["CosmosDb:MainDb:DatabaseName"]!,
            ContainerName = DefaultConfig["CosmosDb:MainDb:ContainerName"]!,
            PartitionKeyPath = DefaultConfig["CosmosDb:MainDb:PartitionKeyPath"],
        }));

        services.AddSingleton(CreateStore("MainSql", new SqlDatabaseConfig
        {
            ConnectionString = DefaultConfig["SqlDatabase:MainSql:ConnectionString"]!,
            DatabaseName = DefaultConfig["SqlDatabase:MainSql:DatabaseName"]!,
        }));

        ConfigStore<ServiceBusConfig> serviceBusStore = new();
        serviceBusStore.AddConfig("MainSBQueue", new ServiceBusConfig
        {
            ConnectionString = DefaultConfig["ServiceBus:MainSBQueue:ConnectionString"]!,
            QueueName = DefaultConfig["ServiceBus:MainSBQueue:QueueName"],
            TopicName = null,
            SubscriptionName = null,
            RequiredSession = false,
        });
        serviceBusStore.AddConfig("MainSBTopic", new ServiceBusConfig
        {
            ConnectionString = DefaultConfig["ServiceBus:MainSBTopic:ConnectionString"]!,
            QueueName = null,
            TopicName = DefaultConfig["ServiceBus:MainSBTopic:TopicName"],
            SubscriptionName = DefaultConfig["ServiceBus:MainSBTopic:SubscriptionName"],
            RequiredSession = false,
        });
        serviceBusStore.AddConfig("SampleSubmission", new ServiceBusConfig
        {
            ConnectionString = DefaultConfig["ServiceBus:SampleSubmission:ConnectionString"]!,
            QueueName = null,
            TopicName = DefaultConfig["ServiceBus:SampleSubmission:TopicName"],
            SubscriptionName = DefaultConfig["ServiceBus:SampleSubmission:SubscriptionName"],
            RequiredSession = false,
        });
        serviceBusStore.AddConfig("ProcessingReply", new ServiceBusConfig
        {
            ConnectionString = DefaultConfig["ServiceBus:ProcessingReply:ConnectionString"]!,
            QueueName = null,
            TopicName = DefaultConfig["ServiceBus:ProcessingReply:TopicName"],
            SubscriptionName = DefaultConfig["ServiceBus:ProcessingReply:SubscriptionName"],
            RequiredSession = false,
        });
        services.AddSingleton(serviceBusStore);
    }

    private static ConfigStore<TConfig> CreateStore<TConfig>(string identifier, TConfig config)
    {
        ConfigStore<TConfig> store = new();
        store.AddConfig(identifier, config);
        return store;
    }
}