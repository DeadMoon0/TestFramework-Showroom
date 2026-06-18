using FunctionApp;
using TestFramework.Azure.Identifier;
using TestFramework.Azure.Configuration.SpecificConfigs;
using TestFramework.Core.Timelines;
using TestFramework.Container.Azure;

namespace TestFramework.Showroom.Azure;

internal static class AzureShowroom
{
    private sealed class MainStorageDefinition : DockerStorageDefinition
    {
        public override StorageAccountIdentifier Identifier => "MainStorage";

        protected override string? BlobContainerName => "showroom-blob";
        protected override string? QueueContainerName => "showroom-queue";
        protected override string? TableContainerName => "MainTable";
    }

    private sealed class MainDbDefinition : DockerCosmosDefinition<CandidateProfile>
    {
        public override CosmosContainerIdentifier Identifier => "MainDb";

        protected override string? DatabaseName => "BaseDB";
        protected override string? ContainerName => "BaseContainer";
    }

    private sealed class MainSqlDefinition : DockerSqlDefinition
    {
        public override SqlDatabaseIdentifier Identifier => "MainSql";

        protected override string? DatabaseName => "master";
    }

    private sealed class MainSbQueueDefinition : DockerServiceBusDefinition
    {
        public override ServiceBusIdentifier Identifier => "MainSBQueue";

        public DockerServiceBusEndpoint Queue => DockerServiceBusEndpoint.Queue("sbq-main");

        protected override ServiceBusConfig? CreateDefaultConfig()
            => BuildConfig(DockerAzureDefaults.PlaceholderConnectionString, Queue);
    }

    private sealed class MainSbTopicDefinition : DockerServiceBusDefinition
    {
        public override ServiceBusIdentifier Identifier => "MainSBTopic";

        public DockerServiceBusEndpoint Subscription => DockerServiceBusEndpoint.TopicSubscription("sbt-main", "Default");

        protected override ServiceBusConfig? CreateDefaultConfig()
            => BuildConfig(DockerAzureDefaults.PlaceholderConnectionString, Subscription);
    }

    private sealed class SampleSubmissionBusDefinition : DockerServiceBusDefinition
    {
        public override ServiceBusIdentifier Identifier => "SampleSubmission";

        public DockerServiceBusEndpoint Topic => DockerServiceBusEndpoint.Topic("sbt-int-in");

        protected override ServiceBusConfig? CreateDefaultConfig()
            => BuildConfig(DockerAzureDefaults.PlaceholderConnectionString, Topic);

        protected override void ConfigureServiceBusTopology(DockerServiceBusTopologyBuilder builder)
            => ConfigureShowroomServiceBusTopology(builder);
    }

    private sealed class ProcessingReplyBusDefinition : DockerServiceBusDefinition
    {
        public override ServiceBusIdentifier Identifier => "ProcessingReply";

        public DockerServiceBusEndpoint Reply => DockerServiceBusEndpoint.TopicSubscription("sbt-int-out", "Default");

        protected override ServiceBusConfig? CreateDefaultConfig()
            => BuildConfig(DockerAzureDefaults.PlaceholderConnectionString, Reply);

        protected override void ConfigureServiceBusTopology(DockerServiceBusTopologyBuilder builder)
            => ConfigureShowroomServiceBusTopology(builder);
    }
    private sealed class ShowroomIntegrationBusDefinition : DockerServiceBusDefinition
    {
        public override ServiceBusIdentifier Identifier => "ShowroomBus";

        public DockerServiceBusEndpoint Submission
            => DockerServiceBusEndpoint.TopicSubscription("sbt-int-in", "Default");

        public DockerServiceBusEndpoint Reply
            => DockerServiceBusEndpoint.TopicSubscription("sbt-int-out", "Default");

        protected override ServiceBusConfig? CreateDefaultConfig()
            => BuildConfig(DockerAzureDefaults.PlaceholderConnectionString, Submission);

        protected override void ConfigureServiceBusTopology(DockerServiceBusTopologyBuilder builder)
            => ConfigureShowroomServiceBusTopology(builder);
    }

    internal sealed class DefaultFunctionAppDefinition : DockerFunctionAppDefinition<AnalysisProcessor>
    {
        public override FunctionAppIdentifier Identifier => "Default";

        protected override void Configure(DockerFunctionAppBuilder builder)
        {
            builder
                .UseStorage<MainStorageDefinition>(tableNameSettingName: "StorageTableName")
                .UseCosmos<MainDbDefinition>()
                .UseServiceBusTrigger<ShowroomIntegrationBusDefinition>(d => d.Submission)
                .UseServiceBusReply<ShowroomIntegrationBusDefinition>(d => d.Reply);
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
        return DockerAzureEnvironment.For<DefaultFunctionAppDefinition>()
            .Include<MainStorageDefinition>()
            .Include<MainDbDefinition>()
            .Include<MainSqlDefinition>()
            .Include<MainSbQueueDefinition>()
            .Include<MainSbTopicDefinition>()
            .Include<SampleSubmissionBusDefinition>()
            .Include<ProcessingReplyBusDefinition>()
            .Include<ShowroomIntegrationBusDefinition>();
    }
}