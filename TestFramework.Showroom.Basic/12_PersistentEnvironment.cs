using TestFramework.Core.Artifacts;
using TestFramework.Core.Environment;
using TestFramework.Core.Logging;
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;

namespace TestFramework.Showroom.Basic;

// ══════════════════════════════════════════════════════════════════════════════
//  CORE PERSISTENCE DIVISION - MODULE 12
//  "Keep The Expensive Part. Rebuild The Cheap Part."
//
//  This file is the stripped-down lab version of persistence. No Docker. No
//  config overlays. No helpful wrappers. Just the raw Core primitive standing
//  in the light where everybody can inspect the bolts and judge the welds.
//
//  The deal is simple:
//    1. A shared root component is expensive enough to earn persistence.
//    2. A worker component depends on that root but remains per-run.
//    3. Each run gets fresh runtime state where freshness matters and reused
//       state where rebuilding would be wasteful.
//
//  If that sounds obvious, good. Good architecture should sound obvious right
//  after it stops wasting your time and your afternoon.
// ══════════════════════════════════════════════════════════════════════════════

public class PersistentEnvironmentContextSample
{
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
            TimelineRun firstRun = await timeline.SetupRun()
                .SetEnv(persistent.CreateEnvironment())
                .RunAsync();

            TimelineRun secondRun = await timeline.SetupRun()
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

    public sealed class ShowroomPersistentSetup : IPersistentEnvironmentSetup
    {
        // The setup is the contract surface: how to build a full environment,
        // and which component roots deserve persistence. A tiny constitution for tiny machinery.
        public PersistentEnvironmentTracker Tracker { get; } = new();

        public IEnvironmentProvider CreateEnvironment() => new ShowroomPersistentEnvironment(Tracker);

        public IReadOnlyCollection<EnvComponentIdentifier> GetPersistentComponentIdentifiers()
            => [ShowroomPersistentEnvironment.SharedComponentId];
    }

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