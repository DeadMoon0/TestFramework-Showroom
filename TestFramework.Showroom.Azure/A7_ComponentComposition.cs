using FunctionApp;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Azure;
using TestFramework.Azure.Configuration;
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
//  CONTAINER ORCHESTRATION DIVISION — MODULE A7
//  "Multiple Function Apps, Shared Resources, And Other Scheduling Choices"
//
//  Module A6 proved the container-backed Function App path can run end-to-end.
//  Excellent. We made the machinery move. Very inspirational.
//  Module A7 answers the next question: what happens when you stop pretending
//  there is only one Function App and one noble, uncomplicated dependency tree?
//
//  This module demonstrates three things:
//    1. Shared dependencies: two Function Apps can reuse one realized resource.
//    2. Contract-based reuse: the right provider is selected on purpose.
//    3. Exclusive dependencies: if two apps demand private ownership of the same
//       realized dependency, resolution fails early and loudly.
//
//  Early and loudly is a feature. Quiet failure is how buildings become folklore.
// ══════════════════════════════════════════════════════════════════════════════

[Collection("AzureShowroom")]
public class ComponentComposition_SharedDependenciesAndContracts(ITestOutputHelper outputHelper)
{
    private static readonly Timeline SharedDependenciesTimeline = Timeline.Create()
        .SetupArtifact("ingestDoc")
        .SetupArtifact("analyseDoc")
        .RegisterArtifact(
            "ingestResult",
            AzureTF.Artifact.StorageAccount.TableRef<AnalysisResult>(
                "SharedStorage",
                Var.Const("MainTable"),
                Var.Const("samples"),
                Var.Const("ingest-run")))
        .RegisterArtifact(
            "analyseResult",
            AzureTF.Artifact.StorageAccount.TableRef<AnalysisResult>(
                "SharedStorage",
                Var.Const("MainTable"),
                Var.Const("samples"),
                Var.Const("analyse-run")))
        .Trigger(
            AzureTF.Trigger.FunctionApp
                .Http("Ingest")
                .SelectEndpointWithMethod<AnalysisProcessor>(nameof(AnalysisProcessor.Run))
                .WithBody(Var.Ref<string>("ingestRequest"))
                .Call())
            .WithTimeOut(TimeSpan.FromMinutes(2))
        .Trigger(
            AzureTF.Trigger.FunctionApp
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
            AzureTF.Artifact.StorageAccount.TableRef<AnalysisResult>(
                "SharedStorage",
                Var.Const("MainTable"),
                Var.Const("samples"),
                Var.Const("contract-run")))
        .Trigger(
            AzureTF.Trigger.FunctionApp
                .Http("ContractConsumer")
                .SelectEndpointWithMethod<AnalysisProcessor>(nameof(AnalysisProcessor.Run))
                .WithBody(Var.Ref<string>("contractRequest"))
                .Call())
            .WithTimeOut(TimeSpan.FromMinutes(2))
        .CaptureArtifactVersion("contractResult")
        .Build();

    private static readonly Timeline ExclusiveDependenciesTimeline = Timeline.Create()
        .Trigger(AzureTF.Trigger.IsLive.FunctionApp("ExclusiveA"))
            .WithTimeOut(TimeSpan.FromMinutes(2))
        .Trigger(AzureTF.Trigger.IsLive.FunctionApp("ExclusiveB"))
            .WithTimeOut(TimeSpan.FromMinutes(2))
        .Build();

    [Fact]
    public async Task Shared_dependencies_are_reused_across_multiple_function_apps()
    {
        IServiceProvider serviceProvider = BuildConfig().BuildServiceProvider();
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
        Assert.Equal(["SharedReply", "SharedSubmission"], environment.UsedServiceBusIdentifiers.OrderBy(x => x, StringComparer.Ordinal));

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
        IServiceProvider serviceProvider = BuildConfig().BuildServiceProvider();
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
        Assert.Equal(["ReplyBus", "SharedSubmission"], environment.UsedServiceBusIdentifiers.OrderBy(x => x, StringComparer.Ordinal));
        Assert.DoesNotContain("AuditBus", environment.UsedServiceBusIdentifiers);

        AnalysisResult contractResult = run.ArtifactStore.GetTableEntityArtifact<AnalysisResult>("contractResult").Last.Entity;
        Assert.Equal("analysed", contractResult.Status);
        Assert.Equal("sample-contract", contractResult.SampleDocId);
    }

    [Fact]
    public async Task Exclusive_dependencies_reject_shared_realizations()
    {
        IServiceProvider serviceProvider = BuildConfig().BuildServiceProvider();
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
        // The failure still happens inside the normal Timeline execution path.
        // Users see the same startup rejection they would hit in a real run.
    }

    private static ConfigInstance BuildConfig()
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
            })
            .Build();
    }

    private static void RegisterAzureStores(IServiceCollection services)
    {
        ConfigStore<StorageAccountConfig> storageStore = new();
        storageStore.AddConfig("SharedStorage", new StorageAccountConfig
        {
            ConnectionString = "UseDevelopmentStorage=true",
            BlobContainerName = "showroom-blob",
            QueueContainerName = "showroom-queue",
            TableContainerName = "MainTable",
        });
        services.AddSingleton(storageStore);

        ConfigStore<CosmosContainerDbConfig> cosmosStore = new();
        cosmosStore.AddConfig("SharedCosmos", new CosmosContainerDbConfig
        {
            ConnectionString = "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;",
            DatabaseName = "BaseDB",
            ContainerName = "BaseContainer",
        });
        services.AddSingleton(cosmosStore);

        ConfigStore<FunctionAppConfig> functionAppStore = new();
        functionAppStore.AddConfig("Ingest", CreateFunctionAppConfig());
        functionAppStore.AddConfig("Analyse", CreateFunctionAppConfig());
        functionAppStore.AddConfig("ContractConsumer", CreateFunctionAppConfig());
        functionAppStore.AddConfig("ExclusiveA", CreateFunctionAppConfig());
        functionAppStore.AddConfig("ExclusiveB", CreateFunctionAppConfig());
        services.AddSingleton(functionAppStore);

        ConfigStore<ServiceBusConfig> serviceBusStore = new();
        serviceBusStore.AddConfig("SharedSubmission", new ServiceBusConfig
        {
            ConnectionString = "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=local",
            QueueName = null,
            TopicName = "sbt-int-in",
            SubscriptionName = "Default",
            RequiredSession = false,
        });
        serviceBusStore.AddConfig("SharedReply", new ServiceBusConfig
        {
            ConnectionString = "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=local",
            QueueName = null,
            TopicName = "sbt-int-out",
            SubscriptionName = "Default",
            RequiredSession = false,
        });
        serviceBusStore.AddConfig("ReplyBus", new ServiceBusConfig
        {
            ConnectionString = "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=local",
            QueueName = null,
            TopicName = "sbt-int-out",
            SubscriptionName = "Default",
            RequiredSession = false,
        });
        serviceBusStore.AddConfig("AuditBus", new ServiceBusConfig
        {
            ConnectionString = "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=local",
            QueueName = "audit-trail",
            TopicName = null,
            SubscriptionName = null,
            RequiredSession = false,
        });
        serviceBusStore.AddConfig("ExclusiveBus", new ServiceBusConfig
        {
            ConnectionString = "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=local",
            QueueName = "exclusive-queue",
            TopicName = null,
            SubscriptionName = null,
            RequiredSession = false,
        });
        services.AddSingleton(serviceBusStore);
    }

    private static FunctionAppConfig CreateFunctionAppConfig() => new()
    {
        BaseUrl = "http://localhost/",
        Code = "unused",
        AdminCode = "unused",
    };

    private sealed class SharedStorageDefinition : DockerStorageDefinition
    {
        public override StorageAccountIdentifier Identifier => "SharedStorage";
    }

    private sealed class SharedCosmosDefinition : DockerCosmosDefinition<CandidateProfile>
    {
        public override CosmosContainerIdentifier Identifier => "SharedCosmos";
    }

    private sealed class SharedReplyBusDefinition : DockerServiceBusDefinition
    {
        public override ServiceBusIdentifier Identifier => "SharedReply";
        public override string TopologyConfigPath => Path.Combine("ShowroomAzure", "ServiceBus", "config.json");
    }

    private sealed class SharedSubmissionBusDefinition : DockerServiceBusDefinition
    {
        public override ServiceBusIdentifier Identifier => "SharedSubmission";
        public override string TopologyConfigPath => Path.Combine("ShowroomAzure", "ServiceBus", "config.json");
    }

    private sealed class IntakeFunctionAppDefinition : DockerFunctionAppDefinition<SampleIngestionFunction>
    {
        public override FunctionAppIdentifier Identifier => "Ingest";

        protected override void Configure(DockerFunctionAppBuilder builder)
        {
            builder
                .UseStorage<SharedStorageDefinition>(tableNameSettingName: "StorageTableName")
                .UseCosmos<SharedCosmosDefinition>()
                .UseServiceBusTrigger<SharedSubmissionBusDefinition>()
                .UseServiceBusReply<SharedReplyBusDefinition>()
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
                .UseServiceBusTrigger<SharedSubmissionBusDefinition>()
                .UseServiceBusReply<SharedReplyBusDefinition>()
                .WithAppSetting("FunctionRole", "Analysis");
        }
    }

    private sealed class ReplyBusDefinition : DockerServiceBusDefinition
    {
        public override ServiceBusIdentifier Identifier => "ReplyBus";
        public override string TopologyConfigPath => Path.Combine("ShowroomAzure", "ServiceBus", "config.json");

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
                .UseServiceBusTrigger<SharedSubmissionBusDefinition>()
                .UseServiceBusReply<ReplyBusDefinition>();
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
}