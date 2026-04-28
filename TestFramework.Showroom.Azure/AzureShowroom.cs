using System.Net.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FunctionApp;
using TestFramework.Azure.Configuration;
using TestFramework.Azure.Configuration.SpecificConfigs;
using TestFramework.Azure.Extensions;
using TestFramework.Azure.Identifier;
using TestFramework.Config;
using TestFramework.Core.Timelines;
using TestFramework.Container.Azure;

namespace TestFramework.Showroom.Azure;

internal static class AzureShowroom
{
    private abstract class ShowroomStorageDefinition : DockerStorageDefinition
    {
        protected abstract StorageAccountConfig CreateConfig();

        public void Register(IServiceCollection services)
        {
            services.AddSingleton(ConfigStore<StorageAccountConfig>.Create(Identifier, CreateConfig()));
        }
    }

    private abstract class ShowroomCosmosDefinition<TDocument> : DockerCosmosDefinition<TDocument>
    {
        protected abstract CosmosContainerDbConfig CreateConfig();

        public void Register(IServiceCollection services)
        {
            services.AddSingleton(ConfigStore<CosmosContainerDbConfig>.Create(Identifier, CreateConfig()));
        }
    }

    private abstract class ShowroomSqlDefinition : DockerSqlDefinition
    {
        protected abstract SqlDatabaseConfig CreateConfig();

        public void Register(IServiceCollection services)
        {
            services.AddSingleton(ConfigStore<SqlDatabaseConfig>.Create(Identifier, CreateConfig()));
        }
    }

    private abstract class ShowroomServiceBusDefinition : DockerServiceBusDefinition
    {
        protected abstract ServiceBusConfig CreateConfig();

        public void Register(ConfigStore<ServiceBusConfig> serviceBusStore)
        {
            serviceBusStore.AddConfig(Identifier, CreateConfig());
        }
    }

    internal abstract class ShowroomFunctionAppDefinition<TFunctionApp> : DockerFunctionAppDefinition<TFunctionApp>
    {
        protected abstract FunctionAppConfig CreateConfig();

        public void Register(IServiceCollection services)
        {
            services.AddSingleton(ConfigStore<FunctionAppConfig>.Create(Identifier, CreateConfig()));
        }
    }

    private sealed class MainStorageDefinition : ShowroomStorageDefinition
    {
        public override StorageAccountIdentifier Identifier => "MainStorage";

        protected override StorageAccountConfig CreateConfig() => new()
        {
            ConnectionString = "UseDevelopmentStorage=true",
            BlobContainerName = "showroom-blob",
            QueueContainerName = "showroom-queue",
            TableContainerName = "MainTable",
        };
    }

    private sealed class MainDbDefinition : ShowroomCosmosDefinition<CandidateProfile>
    {
        public override CosmosContainerIdentifier Identifier => "MainDb";

        protected override CosmosContainerDbConfig CreateConfig() => new()
        {
            ConnectionString = "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;",
            DatabaseName = "BaseDB",
            ContainerName = "BaseContainer",
        };
    }

    private sealed class MainSqlDefinition : ShowroomSqlDefinition
    {
        public override SqlDatabaseIdentifier Identifier => "MainSql";

        protected override SqlDatabaseConfig CreateConfig() => new()
        {
            ConnectionString = "Server=localhost;Database=master;User Id=sa;Password=TestFramework_Container1!;TrustServerCertificate=True",
            DatabaseName = "master",
        };
    }

    private sealed class MainSbQueueDefinition : ShowroomServiceBusDefinition
    {
        public override ServiceBusIdentifier Identifier => "MainSBQueue";

        protected override ServiceBusConfig CreateConfig() => new()
        {
            ConnectionString = "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=local",
            QueueName = "sbq-main",
            TopicName = null,
            SubscriptionName = null,
            RequiredSession = false,
        };
    }

    private sealed class MainSbTopicDefinition : ShowroomServiceBusDefinition
    {
        public override ServiceBusIdentifier Identifier => "MainSBTopic";

        protected override ServiceBusConfig CreateConfig() => new()
        {
            ConnectionString = "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=local",
            QueueName = null,
            TopicName = "sbt-main",
            SubscriptionName = "Default",
            RequiredSession = false,
        };
    }

    private sealed class ProcessingReplyDefinition : ShowroomServiceBusDefinition
    {
        public override ServiceBusIdentifier Identifier => "ProcessingReply";
        public override string TopologyConfigPath => Path.Combine("ShowroomAzure", "ServiceBus", "config.json");

        protected override ServiceBusConfig CreateConfig() => new()
        {
            ConnectionString = "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=local",
            QueueName = null,
            TopicName = "sbt-int-out",
            SubscriptionName = "Default",
            RequiredSession = false,
        };
    }

    private sealed class SampleSubmissionDefinition : ShowroomServiceBusDefinition
    {
        public override ServiceBusIdentifier Identifier => "SampleSubmission";
        public override string TopologyConfigPath => Path.Combine("ShowroomAzure", "ServiceBus", "config.json");

        protected override ServiceBusConfig CreateConfig() => new()
        {
            ConnectionString = "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=local",
            QueueName = null,
            TopicName = "sbt-int-in",
            SubscriptionName = "Default",
            RequiredSession = false,
        };
    }

    internal sealed class DefaultFunctionAppDefinition : ShowroomFunctionAppDefinition<AnalysisProcessor>
    {
        public override FunctionAppIdentifier Identifier => "Default";

        protected override FunctionAppConfig CreateConfig() => new()
        {
            BaseUrl = "http://localhost/",
            Code = "unused",
            AdminCode = "unused",
        };

        protected override void Configure(DockerFunctionAppBuilder builder)
        {
            builder
                .UseStorage<MainStorageDefinition>()
                .UseCosmos<MainDbDefinition>()
                .UseServiceBusTrigger<SampleSubmissionDefinition>()
                .UseServiceBusReply<ProcessingReplyDefinition>();
        }
    }

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

    private static void RegisterAzureStores(IServiceCollection services)
    {
        new MainStorageDefinition().Register(services);
        new MainDbDefinition().Register(services);
        new MainSqlDefinition().Register(services);
        new DefaultFunctionAppDefinition().Register(services);

        ConfigStore<ServiceBusConfig> serviceBusStore = new();
        new MainSbQueueDefinition().Register(serviceBusStore);
        new MainSbTopicDefinition().Register(serviceBusStore);
        new SampleSubmissionDefinition().Register(serviceBusStore);
        new ProcessingReplyDefinition().Register(serviceBusStore);
        services.AddSingleton(serviceBusStore);
    }
}