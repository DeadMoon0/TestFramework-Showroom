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
        // Retry belongs to the timeline step, not to a specific transport.
        // This sample keeps the failing operation in-process so you can see the modifier in isolation.
        Timeline timeline = Timeline.Create()
            .Trigger(new EventuallySuccessfulStep(_probe))
                .Name("transient")
                .WithRetry(3, CalcDelays.Fixed(TimeSpan.Zero))
            .Build();

        TimelineRun run = await timeline.SetupRun(outputHelper).RunAsync();

        run.EnsureRanToCompletion();
        Assert.Equal(2, _probe.Attempts);
        Assert.Equal("success", run.Step("transient").LastResult.Result);
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

    private sealed class EventuallySuccessfulStep(RetryProbe probe) : Step<string>
    {
        public override string Name => "Eventually Successful";

        public override string Description => "Fails once, then succeeds so the retry modifier has observable work to do.";

        public override bool DoesReturn => true;

        public override Step<string> Clone() => new EventuallySuccessfulStep(probe).WithClonedOptions(this);

        public override Task<string?> Execute(IServiceProvider serviceProvider, VariableStore variableStore, ArtifactStore artifactStore, ScopedLogger logger, CancellationToken cancellationToken)
        {
            if (probe.NextAttempt() == 1)
            {
                throw new InvalidOperationException("Transient failure for retry demo.");
            }

            return Task.FromResult<string?>("success");
        }

        public override StepInstance<Step<string>, string> GetInstance() =>
            new(this);

        public override void DeclareIO(StepIOContract contract)
        {
        }
    }
}
