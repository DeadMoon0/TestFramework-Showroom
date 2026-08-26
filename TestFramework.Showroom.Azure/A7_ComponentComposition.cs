using Microsoft.Extensions.DependencyInjection;
using FunctionApp;
using TestFramework.Azure;
using TestFramework.Azure.Configuration.SpecificConfigs;
using TestFramework.Azure.Extensions;
using TestFramework.Azure.Identifier;
using TestFramework.Container.Azure;
using TestFramework.Container.Sources;
using TestFramework.Config;
using TestFramework.Core.Exceptions;
using TestFramework.Core.Environment;
using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using Xunit.Abstractions;
using TestFramework.Container.Azure.Contracts;
namespace TestFramework.Showroom.Azure;

//doc: Several apps. One dependency graph. No delusions.
//doc:
//doc: A6 proved the container-backed path can finish an end-to-end story. Fine. The lights turned on,
//doc: everybody applauded. Now we can discuss what happens when the environment stops being polite and
//doc: starts being realistic and vaguely territorial.
//doc:
//doc: Real systems do not arrive one Function App at a time. They arrive in packs, each with demands,
//doc: assumptions and the occasional territorial dispute. This chapter walks through the three outcomes that
//doc: matter:
//doc:
//doc: 1. **Shared dependencies are reused** when the graph allows it - two apps naming one storage account
//doc:    get one storage account, not two.
//doc: 2. **Contracts select the intended provider** when several could satisfy a need, so the choice is
//doc:    stated rather than inferred from declaration order.
//doc: 3. **Exclusive claims fail** rather than being quietly shared, and they fail inside the normal timeline
//doc:    path where a suite would actually hit them.
//doc:
//doc: That third one is the theme. Fast failure is mercy; late failure is paperwork and committee language.
//doc:
//doc: This chapter also breaks the lane's habit of using `AzureShowroom.CreateEnvironment()`: each test builds
//doc: its own environment, because the environment *is* what is under test here. The definitions all live at
//doc: the bottom of the file.

//doc: Three timelines, one per outcome. They are ordinary - two HTTP calls into two function apps, a
//doc: capture, a liveness probe - and that is the point: nothing in a timeline expresses composition. The
//doc: composition is entirely in the environment each test hands to `SetEnv`.

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

    //doc: Outcome one. Two function apps, `Ingest` and `Analyse`, each declaring the same storage, Cosmos and
    //doc: Service Bus definitions. The environment is asked for both and the assertions then read what it
    //doc: actually used: two function app identifiers, but exactly **one** storage, **one** Cosmos and
    //doc: **one** bus.
    //doc:
    //doc: That is the whole claim, and `environment.UsedStorageIdentifiers` is how the framework lets you
    //doc: make it. Sharing by name is not a coincidence you hope for; it is an outcome you can assert.
    //doc:
    //doc: The two apps are otherwise identical except for a `WithAppSetting("FunctionRole", …)` - which is the
    //doc: honest way to say "same image, different job".

    [DockerFact]
    [Trait("Category", "DockerSmoke")]
    public async Task Shared_dependencies_are_reused_across_multiple_function_apps()
    {
        // Held as the concrete ServiceProvider, which is what BuildServiceProvider returns: the
        // provider owns every singleton it built, and the concrete type is what makes that
        // ownership visible here instead of hiding it behind IServiceProvider.
        //
        // Disposed asynchronously, and that is not a style choice. It holds an AzureClientCache,
        // which implements IAsyncDisposable and not IDisposable, and the container refuses a
        // synchronous Dispose of such a service rather than blocking on it.
        await using ServiceProvider serviceProvider = ConfigInstance.Create().LoadDockerAzureConfig().BuildServiceProvider();
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

    //doc: Outcome two, and the subtler one. Two buses are included, `ReplyBus` and `AuditBus`, and both
    //doc: *provide* a contract. The consuming app **requires** the one keyed `reply`, so that is the one it is
    //doc: wired to.
    //doc:
    //doc: The assertions prove both halves: `ReplyBus` was used, and `AuditBus` was not - it was declared,
    //doc: nobody required it, so nothing started it. Declaration is not consumption.
    //doc:
    //doc: Why bother with a contract instead of naming the definition directly? Because naming works until
    //doc: two candidates could plausibly serve, at which point selection by declaration order is a decision
    //doc: nobody made on purpose. A contract is the requirement written down: kind, entity, key. It is checked,
    //doc: and when nothing satisfies it you find out at composition time rather than from a message that never
    //doc: arrives.

    [DockerFact]
    [Trait("Category", "DockerSmoke")]
    public async Task Contracts_select_the_intended_provider_when_multiple_candidates_exist()
    {
        // Held as the concrete ServiceProvider, which is what BuildServiceProvider returns: the
        // provider owns every singleton it built, and the concrete type is what makes that
        // ownership visible here instead of hiding it behind IServiceProvider.
        //
        // Disposed asynchronously, and that is not a style choice. It holds an AzureClientCache,
        // which implements IAsyncDisposable and not IDisposable, and the container refuses a
        // synchronous Dispose of such a service rather than blocking on it.
        await using ServiceProvider serviceProvider = ConfigInstance.Create().LoadDockerAzureConfig().BuildServiceProvider();
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

    //doc: Outcome three: the refusal. Two function apps each claim the same bus with
    //doc: `DependencyOwnership.Exclusive`, which is a claim the environment cannot honour twice. So it does
    //doc: not try.
    //doc:
    //doc: Read where the rejection arrives, because that is the design decision. It is not an assertion helper
    //doc: and not a special validation mode - it comes out of `EnsureRanToCompletion()` as an ordinary
    //doc: `TimelineRunFailedException`, naming the resource as `servicebus:ExclusiveBus`. Exactly where a real
    //doc: suite would hit the wall and pretend to be surprised.
    //doc:
    //doc: `Exclusive` is worth reaching for when sharing would be silently wrong rather than merely wasteful:
    //doc: a queue where two consumers would steal each other's messages, a database an app resets on startup.
    //doc: The default is shared, because sharing is usually what you want and always what is cheaper.

    [DockerFact]
    [Trait("Category", "DockerSmoke")]
    public async Task Exclusive_dependencies_reject_shared_realizations()
    {
        // Held as the concrete ServiceProvider, which is what BuildServiceProvider returns: the
        // provider owns every singleton it built, and the concrete type is what makes that
        // ownership visible here instead of hiding it behind IServiceProvider.
        //
        // Disposed asynchronously, and that is not a style choice. It holds an AzureClientCache,
        // which implements IAsyncDisposable and not IDisposable, and the container refuses a
        // synchronous Dispose of such a service rather than blocking on it.
        await using ServiceProvider serviceProvider = ConfigInstance.Create().LoadDockerAzureConfig().BuildServiceProvider();
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

    //doc: The definitions. This is the part to read twice, because everything above is a consequence of what
    //doc: is written here - and it is all declarative. No definition knows about the tests, the environments,
    //doc: or each other's runtime state.
    //doc:
    //doc: - The three shared definitions (`SharedStorage`, `SharedCosmos`, `SharedBus`) are named once and
    //doc:   referenced by both apps through `UseStorage<…>`, `UseCosmos<…>`, `UseServiceBusTrigger<…>`. That
    //doc:   reference by *type* is what makes sharing by name possible.
    //doc: - `ReplyBusDefinition` and `AuditBusDefinition` each `Provide` a contract; `ContractConsumer…`
    //doc:   `Require`s one. Provide and require are the two halves, and they meet on the contract key.
    //doc: - `ExclusiveFunctionAppDefinitionA` and `…B` use `ConfigureDependencies` rather than `Configure`,
    //doc:   because ownership is a statement about the dependency graph rather than about app settings.
    //doc: - All five apps name the same payload through `Source`, and that is the point of the chapter in one
    //doc:   line: identical code, five different places in the dependency graph. What separates them is what
    //doc:   they declare about their neighbours, never what they are built from.
    //doc: - And the topology helper at the bottom declares the queues and topics the emulator must actually
    //doc:   have. A bus identifier is a name; the entities behind it still have to exist.

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

    // Every app below ships this same payload, published on the host because a Function App payload is
    // mounted into the Functions host image rather than run as an image of its own.
    private const string FunctionAppProject = "../Azure/FunctionApp/FunctionApp.csproj";

    private sealed class IntakeFunctionAppDefinition : DockerFunctionAppDefinition
    {
        public override FunctionAppIdentifier Identifier => "Ingest";

        public override ContainerSource Source => ContainerSource.Project(FunctionAppProject).BuiltOnHost();

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

    private sealed class AnalysisFunctionAppDefinition : DockerFunctionAppDefinition
    {
        public override FunctionAppIdentifier Identifier => "Analyse";

        public override ContainerSource Source => ContainerSource.Project(FunctionAppProject).BuiltOnHost();

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

    private sealed class ContractConsumerFunctionAppDefinition : DockerFunctionAppDefinition
    {
        public override FunctionAppIdentifier Identifier => "ContractConsumer";

        public override ContainerSource Source => ContainerSource.Project(FunctionAppProject).BuiltOnHost();

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

    private sealed class ExclusiveFunctionAppDefinitionA : DockerFunctionAppDefinition
    {
        public override FunctionAppIdentifier Identifier => "ExclusiveA";

        public override ContainerSource Source => ContainerSource.Project(FunctionAppProject).BuiltOnHost();

        protected override void ConfigureDependencies(DockerAzureDependencyBuilder dependencies)
        {
            dependencies.Include<ExclusiveBusDefinition>(DependencyOwnership.Exclusive);
        }
    }

    private sealed class ExclusiveFunctionAppDefinitionB : DockerFunctionAppDefinition
    {
        public override FunctionAppIdentifier Identifier => "ExclusiveB";

        public override ContainerSource Source => ContainerSource.Project(FunctionAppProject).BuiltOnHost();

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