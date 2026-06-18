using FunctionApp;
using TestFramework.Azure;
using TestFramework.Azure.Configuration.SpecificConfigs;
using TestFramework.Azure.Extensions;
using TestFramework.Azure.Identifier;
using TestFramework.Container.Azure;
using TestFramework.Config;
using TestFramework.Core.Exceptions;
using TestFramework.Core.Environment;
using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using Xunit.Abstractions;
using TestFramework.Container.Azure.Contracts;
namespace TestFramework.Showroom.Azure;

// ══════════════════════════════════════════════════════════════════════════════
//  CONTAINER ORCHESTRATION DIVISION - MODULE A7
//  "Several Apps. One Dependency Graph. No Delusions."
//
//  A6 proved the container-backed path can finish an end-to-end story. Fine.
//  The lights turned on. Everybody applauded. Now we can discuss what happens
//  when the environment stops being polite and starts being realistic and vaguely territorial.
//
//  Real systems do not arrive one Function App at a time. They arrive in packs,
//  each with demands, assumptions, and the occasional territorial dispute.
//  This chapter walks through the three outcomes that matter:
//    1. Shared dependencies can be reused when the graph allows it.
//    2. Contracts can force the right provider to be selected on purpose.
//    3. Exclusive claims must fail before the suite starts telling lies.
//
//  Fast failure is mercy. Late failure is paperwork and committee language.
// ══════════════════════════════════════════════════════════════════════════════

public class ComponentComposition_SharedDependenciesAndContracts(ITestOutputHelper outputHelper)
{
    private static readonly Timeline SharedDependenciesTimeline = Timeline.Create()
        .SetupArtifact("ingestDoc")
        .SetupArtifact("analyseDoc")
        .RegisterArtifact(
            "ingestResult",
            AzureExt.Artifact.StorageAccount.TableRef<AnalysisResult>(
                "SharedStorage",
                Var.Const("MainTable"),
                Var.Const("samples"),
                Var.Const("ingest-run")))
        .RegisterArtifact(
            "analyseResult",
            AzureExt.Artifact.StorageAccount.TableRef<AnalysisResult>(
                "SharedStorage",
                Var.Const("MainTable"),
                Var.Const("samples"),
                Var.Const("analyse-run")))
        .Trigger(
            AzureExt.Trigger.FunctionApp
                .Http("Ingest")
                .SelectEndpointWithMethod<AnalysisProcessor>(nameof(AnalysisProcessor.Run))
                .WithBody(Var.Ref<string>("ingestRequest"))
                .Call())
            .WithTimeOut(TimeSpan.FromMinutes(2))
        .Trigger(
            AzureExt.Trigger.FunctionApp
                .Http("Analyse")
                .SelectEndpointWithMethod<AnalysisProcessor>(nameof(AnalysisProcessor.Run))
                .WithBody(Var.Ref<string>("analyseRequest"))
                .Call())
            .WithTimeOut(TimeSpan.FromMinutes(2))
        .CaptureArtifactVersion("ingestResult")
        .CaptureArtifactVersion("analyseResult")
        .Build();

    private static readonly Timeline ContractSelectionTimeline = Timeline.Create()
        .SetupArtifact("contractDoc")
        .RegisterArtifact(
            "contractResult",
            AzureExt.Artifact.StorageAccount.TableRef<AnalysisResult>(
                "SharedStorage",
                Var.Const("MainTable"),
                Var.Const("samples"),
                Var.Const("contract-run")))
        .Trigger(
            AzureExt.Trigger.FunctionApp
                .Http("ContractConsumer")
                .SelectEndpointWithMethod<AnalysisProcessor>(nameof(AnalysisProcessor.Run))
                .WithBody(Var.Ref<string>("contractRequest"))
                .Call())
            .WithTimeOut(TimeSpan.FromMinutes(2))
        .WaitForEvent(
            AzureExt.Event.ServiceBus.MessageReceived(
                "ReplyBus",
                correlationId: Var.Const("a7-contract-reply"),
                completeMessage: true))
            .WithTimeOut(TimeSpan.FromSeconds(30))
        .CaptureArtifactVersion("contractResult")
        .Build();

    private static readonly Timeline ExclusiveDependenciesTimeline = Timeline.Create()
        .Trigger(AzureExt.Trigger.IsLive.FunctionApp("ExclusiveA"))
            .WithTimeOut(TimeSpan.FromMinutes(2))
        .Trigger(AzureExt.Trigger.IsLive.FunctionApp("ExclusiveB"))
            .WithTimeOut(TimeSpan.FromMinutes(2))
        .Build();

    [Fact]
    public async Task Shared_dependencies_are_reused_across_multiple_function_apps()
    {
        IServiceProvider serviceProvider = ConfigInstance.Create().LoadDockerAzureConfig().BuildServiceProvider();
        using IDisposable _ = (IDisposable)serviceProvider;
        DockerAzureEnvironment environment = DockerAzureEnvironment.For<IntakeFunctionAppDefinition>()
            .Include<AnalysisFunctionAppDefinition>();

        TimelineRun run = await SharedDependenciesTimeline
            .SetupRun(serviceProvider, outputHelper)
            .SetEnv(environment)
            .AddCosmosItemArtifact("ingestDoc", "SharedCosmos", new CandidateProfile
            {
                Id = "sample-ingest",
                PartitionKey = "samples",
                RunId = "ingest-run",
                Stage = "ingested",
                Status = "registered",
            })
            .AddCosmosItemArtifact("analyseDoc", "SharedCosmos", new CandidateProfile
            {
                Id = "sample-analyse",
                PartitionKey = "samples",
                RunId = "analyse-run",
                Stage = "ingested",
                Status = "registered",
            })
            .AddVariable("ingestRequest", System.Text.Json.JsonSerializer.Serialize(new SampleAnalysisRequest(
                RunId: "ingest-run",
                SampleDocId: "sample-ingest",
                AnalysisReplyCorrelationId: "a7-ingest-reply")))
            .AddVariable("analyseRequest", System.Text.Json.JsonSerializer.Serialize(new SampleAnalysisRequest(
                RunId: "analyse-run",
                SampleDocId: "sample-analyse",
                AnalysisReplyCorrelationId: "a7-analyse-reply")))
            .RunAsync();

        run.EnsureRanToCompletion();

        Assert.True(run.EnvironmentContext.Contains(DockerAzureEnvironment.FunctionAppComponentId));
        Assert.True(run.EnvironmentContext.Contains(DockerAzureEnvironment.AzuriteComponentId));
        Assert.True(run.EnvironmentContext.Contains(DockerAzureEnvironment.CosmosDbComponentId));
        Assert.True(run.EnvironmentContext.Contains(DockerAzureEnvironment.ServiceBusComponentId));

        Assert.Equal(2, environment.UsedFunctionAppIdentifiers.Count);
        Assert.Contains("Ingest", environment.UsedFunctionAppIdentifiers);
        Assert.Contains("Analyse", environment.UsedFunctionAppIdentifiers);

        Assert.Equal(["SharedStorage"], environment.UsedStorageIdentifiers.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(["SharedCosmos"], environment.UsedCosmosIdentifiers.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(["SharedBus"], environment.UsedServiceBusIdentifiers.OrderBy(x => x, StringComparer.Ordinal));

        AnalysisResult ingestResult = run.ArtifactStore.GetTableEntityArtifact<AnalysisResult>("ingestResult").Last.Entity;
        AnalysisResult analyseResult = run.ArtifactStore.GetTableEntityArtifact<AnalysisResult>("analyseResult").Last.Entity;
        Assert.Equal("analysed", ingestResult.Status);
        Assert.Equal("sample-ingest", ingestResult.SampleDocId);
        Assert.Equal("analysed", analyseResult.Status);
        Assert.Equal("sample-analyse", analyseResult.SampleDocId);
    }

    [Fact]
    public async Task Contracts_select_the_intended_provider_when_multiple_candidates_exist()
    {
        IServiceProvider serviceProvider = ConfigInstance.Create().LoadDockerAzureConfig().BuildServiceProvider();
        using IDisposable _ = (IDisposable)serviceProvider;
        DockerAzureEnvironment environment = new DockerAzureEnvironment()
            .Include<ReplyBusDefinition>()
            .Include<AuditBusDefinition>()
            .Include<ContractConsumerFunctionAppDefinition>();

        TimelineRun run = await ContractSelectionTimeline
            .SetupRun(serviceProvider, outputHelper)
            .SetEnv(environment)
            .AddCosmosItemArtifact("contractDoc", "SharedCosmos", new CandidateProfile
            {
                Id = "sample-contract",
                PartitionKey = "samples",
                RunId = "contract-run",
                Stage = "ingested",
                Status = "registered",
            })
            .AddVariable("contractRequest", System.Text.Json.JsonSerializer.Serialize(new SampleAnalysisRequest(
                RunId: "contract-run",
                SampleDocId: "sample-contract",
                AnalysisReplyCorrelationId: "a7-contract-reply")))
            .RunAsync();

        run.EnsureRanToCompletion();

        Assert.True(run.EnvironmentContext.Contains(DockerAzureEnvironment.FunctionAppComponentId));
        Assert.True(run.EnvironmentContext.Contains(DockerAzureEnvironment.ServiceBusComponentId));

        Assert.Equal(["ContractConsumer"], environment.UsedFunctionAppIdentifiers.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(["ReplyBus", "SharedBus"], environment.UsedServiceBusIdentifiers.OrderBy(x => x, StringComparer.Ordinal));
        Assert.DoesNotContain("AuditBus", environment.UsedServiceBusIdentifiers);

        AnalysisResult contractResult = run.ArtifactStore.GetTableEntityArtifact<AnalysisResult>("contractResult").Last.Entity;
        Assert.Equal("analysed", contractResult.Status);
        Assert.Equal("sample-contract", contractResult.SampleDocId);
    }

    [Fact]
    public async Task Exclusive_dependencies_reject_shared_realizations()
    {
        IServiceProvider serviceProvider = ConfigInstance.Create().LoadDockerAzureConfig().BuildServiceProvider();
        using IDisposable _ = (IDisposable)serviceProvider;
        DockerAzureEnvironment environment = new DockerAzureEnvironment()
            .Include<ExclusiveBusDefinition>()
            .Include<ExclusiveFunctionAppDefinitionA>()
            .Include<ExclusiveFunctionAppDefinitionB>();

        TimelineRun run = await ExclusiveDependenciesTimeline
            .SetupRun(serviceProvider, outputHelper)
            .SetEnv(environment)
            .RunAsync();

        TimelineRunFailedException exception = Assert.Throws<TimelineRunFailedException>(() => run.EnsureRanToCompletion());

        Assert.Contains("exclusive", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("servicebus:ExclusiveBus", exception.Message, StringComparison.Ordinal);
        // The important part is where the rejection shows up: inside the normal
        // timeline path, exactly where a real suite would hit the wall and pretend to be surprised.
    }

    private sealed class SharedStorageDefinition : DockerStorageDefinition
    {
        public override StorageAccountIdentifier Identifier => "SharedStorage";

        protected override string? BlobContainerName => "showroom-blob";
        protected override string? QueueContainerName => "showroom-queue";
        protected override string? TableContainerName => "MainTable";
    }

    private sealed class SharedCosmosDefinition : DockerCosmosDefinition<CandidateProfile>
    {
        public override CosmosContainerIdentifier Identifier => "SharedCosmos";

        protected override string? DatabaseName => "BaseDB";
        protected override string? ContainerName => "BaseContainer";
    }

    private sealed class SharedBusDefinition : DockerServiceBusDefinition
    {
        public override ServiceBusIdentifier Identifier => "SharedBus";

        public DockerServiceBusEndpoint Submission
            => DockerServiceBusEndpoint.TopicSubscription("sbt-int-in", "Default");

        public DockerServiceBusEndpoint Reply
            => DockerServiceBusEndpoint.TopicSubscription("sbt-int-out", "Default");

        protected override void ConfigureServiceBusTopology(DockerServiceBusTopologyBuilder builder)
            => ConfigureShowroomServiceBusTopology(builder);
    }

    private sealed class IntakeFunctionAppDefinition : DockerFunctionAppDefinition<SampleIngestionFunction>
    {
        public override FunctionAppIdentifier Identifier => "Ingest";

        protected override void Configure(DockerFunctionAppBuilder builder)
        {
            builder
                .UseStorage<SharedStorageDefinition>(tableNameSettingName: "StorageTableName")
                .UseCosmos<SharedCosmosDefinition>()
                .UseServiceBusTrigger<SharedBusDefinition>(d => d.Submission)
                .UseServiceBusReply<SharedBusDefinition>(d => d.Reply)
                .WithAppSetting("FunctionRole", "Ingestion");
        }
    }

    private sealed class AnalysisFunctionAppDefinition : DockerFunctionAppDefinition<AnalysisProcessor>
    {
        public override FunctionAppIdentifier Identifier => "Analyse";

        protected override void Configure(DockerFunctionAppBuilder builder)
        {
            builder
                .UseStorage<SharedStorageDefinition>(tableNameSettingName: "StorageTableName")
                .UseCosmos<SharedCosmosDefinition>()
                .UseServiceBusTrigger<SharedBusDefinition>(d => d.Submission)
                .UseServiceBusReply<SharedBusDefinition>(d => d.Reply)
                .WithAppSetting("FunctionRole", "Analysis");
        }
    }

    private sealed class ReplyBusDefinition : DockerServiceBusDefinition
    {
        public override ServiceBusIdentifier Identifier => "ReplyBus";

        public DockerServiceBusEndpoint Reply
            => DockerServiceBusEndpoint.TopicSubscription("sbt-int-out", "Default");

        protected override ServiceBusConfig? CreateDefaultConfig()
            => BuildConfig(DockerAzureDefaults.PlaceholderConnectionString, Reply);

        protected override void ConfigureServiceBusTopology(DockerServiceBusTopologyBuilder builder)
            => ConfigureShowroomServiceBusTopology(builder);

        protected override void ConfigureContracts(DockerAzureContractBuilder contracts)
        {
            contracts.Provide(new ServiceBusEndpointContract(
                ContractKey: "reply",
                ServiceBusIdentifier: Identifier,
                EndpointKind: ServiceBusEndpointKind.Topic,
                EntityName: "sbt-int-out"));
        }
    }

    private sealed class AuditBusDefinition : DockerServiceBusDefinition
    {
        public override ServiceBusIdentifier Identifier => "AuditBus";

        public DockerServiceBusEndpoint Queue => DockerServiceBusEndpoint.Queue("audit-trail");

        protected override void ConfigureContracts(DockerAzureContractBuilder contracts)
        {
            contracts.Provide(new ServiceBusEndpointContract(
                ContractKey: "audit",
                ServiceBusIdentifier: Identifier,
                EndpointKind: ServiceBusEndpointKind.Queue,
                EntityName: "audit-trail"));
        }
    }

    private sealed class ContractConsumerFunctionAppDefinition : DockerFunctionAppDefinition<HttpTests>
    {
        public override FunctionAppIdentifier Identifier => "ContractConsumer";

        protected override void Configure(DockerFunctionAppBuilder builder)
        {
            builder
                .UseStorage<SharedStorageDefinition>(tableNameSettingName: "StorageTableName")
                .UseCosmos<SharedCosmosDefinition>()
                .UseServiceBusTrigger<SharedBusDefinition>(d => d.Submission)
                .UseServiceBusReply<ReplyBusDefinition>(d => d.Reply);
        }

        protected override void ConfigureContracts(DockerAzureContractBuilder contracts)
        {
            contracts.Require(new ServiceBusEndpointContract(
                ContractKey: "reply",
                ServiceBusIdentifier: "ReplyBus",
                EndpointKind: ServiceBusEndpointKind.Topic,
                EntityName: "sbt-int-out"));
        }
    }

    private sealed class ExclusiveBusDefinition : DockerServiceBusDefinition
    {
        public override ServiceBusIdentifier Identifier => "ExclusiveBus";

        public DockerServiceBusEndpoint Queue => DockerServiceBusEndpoint.Queue("exclusive-queue");
    }

    private sealed class ExclusiveFunctionAppDefinitionA : DockerFunctionAppDefinition<AnalysisProcessor>
    {
        public override FunctionAppIdentifier Identifier => "ExclusiveA";

        protected override void ConfigureDependencies(DockerAzureDependencyBuilder dependencies)
        {
            dependencies.Include<ExclusiveBusDefinition>(DependencyOwnership.Exclusive);
        }
    }

    private sealed class ExclusiveFunctionAppDefinitionB : DockerFunctionAppDefinition<HttpTests>
    {
        public override FunctionAppIdentifier Identifier => "ExclusiveB";

        protected override void ConfigureDependencies(DockerAzureDependencyBuilder dependencies)
        {
            dependencies.Include<ExclusiveBusDefinition>(DependencyOwnership.Exclusive);
        }
    }

    private static void ConfigureShowroomServiceBusTopology(DockerServiceBusTopologyBuilder builder)
    {
        builder.AddNamespace("sbemulatorns", ns => ns
            .AddQueue("audit-trail")
            .AddQueue("exclusive-queue")
            .AddTopic("sbt-int-in", topic => topic.AddSubscription("Default"))
            .AddTopic("sbt-int-out", topic => topic.AddSubscription("Default")));
    }
}