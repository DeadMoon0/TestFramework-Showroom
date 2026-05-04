using FunctionApp;
using TestFramework.Azure.Configuration.SpecificConfigs;
using TestFramework.Azure.Identifier;
using TestFramework.Core.Timelines;
using TestFramework.Container.Azure;

namespace TestFramework.Showroom.Azure;

internal static class AzureShowroom
{
    private abstract class ShowroomStorageDefinition : DockerStorageDefinition
    {
        protected sealed override StorageAccountConfig? CreateDefaultConfig() => CreateConfig();

        protected abstract StorageAccountConfig CreateConfig();
    }

    private abstract class ShowroomCosmosDefinition<TDocument> : DockerCosmosDefinition<TDocument>
    {
        protected sealed override CosmosContainerDbConfig? CreateDefaultConfig() => CreateConfig();

        protected abstract CosmosContainerDbConfig CreateConfig();
    }

    private abstract class ShowroomSqlDefinition : DockerSqlDefinition
    {
        protected sealed override SqlDatabaseConfig? CreateDefaultConfig() => CreateConfig();

        protected abstract SqlDatabaseConfig CreateConfig();
    }

    private abstract class ShowroomServiceBusDefinition : DockerServiceBusDefinition
    {
        protected sealed override ServiceBusConfig? CreateDefaultConfig() => CreateConfig();

        protected abstract ServiceBusConfig CreateConfig();
    }

    internal abstract class ShowroomFunctionAppDefinition<TFunctionApp> : DockerFunctionAppDefinition<TFunctionApp>
    {
        protected sealed override FunctionAppConfig? CreateDefaultConfig() => CreateConfig();

        protected abstract FunctionAppConfig CreateConfig();
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

        protected override void ConfigureServiceBusTopology(DockerServiceBusTopologyBuilder builder)
            => ConfigureShowroomServiceBusTopology(builder);

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

        protected override void ConfigureServiceBusTopology(DockerServiceBusTopologyBuilder builder)
            => ConfigureShowroomServiceBusTopology(builder);

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
                .UseStorage<MainStorageDefinition>(tableNameSettingName: "StorageTableName")
                .UseCosmos<MainDbDefinition>()
                .UseServiceBusTrigger<SampleSubmissionDefinition>()
                .UseServiceBusReply<ProcessingReplyDefinition>();
        }
    }

    private static void ConfigureShowroomServiceBusTopology(DockerServiceBusTopologyBuilder builder)
    {
        builder.AddNamespace("sbemulatorns", ns => ns
            .AddQueue("sbq-main")
            .AddTopic("sbt-main", topic => topic.AddSubscription("Default"))
            .AddTopic("sbt-int-in", topic => topic.AddSubscription("Default"))
            .AddTopic("sbt-int-out", topic => topic.AddSubscription("Default")));
    }

    internal static DockerAzureEnvironment CreateEnvironment()
    {
        return new DockerAzureEnvironment()
            .Include<MainStorageDefinition>()
            .Include<MainDbDefinition>()
            .Include<MainSqlDefinition>()
            .Include<MainSbQueueDefinition>()
            .Include<MainSbTopicDefinition>()
            .Include<DefaultFunctionAppDefinition>();
    }
}