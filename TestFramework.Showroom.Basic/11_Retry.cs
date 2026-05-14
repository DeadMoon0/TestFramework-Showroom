using TestFramework.Core.Artifacts;
using TestFramework.Core.Logging;
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

public class Retry_Basic(ITestOutputHelper outputHelper)
{
    private readonly RetryProbe _probe = new();

    [Fact]
    public async Task Run()
    {
        // Retry is a property of the step contract, not a special favor granted
        // by one transport. That is why this sample keeps the failure in-process:
        // no network fog, no emulator noise, just the modifier standing there on
        // its own merits like a very smug safety mechanism.
        Timeline timeline = Timeline.Create()
            .Trigger(new EventuallySuccessfulStep(_probe))
                .Name("transient")
                .WithRetry(3, CalcDelays.Fixed(TimeSpan.Zero))
            .Build();

        TimelineRun run = await timeline.SetupRun(outputHelper).RunAsync();

        run.EnsureRanToCompletion();
        Assert.Equal(2, _probe.Attempts);
        Assert.Equal("success", Assert.IsType<TextResultContext>(run.Step("transient").LastResult.Result).Value);
    }

    private sealed class RetryProbe
    {
        public int Attempts { get; private set; }

        public int NextAttempt()
        {
            Attempts++;
            return Attempts;
        }
    }

    private sealed class EventuallySuccessfulStep(RetryProbe probe) : Step<TextResultContext>
    {
        public override string Name => "Eventually Successful";

        public override string Description => "Fails once, then succeeds so the retry modifier has observable work to do.";

        public override bool DoesReturn => true;

        public override Step<TextResultContext> Clone() => new EventuallySuccessfulStep(probe).WithClonedOptions(this);

        public override Task<TextResultContext?> Execute(IServiceProvider serviceProvider, VariableStore variableStore, ArtifactStore artifactStore, ScopedLogger logger, CancellationToken cancellationToken)
        {
            // First attempt fails on purpose. Otherwise this is not a retry demo,
            // it is just a very confident no-op with a stopwatch attached and opinions about resilience.
            if (probe.NextAttempt() == 1)
            {
                throw new InvalidOperationException("Transient failure for retry demo.");
            }

            return Task.FromResult<TextResultContext?>(new("success"));
        }

        public override StepInstance<Step<TextResultContext>, TextResultContext> GetInstance() =>
            new(this);

        public override void DeclareIO(StepIOContract contract)
        {
        }
    }

    private sealed record TextResultContext(string Value) : StepResultContext;
}
