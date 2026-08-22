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
using Xunit.Abstractions;

namespace TestFramework.Showroom.Azure;

//doc: One stack. Many test runs. Less waiting.
//doc:
//doc: Every earlier cloud chapter rebuilds its slice of the Azure stack from scratch, because that is how you
//doc: teach the machinery without also teaching impatience. It is clean, honest, and wildly expensive once
//doc: the suite grows teeth and a calendar. Then reality walks in holding a stopwatch.
//doc:
//doc: This is chapter 12's persistence primitive with containers under it, and packaged for xunit. The
//doc: arrangement:
//doc:
//doc: 1. Describe the full environment once, through a state type.
//doc: 2. Mark the expensive slice that deserves to live longer than one run.
//doc: 3. Keep producing fresh runs on top of that reused machinery.
//doc:
//doc: The important detail is in the second test: persistent is not the same as frozen. The stack stays on,
//doc: and a run can still walk in and rearrange the desk - which is how adults express freedom.
//doc:
//doc: Each test runs the same timeline twice into one output helper, so each panel below holds two reports.
//doc: They are useful for seeing that both runs are whole runs rather than one run and an echo - but the
//doc: claim itself is in the assertions. `Assert.Same` on runtime state across two runs is a fact no log line
//doc: states, because the report describes what a run did and not which machinery it was handed.

//doc: `[Collection]` is what ties the tests to the shared fixture, and the collection is declared
//doc: non-parallel further down - a shared hosted stack and concurrent tests are a combination that teaches
//doc: nothing except patience.

[Collection(PersistentHostedCollectionDefinition.CollectionName)]
public class PersistentHostedFixture_ReusesPersistentComponentsAcrossRuns(
    PersistentHostedFixture fixture,
    ITestOutputHelper output)
{
    // One tiny timeline is enough here. We are not testing business flow.
    // We are interrogating the contract: does the same hosted slice survive
    // multiple runs, and can each run still negotiate its own config without filing forms in triplicate?
    private static readonly Timeline InspectStorageTimeline = Timeline.Create()
        .Trigger(new InspectStorageConfigStep())
            .Name("inspect-storage-config")
        .Build();

    //doc: Test one: two runs, same fixture, no excuses. `fixture.GetEnv()` hands out an environment per run,
    //doc: and the assertions then prove the *network* and the *Azurite* component states are literally the
    //doc: same objects across both. Not equivalent. The same.
    //doc:
    //doc: That is the saving, stated as an assertion rather than as a stopwatch reading: two runs, one
    //doc: container start. And the step's result confirms the config each run saw was the persistent one -
    //doc: `PersistentTable` both times.

    [DockerFact]
    [Trait("Category", "DockerSmoke")]
    public async Task Persistent_fixture_reuses_the_same_storage_runtime_slice()
    {
        // Two runs. Same fixture. No excuses. If the persistent slice works,
        // both runs should point at the same underlying hosted runtime state instead of reenacting Groundhog Day with containers.
        TimelineRun firstRun = await InspectStorageTimeline.SetupRun(output).SetEnv(fixture.GetEnv()).RunAsync();
        TimelineRun secondRun = await InspectStorageTimeline.SetupRun(output).SetEnv(fixture.GetEnv()).RunAsync();

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

    //doc: Test two is the one that makes the feature usable. A baseline run, then a run that overrides only
    //doc: its own view of the config: `GetEnv(builder => …)` layers a run-local `ConfigStore` on top.
    //doc:
    //doc: The two assertions afterwards are the whole point, side by side. The baseline sees
    //doc: `PersistentTable`, the override run sees `RunLocalTable` - *and* both still share the same network
    //doc: and Azurite state. The config surface bent; nothing restarted.
    //doc:
    //doc: That is the difference between "persistent" and "frozen", and it is what lets a shared stack serve
    //doc: tests that need slightly different configuration without giving up the reuse that made it worth
    //doc: sharing.

    [DockerFact]
    [Trait("Category", "DockerSmoke")]
    public async Task Persistent_fixture_allows_run_local_config_overrides_without_restarting_the_stack()
    {
        // Baseline first, then a run that changes only its local view of the
        // config. The storage stack should stay alive while the logical config
        // surface bends to the needs of that specific run and nobody has to reboot their hopes.
        TimelineRun baselineRun = await InspectStorageTimeline.SetupRun(output).SetEnv(fixture.GetEnv()).RunAsync();
        TimelineRun overrideRun = await InspectStorageTimeline
            .SetupRun(output)
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

    //doc: The step is a custom step in chapter 11's shape, with one addition: it declares an environment
    //doc: requirement, so the storage component is created because something asked for it. Its whole job is to
    //doc: report which table name the *current run* sees, which keeps the example honest - if config layering
    //doc: is wrong, this step says so immediately and without the courtesy of soft music.

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

//doc: The rest of the file is the fixture plumbing, and it is worth reading because two of these four types
//doc: exist for reasons that are not obvious.
//doc:
//doc: The definition is intentionally boring. That is the point: stable infrastructure should look inevitable,
//doc: not dramatic. Drama is for logs.

internal sealed class PersistentStorageDefinition : DockerStorageDefinition
{
    // The definition is intentionally boring. That is the point. Stable
    // infrastructure should look inevitable, not dramatic. Drama is for logs.
    public override StorageAccountIdentifier Identifier => "PersistentStorage";

    protected override string? BlobContainerName => "persistent-blob";
    protected override string? TableContainerName => "PersistentTable";
}

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class PersistentHostedCollectionDefinition : ICollectionFixture<PersistentHostedFixture>
{
    // xUnit needs a stable identity for the shared hosted fixture. Give it one
    // and it stops trying to be creative, which is best for everyone involved.
    public const string CollectionName = "AzureShowroom.PersistentHosted";
}

//doc: And the adapter, which carries two jobs that are both load-bearing and neither decorative.
//doc:
//doc: First, `DockerAzureHostedCollectionFixture` deliberately does not implement `IAsyncLifetime` - xunit is
//doc: not a runtime dependency of the container package - so without this declaration nothing would ever call
//doc: `InitializeAsync` and `GetEnv()` would refuse to hand out an environment. Declaring the interface here
//doc: is the adapter the package asks consumers for, and it is four lines.
//doc:
//doc: Second, the Docker gate, and this one is a trap worth knowing about. A collection fixture is constructed
//doc: *before* any test in the collection runs, so a `[DockerFact]` skip cannot save a fixture that has
//doc: already tried to boot a container stack. Checking the gate here means a machine without Docker gets two
//doc: explained skips instead of one boot failure.

/// <summary>
/// The xUnit v2 adapter for the hosted fixture, plus the Docker gate.
/// </summary>
/// <remarks>
/// <para>
/// Two jobs, both load-bearing. First, <c>DockerAzureHostedCollectionFixture</c> deliberately does
/// not implement <c>IAsyncLifetime</c> — xunit is not a runtime dependency of the container package —
/// so nothing would ever call <c>InitializeAsync</c> and <c>GetEnv()</c> would refuse to hand out an
/// environment. Declaring the interface here is the adapter the package README asks consumers for.
/// </para>
/// <para>
/// Second, the gate. A collection fixture is constructed before any test in the collection runs, so
/// a <c>[DockerFact]</c> skip cannot save a fixture that has already tried to boot a container stack.
/// Checking here means a machine without Docker gets two explained skips instead of one boot failure.
/// </para>
/// </remarks>
public sealed class PersistentHostedFixture : DockerAzureHostedCollectionFixture<PersistentHostedFixtureState>, IAsyncLifetime
{
    private bool _booted;

    Task IAsyncLifetime.InitializeAsync()
    {
        if (!ShowroomEnvironmentGate.TryEnableDockerHost(out _))
            return Task.CompletedTask;

        _booted = true;
        return base.InitializeAsync();
    }

    Task IAsyncLifetime.DisposeAsync()
        => _booted ? base.DisposeAsync() : Task.CompletedTask;
}

//doc: Last, the state type, which is where the three decisions from the top of the chapter are actually
//doc: written down: `PersistentRequirements` names the slice that survives, `CreateEnvironment()` describes
//doc: the whole shape, and `CreatePersistentConfig()` is the default run view that later runs clone and layer
//doc: over - which is what test two overrides without paying for a restart.
//doc:
//doc: Only storage is promoted here, deliberately. One reusable component chain teaches the mechanism; a full
//doc: family reunion teaches container startup times.

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
