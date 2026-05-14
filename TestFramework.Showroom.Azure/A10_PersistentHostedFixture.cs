using Microsoft.Extensions.DependencyInjection;
using TestFramework.Azure;
using TestFramework.Azure.Configuration;
using TestFramework.Azure.Configuration.SpecificConfigs;
using TestFramework.Azure.Extensions;
using TestFramework.Azure.Identifier;
using TestFramework.Container.Azure;
using TestFramework.Config;
using TestFramework.Core.Environment;
using TestFramework.Core.Logging;
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;

namespace TestFramework.Showroom.Azure;

// ══════════════════════════════════════════════════════════════════════════════
//  HOSTED PERSISTENCE DIVISION - MODULE A10
//  "One Stack. Many Test Runs. Less Waiting."
//
//  Earlier chapters rebuild the whole Azure test stack every time because that
//  is how you teach the machinery without also teaching impatience. It is clean,
//  honest, and wildly expensive once the suite grows teeth and a calendar.
//
//  Then reality walks in holding a stopwatch.
//
//  If the storage, network, and emulator slice stays the same for run after
//  run, rebuilding it is not discipline. It is theater. This chapter shows the
//  grown-up arrangement:
//    1. Describe the full environment once through TState.
//    2. Mark the expensive slice that deserves to live longer than one run.
//    3. Keep producing fresh runs on top of that reused machinery.
//
//  Important detail: persistent is not the same as frozen. The stack stays on.
//  The run still gets to walk in and rearrange the desk, which is how adults express freedom.
// ══════════════════════════════════════════════════════════════════════════════

[Collection(PersistentHostedCollectionDefinition.CollectionName)]
public class PersistentHostedFixture_ReusesPersistentComponentsAcrossRuns(
    DockerAzureHostedCollectionFixture<PersistentHostedFixtureState> fixture)
{
    // One tiny timeline is enough here. We are not testing business flow.
    // We are interrogating the contract: does the same hosted slice survive
    // multiple runs, and can each run still negotiate its own config without filing forms in triplicate?
    private static readonly Timeline InspectStorageTimeline = Timeline.Create()
        .Trigger(new InspectStorageConfigStep()).Name("inspect-storage-config")
        .Build();

    [Fact]
    public async Task Persistent_fixture_reuses_the_same_storage_runtime_slice()
    {
        // Two runs. Same fixture. No excuses. If the persistent slice works,
        // both runs should point at the same underlying hosted runtime state instead of reenacting Groundhog Day with containers.
        TimelineRun firstRun = await InspectStorageTimeline.SetupRun().SetEnv(fixture.GetEnv()).RunAsync();
        TimelineRun secondRun = await InspectStorageTimeline.SetupRun().SetEnv(fixture.GetEnv()).RunAsync();

        firstRun.EnsureRanToCompletion();
        secondRun.EnsureRanToCompletion();

        Assert.Equal("PersistentTable", Assert.IsType<InspectStorageConfigResult>(firstRun.Step("inspect-storage-config").LastResult.Result).TableContainerName);
        Assert.Equal("PersistentTable", Assert.IsType<InspectStorageConfigResult>(secondRun.Step("inspect-storage-config").LastResult.Result).TableContainerName);
        Assert.Same(
            firstRun.EnvironmentContext.GetState<object>(DockerAzureEnvironment.NetworkComponentId),
            secondRun.EnvironmentContext.GetState<object>(DockerAzureEnvironment.NetworkComponentId));
        Assert.Same(
            firstRun.EnvironmentContext.GetState<object>(DockerAzureEnvironment.AzuriteComponentId),
            secondRun.EnvironmentContext.GetState<object>(DockerAzureEnvironment.AzuriteComponentId));
    }

    [Fact]
    public async Task Persistent_fixture_allows_run_local_config_overrides_without_restarting_the_stack()
    {
        // Baseline first, then a run that changes only its local view of the
        // config. The storage stack should stay alive while the logical config
        // surface bends to the needs of that specific run and nobody has to reboot their hopes.
        TimelineRun baselineRun = await InspectStorageTimeline.SetupRun().SetEnv(fixture.GetEnv()).RunAsync();
        TimelineRun overrideRun = await InspectStorageTimeline
            .SetupRun()
            .SetEnv(fixture.GetEnv(builder =>
            {
                builder.AddService(services =>
                {
                    services.AddSingleton(ConfigStore<StorageAccountConfig>.Create("PersistentStorage", new StorageAccountConfig
                    {
                        ConnectionString = "UseDevelopmentStorage=true",
                        BlobContainerName = "persistent-blob",
                        QueueContainerName = null,
                        TableContainerName = "RunLocalTable",
                    }));
                });
            }))
            .RunAsync();

        baselineRun.EnsureRanToCompletion();
        overrideRun.EnsureRanToCompletion();

        Assert.Equal("PersistentTable", Assert.IsType<InspectStorageConfigResult>(baselineRun.Step("inspect-storage-config").LastResult.Result).TableContainerName);
        Assert.Equal("RunLocalTable", Assert.IsType<InspectStorageConfigResult>(overrideRun.Step("inspect-storage-config").LastResult.Result).TableContainerName);
        Assert.Same(
            baselineRun.EnvironmentContext.GetState<object>(DockerAzureEnvironment.NetworkComponentId),
            overrideRun.EnvironmentContext.GetState<object>(DockerAzureEnvironment.NetworkComponentId));
        Assert.Same(
            baselineRun.EnvironmentContext.GetState<object>(DockerAzureEnvironment.AzuriteComponentId),
            overrideRun.EnvironmentContext.GetState<object>(DockerAzureEnvironment.AzuriteComponentId));
    }

    private sealed class InspectStorageConfigStep : TestFramework.Core.Steps.Step<InspectStorageConfigResult>, IHasEnvironmentRequirements
    {
        // This step does one job: ask the current run which table name it sees.
        // That keeps the example honest. If config layering is wrong, this step
        // reports the lie immediately and without the courtesy of soft music.
        public override string Name => "inspect-storage-config";

        public override string Description => "Reads the active Storage config for the current run.";

        public override bool DoesReturn => true;

        public IReadOnlyCollection<EnvironmentRequirement> GetEnvironmentRequirements(VariableStore variableStore)
            => [new(AzureEnvironmentResourceKinds.Storage, "PersistentStorage")];

        public override Task<InspectStorageConfigResult?> Execute(IServiceProvider serviceProvider, VariableStore variableStore, TestFramework.Core.Artifacts.ArtifactStore artifactStore, ScopedLogger logger, CancellationToken cancellationToken)
        {
            StorageAccountConfig config = ((ConfigStore<StorageAccountConfig>)serviceProvider.GetService(typeof(ConfigStore<StorageAccountConfig>))!).GetConfig("PersistentStorage");
            return Task.FromResult<InspectStorageConfigResult?>(new(config.TableContainerName ?? throw new InvalidOperationException("PersistentStorage table name was not configured.")));
        }

        public override TestFramework.Core.Steps.Step<InspectStorageConfigResult> Clone() => new InspectStorageConfigStep().WithClonedOptions(this);

        public override void DeclareIO(StepIOContract contract)
        {
        }

        public override TestFramework.Core.Steps.StepInstance<TestFramework.Core.Steps.Step<InspectStorageConfigResult>, InspectStorageConfigResult> GetInstance()
            => new(this);
    }

    private sealed record InspectStorageConfigResult(string TableContainerName) : StepResultContext;

}

internal sealed class PersistentStorageDefinition : DockerStorageDefinition
{
    // The definition is intentionally boring. That is the point. Stable
    // infrastructure should look inevitable, not dramatic. Drama is for logs.
    public override StorageAccountIdentifier Identifier => "PersistentStorage";

    protected override string? BlobContainerName => "persistent-blob";
    protected override string? TableContainerName => "PersistentTable";
}

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class PersistentHostedCollectionDefinition : ICollectionFixture<DockerAzureHostedCollectionFixture<PersistentHostedFixtureState>>
{
    // xUnit needs a stable identity for the shared hosted fixture. Give it one
    // and it stops trying to be creative, which is best for everyone involved.
    public const string CollectionName = "AzureShowroom.PersistentHosted";
}

public sealed class PersistentHostedFixtureState : IDockerAzureHostedFixtureState
{
    // Only the storage requirement is promoted into the persistent slice here.
    // That keeps the sample focused on one reusable component chain instead of a full family reunion.
    public IReadOnlyList<EnvironmentRequirement> PersistentRequirements =>
    [
        new(AzureEnvironmentResourceKinds.Storage, "PersistentStorage"),
    ];

    // Full environment shape. The fixture decides what stays alive longer.
    public DockerAzureEnvironment CreateEnvironment()
        => new DockerAzureEnvironment().Include<PersistentStorageDefinition>();

    // This is the default run view that the hosted fixture clones and layers.
    // Later runs may override it without paying the price of rebuilding the
    // persistent storage machinery underneath, which is excellent because time remains stubbornly finite.
    public ConfigInstance CreatePersistentConfig()
        => ConfigInstance.Create()
            .LoadDockerAzureConfig()
            .AddService(services =>
            {
                services.AddSingleton(ConfigStore<StorageAccountConfig>.Create("PersistentStorage", new StorageAccountConfig
                {
                    ConnectionString = "UseDevelopmentStorage=true",
                    BlobContainerName = "persistent-blob",
                    QueueContainerName = null,
                    TableContainerName = "PersistentTable",
                }));
            })
            .Build();
}
