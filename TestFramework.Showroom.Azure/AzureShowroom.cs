using FunctionApp;
using TestFramework.Azure.Identifier;
using TestFramework.Azure.Configuration.SpecificConfigs;
using TestFramework.Core.Timelines;
using TestFramework.Container.Azure;
using TestFramework.Container.Sources;
using Xunit;

// Chapters in this lane run one at a time, for the same reasons the web lane does it in W0: the
// Function App is built from its project, and several chapters publishing that one project at the
// same moment contend over its obj/ whatever their output directories say - on the published
// Container this lane pins, the loser reports that the project "could not be published". Newer
// builds of the framework serialise that themselves, which makes this a cost question rather than a
// correctness one; either way three full Azure stacks at once is not what a reader came here for.
//
// The lane did not need this while a Function App shipped the build output behind a type. It does now
// that the payload is a declared source, because a declared project source is published rather than
// copied.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

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

    internal sealed class DefaultFunctionAppDefinition : DockerFunctionAppDefinition
    {
        public override FunctionAppIdentifier Identifier => "Default";

        public override ContainerSource Source =>
            ContainerSource.Project("../Azure/FunctionApp/FunctionApp.csproj").BuiltOnHost();
        // ^ Published on the host, because the payload is mounted into the Functions
        //   host image rather than run as an image of its own.

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