using FunctionApp;
using TestFramework.Azure.Identifier;
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

        protected override DockerServiceBusEndpoint? Endpoint => DockerServiceBusEndpoint.Queue("sbq-main");
    }

    private sealed class MainSbTopicDefinition : DockerServiceBusDefinition
    {
        public override ServiceBusIdentifier Identifier => "MainSBTopic";

        protected override DockerServiceBusEndpoint? Endpoint => DockerServiceBusEndpoint.TopicSubscription("sbt-main", "Default");
    }

    private sealed class ProcessingReplyDefinition : DockerServiceBusDefinition
    {
        public override ServiceBusIdentifier Identifier => "ProcessingReply";

        protected override DockerServiceBusEndpoint? Endpoint => DockerServiceBusEndpoint.TopicSubscription("sbt-int-out", "Default");

        protected override void ConfigureServiceBusTopology(DockerServiceBusTopologyBuilder builder)
            => ConfigureShowroomServiceBusTopology(builder);
    }

    private sealed class SampleSubmissionDefinition : DockerServiceBusDefinition
    {
        public override ServiceBusIdentifier Identifier => "SampleSubmission";

        protected override DockerServiceBusEndpoint? Endpoint => DockerServiceBusEndpoint.TopicSubscription("sbt-int-in", "Default");

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
        return DockerAzureEnvironment.For<DefaultFunctionAppDefinition>()
            .Include<MainStorageDefinition>()
            .Include<MainDbDefinition>()
            .Include<MainSqlDefinition>()
            .Include<MainSbQueueDefinition>()
            .Include<MainSbTopicDefinition>()
            .Include<ProcessingReplyDefinition>()
            .Include<SampleSubmissionDefinition>();
    }
}