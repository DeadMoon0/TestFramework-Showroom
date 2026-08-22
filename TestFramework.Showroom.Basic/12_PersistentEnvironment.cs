using TestFramework.Core.Artifacts;
using TestFramework.Core.Environment;
using TestFramework.Core.Logging;
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

//doc: Keep the expensive part. Rebuild the cheap part.
//doc:
//doc: A run is isolated from every other run, which is the property everything else in this framework
//doc: rests on - and it is also why a suite that starts a database per run spends its life starting
//doc: databases. `PersistentEnvironmentContext` is the way out: it keeps chosen components alive across
//doc: runs while everything that depends on them is still rebuilt per run.
//doc:
//doc: This file is the stripped-down lab version. No Docker, no config overlays, no helpful wrappers -
//doc: just the raw Core primitive standing in the light where everybody can inspect the bolts and judge
//doc: the welds. Chapter A9 in the cloud lane is the same idea with real containers under it.
//doc:
//doc: The deal is:
//doc:
//doc: 1. A shared root component is expensive enough to earn persistence.
//doc: 2. A worker component depends on that root but stays per-run.
//doc: 3. Each run gets fresh state where freshness matters, and reused state where rebuilding would be
//doc:    waste.
//doc:
//doc: If that sounds obvious, good. Good architecture should sound obvious right after it stops wasting
//doc: your afternoon.

public class PersistentEnvironmentContextSample(ITestOutputHelper output)
{
    //doc: One test, and it is an experiment rather than a scenario: two runs of the same timeline against
    //doc: one persistent context, and then a ledger of what was built and disposed.
    //doc:
    //doc: The four assertions in the middle are the entire claim. The shared state is the *same object* in
    //doc: both runs; the worker is not; and each worker points at that same shared root. Then the counters:
    //doc: one shared create, two worker creates, **zero** shared disposals while the context is open, two
    //doc: worker disposals. The shared root is finally disposed after the `await using` block closes -
    //doc: nothing persists forever, not even our favourite shortcuts.
    //doc:
    //doc: Both runs report into one output helper, so the panel below holds two reports back to back. What
    //doc: they show is that each run is a whole run - its own environment step in `Prepare`, its own
    //doc: `require-worker`, its own teardown - and that is worth seeing, because reuse is not the same as
    //doc: skipping work.
    //doc:
    //doc: What the log does *not* show is which components were built, so it cannot tell you the shared root
    //doc: was created once. Only the tracker can, which is why this chapter counts instead of reading.

    [Fact]
    public async Task Reuses_the_shared_root_but_keeps_run_components_per_run()
    {
        // The setup owns the environment blueprint and the tracker. The tracker
        // is our clipboard for proving what was created once and what was paid
        // for again. Science loves clipboards. So do auditors.
        ShowroomPersistentSetup setup = new();
        Timeline timeline = Timeline.Create()
            .Trigger(new RequireWorkerStep())
            .Build();

        // CreateAsync, not the constructor: constructing one blocks the calling thread for the whole
        // bootstrap and deadlocks under a SynchronizationContext, which is why that overload is now
        // marked obsolete. A teaching chapter should show the shape you want copied.
        await using (PersistentEnvironmentContext<ShowroomPersistentSetup> persistent =
                     await PersistentEnvironmentContext<ShowroomPersistentSetup>.CreateAsync(setup))
        {
            // Same timeline, same persistent context, two separate runs. If the
            // primitive works, the shared root survives and the worker does not. Very Darwinian. Very efficient.
            TimelineRun firstRun = await timeline.SetupRun(output)
                .SetEnv(persistent.CreateEnvironment())
                .RunAsync();

            TimelineRun secondRun = await timeline.SetupRun(output)
                .SetEnv(persistent.CreateEnvironment())
                .RunAsync();

            firstRun.EnsureRanToCompletion();
            secondRun.EnsureRanToCompletion();

            SharedRuntimeState firstShared = Assert.IsType<SharedRuntimeState>(firstRun.EnvironmentContext.GetState(ShowroomPersistentEnvironment.SharedComponentId));
            SharedRuntimeState secondShared = Assert.IsType<SharedRuntimeState>(secondRun.EnvironmentContext.GetState(ShowroomPersistentEnvironment.SharedComponentId));
            RunScopedRuntimeState firstWorker = Assert.IsType<RunScopedRuntimeState>(firstRun.EnvironmentContext.GetState(ShowroomPersistentEnvironment.WorkerComponentId));
            RunScopedRuntimeState secondWorker = Assert.IsType<RunScopedRuntimeState>(secondRun.EnvironmentContext.GetState(ShowroomPersistentEnvironment.WorkerComponentId));

            Assert.Same(firstShared, secondShared);
            Assert.NotSame(firstWorker, secondWorker);
            Assert.Same(firstShared, firstWorker.SharedRoot);
            Assert.Same(secondShared, secondWorker.SharedRoot);

            // During the lifetime of the persistent context, the shared root has
            // no business being disposed. The worker, on the other hand, gets
            // built and torn down like a mayfly with a work order and a short lease.
            Assert.Equal(1, setup.Tracker.SharedCreates);
            Assert.Equal(2, setup.Tracker.WorkerCreates);
            Assert.Equal(0, setup.Tracker.SharedDisposals);
            Assert.Equal(2, setup.Tracker.WorkerDisposals);
        }

        // Once the context is gone, the bill finally comes due for the shared
        // root as well. Nothing persists forever. Not even our favorite shortcuts.
        Assert.Equal(1, setup.Tracker.SharedDisposals);
    }

    //doc: Everything below is the machinery the test drives, and it is worth reading in order.
    //doc:
    //doc: The setup is the contract surface: how to build a full environment, and which component roots
    //doc: deserve persistence. Note that the second question is answered by identifier, in one list. A tiny
    //doc: constitution for tiny machinery.

    public sealed class ShowroomPersistentSetup : IPersistentEnvironmentSetup
    {
        // The setup is the contract surface: how to build a full environment,
        // and which component roots deserve persistence. A tiny constitution for tiny machinery.
        public PersistentEnvironmentTracker Tracker { get; } = new();

        public IEnvironmentProvider CreateEnvironment() => new ShowroomPersistentEnvironment(Tracker);

        public IReadOnlyCollection<EnvComponentIdentifier> GetPersistentComponentIdentifiers()
            => [ShowroomPersistentEnvironment.SharedComponentId];
    }

    //doc: The environment declares both components in one shape, and `MapResourceKind` is how a step's
    //doc: stated requirement finds the component that satisfies it. Persistence is a policy choice about a
    //doc: component, not a separate environment type with its own parade.
    //doc:
    //doc: `IPersistentEnvironmentStateSink` is the seam that makes reuse possible: a fresh environment
    //doc: instance is built per run, and the context injects the already-created state back into it. Reuse
    //doc: without mystery, which is a rare pleasure.

    private sealed class ShowroomPersistentEnvironment : EnvironmentProviderBase, IPersistentEnvironmentStateSink
    {
        private readonly Dictionary<EnvComponentIdentifier, object?> _persistentStates = [];

        public static readonly EnvComponentIdentifier SharedComponentId = "shared-root";
        public static readonly EnvComponentIdentifier WorkerComponentId = "worker";

        public ShowroomPersistentEnvironment(PersistentEnvironmentTracker tracker)
        {
            // Register the persistent root and the per-run dependent in one
            // environment shape. Persistence is a policy choice, not a separate
            // environment type with its own parade.
            AddComponent(new SharedRootComponent(tracker));
            AddComponent(new WorkerComponent(tracker));
            MapResourceKind("showroom.worker", WorkerComponentId);
        }

        // The persistent context injects already-created runtime state back into
        // fresh environment instances through this sink. Reuse without mystery. A rare pleasure.
        public void SetPersistentState(EnvComponentIdentifier identifier, object? state)
            => _persistentStates[identifier] = state;

        public SharedRuntimeState GetSharedRootState()
            => (SharedRuntimeState)_persistentStates[SharedComponentId]!;

        public static ShowroomPersistentEnvironment Unwrap(IEnvironmentProvider environment)
        {
            // PersistentEnvironmentContext wraps the environment provider. The
            // worker component needs the concrete environment again so it can
            // inspect the seeded persistent state without composing a letter to abstraction.
            while (environment is IEnvironmentProviderProxy proxy)
                environment = proxy.InnerEnvironment;

            return (ShowroomPersistentEnvironment)environment;
        }
    }

    //doc: The expensive machinery, and the one line that makes it expensive-but-tolerable:
    //doc: `ReuseMode.PersistentContext`. A component says for itself whether it may be reused; the setup
    //doc: says which of those this context actually keeps. Both have to agree - name a component in the
    //doc: setup that never opted in and the context refuses to start rather than quietly running per-run.
    //doc:
    //doc: A component is a create/deconstruct pair and a dependency list, and both halves get the run's
    //doc: services, variables, artifacts and logger - so a component can do real work, and say so in the
    //doc: report while doing it.

    private sealed class SharedRootComponent(PersistentEnvironmentTracker tracker) : EnvComponent
    {
        // This is the expensive machinery we refuse to rebuild every run. Principles are nice. Saved minutes are nicer.
        public override EnvComponentIdentifier Id => ShowroomPersistentEnvironment.SharedComponentId;

        public override EnvComponentReuseMode ReuseMode => EnvComponentReuseMode.PersistentContext;

        public override IReadOnlyList<EnvComponentIdentifier> Dependencies => [];

        public override Task<object?> CreateAsync(IEnvironmentProvider environment, IServiceProvider serviceProvider, VariableStore variableStore, ArtifactStore artifactStore, ScopedLogger logger, CancellationToken cancellationToken)
        {
            tracker.SharedCreates++;
            return Task.FromResult((object?)new SharedRuntimeState(Guid.NewGuid().ToString("N")));
        }

        public override Task DeconstructAsync(object? state, IEnvironmentProvider environment, IServiceProvider serviceProvider, VariableStore variableStore, ArtifactStore artifactStore, ScopedLogger logger, CancellationToken cancellationToken)
        {
            tracker.SharedDisposals++;
            return Task.CompletedTask;
        }
    }

    //doc: The worker declares the shared root as a dependency and states no reuse mode, so it lands on the
    //doc: per-run side of the border. It gets a fresh identity every run and anchors itself to the reused
    //doc: root like a professional freeloader - which is exactly the arrangement being tested.

    private sealed class WorkerComponent(PersistentEnvironmentTracker tracker) : EnvComponent
    {
        // This component still lives on the per-run side of the border. It gets
        // a fresh identity each run, but it anchors itself to the reused root like a professional freeloader.
        public override EnvComponentIdentifier Id => ShowroomPersistentEnvironment.WorkerComponentId;

        public override IReadOnlyList<EnvComponentIdentifier> Dependencies => [ShowroomPersistentEnvironment.SharedComponentId];

        public override Task<object?> CreateAsync(IEnvironmentProvider environment, IServiceProvider serviceProvider, VariableStore variableStore, ArtifactStore artifactStore, ScopedLogger logger, CancellationToken cancellationToken)
        {
            tracker.WorkerCreates++;
            SharedRuntimeState sharedRoot = ShowroomPersistentEnvironment.Unwrap(environment).GetSharedRootState();
            return Task.FromResult((object?)new RunScopedRuntimeState(Guid.NewGuid(), sharedRoot));
        }

        public override Task DeconstructAsync(object? state, IEnvironmentProvider environment, IServiceProvider serviceProvider, VariableStore variableStore, ArtifactStore artifactStore, ScopedLogger logger, CancellationToken cancellationToken)
        {
            tracker.WorkerDisposals++;
            return Task.CompletedTask;
        }
    }

    //doc: The timeline needs exactly one step: something that *demands* the worker. `IHasEnvironmentRequirements`
    //doc: is how a step says so, and it matters more than it looks: the environment creates the components
    //doc: that were *asked for*, not the ones that were declared. A run collects the requirements its steps
    //doc: state, plus whatever its tracked artifacts imply, and builds that set. Declaring a component you
    //doc: never use costs nothing at all.

    private sealed class RequireWorkerStep : Step<EmptyStepResultContext>, IHasEnvironmentRequirements
    {
        // The timeline only needs one trigger here: something that demands the
        // worker component so the environment contract has to prove itself under questioning.
        public override string Name => "require-worker";

        public override string Description => "Forces the worker component to be created for the run.";

        public override bool DoesReturn => false;

        public IReadOnlyCollection<EnvironmentRequirement> GetEnvironmentRequirements(VariableStore variableStore)
            => [new("showroom.worker", "worker")];

        public override Task<EmptyStepResultContext?> Execute(IServiceProvider serviceProvider, VariableStore variableStore, ArtifactStore artifactStore, ScopedLogger logger, CancellationToken cancellationToken)
            => Task.FromResult<EmptyStepResultContext?>(null);

        public override Step<EmptyStepResultContext> Clone() => new RequireWorkerStep().WithClonedOptions(this);

        public override void DeclareIO(StepIOContract contract)
        {
        }

        public override StepInstance<Step<EmptyStepResultContext>, EmptyStepResultContext> GetInstance() => new(this);
    }

    //doc: Two state records and a counter. The worker state carries both its own run identity and a pointer
    //doc: back to the shared root, which is what makes the assertion story blunt, undeniable and pleasantly
    //doc: rude - and a tiny ledger beats a long speech about what persistence saved.

    public sealed record SharedRuntimeState(string Token);

    // The worker state carries both its own run identity and a pointer back to
    // the shared root. That makes the assertion story blunt, undeniable, and pleasantly rude.
    public sealed record RunScopedRuntimeState(Guid RunId, SharedRuntimeState SharedRoot);

    public sealed class PersistentEnvironmentTracker
    {
        // A tiny ledger beats a long speech. These counters show exactly what
        // persistence saved and what still behaved per-run, which is more than can be said for most status meetings.
        public int SharedCreates { get; set; }

        public int SharedDisposals { get; set; }

        public int WorkerCreates { get; set; }

        public int WorkerDisposals { get; set; }
    }
}
