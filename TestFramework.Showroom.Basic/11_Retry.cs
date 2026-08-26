using TestFramework.Core.Artifacts;
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Timelines;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

//doc: Retry is a property of the step contract, not a favour one transport grants. So this chapter proves
//doc: it with a step that has no transport at all: the failure is in-process, deliberate, and repeatable.
//doc: No network fog, no emulator noise, just the modifier standing there on its own merits like a very
//doc: smug safety mechanism.
//doc:
//doc: Two numbers are worth getting right before reading the code. Retry is **off by default** -
//doc: `MaxRetryCount` starts at zero, so a step runs once unless you say otherwise. And `WithRetry(3, …)`
//doc: means three *retries*: up to four attempts in total, the first one plus three more. The delay
//doc: strategy is separate - `CalcDelays.Fixed(TimeSpan.Zero)` here so the chapter does not spend real
//doc: seconds proving a point, `Exponential` if you say nothing, and `Linear` or `None` if you would rather
//doc: not think about powers of two.

public class Retry_Basic(ITestOutputHelper outputHelper)
{
    //doc: The probe is the whole verification strategy: a counter outside the step, so the test can state
    //doc: exactly how many times the step body ran rather than inferring it from the log.

    private readonly RetryProbe _probe = new();

    //doc: This chapter builds its timeline inside the test rather than in a field. Both are fine - a field
    //doc: is worth it when several tests share one plan, and here nothing does.
    //doc:
    //doc: The assertions are the interesting part. `_probe.Attempts` is 2, not 4: retrying stops at the
    //doc: first success, and the run reports `PASS` even though something in it failed once. And
    //doc: `LastResult` is exactly what its name says - the last attempt's result, which is the successful
    //doc: one. The failed attempt is not erased: the report below carries `LOGS ATTEMPT 1` with the
    //doc: exception and the words `retry scheduled`, then `LOGS ATTEMPT 2` with the success, and the Flow
    //doc: Trace marks the second pass `[#0:r2]`. A step that needed two goes says so forever.

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

    //doc: The rest of the file is a custom step, and this is the first time the Showroom writes one. It is
    //doc: worth reading as a checklist, because the surface is small and every member on it is load-bearing:
    //doc:
    //doc: - `Name` and `Description` are what the run's report calls it. They are not decoration - a step
    //doc:   nobody can identify in a report is a step nobody can debug.
    //doc: - `DoesReturn` says whether a result exists to bind. Result bindings are skipped when it is false.
    //doc: - `Clone()` exists because a timeline is a frozen plan that many runs execute, so each run needs
    //doc:   its own instance. `WithClonedOptions(this)` carries the retry, timeout and naming over.
    //doc: - `Execute` does the work, and gets one `RunContext` handed to it. Everything a step is allowed
    //doc:   to know arrives on it: `Variables` and `Artifacts` (the two channels a run communicates
    //doc:   through), `Services`, `Logger`, `Values` for where the run's resources ended up, `State` for
    //doc:   anything live the run has to keep, `Attempt` for which try this is - and `Deadline`, which is
    //doc:   how long this step has and the token that fires when it runs out. It used to be four loose
    //doc:   parameters and no deadline at all, which meant a step could not tell "my time is up" from "the
    //doc:   run was cancelled" and had to guess a margin to say anything useful about a timeout.
    //doc: - Throwing is how a step fails; there is no result code to remember to check.
    //doc: - `GetInstance()` wraps the step in the per-run instance the runner tracks attempts and results on.
    //doc: - `DeclareIO` is the contract from chapter 10. Empty here, honestly so: this step reads nothing
    //doc:   and writes nothing.

    private sealed class EventuallySuccessfulStep(RetryProbe probe) : Step<TextResultContext>
    {
        public override string Name => "Eventually Successful";

        public override string Description => "Fails once, then succeeds so the retry modifier has observable work to do.";

        public override bool DoesReturn => true;

        public override Step<TextResultContext> Clone() => new EventuallySuccessfulStep(probe).WithClonedOptions(this);

        public override Task<TextResultContext?> Execute(RunContext context)
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

    //doc: And the result type. A step's result is a `StepResultContext`, which is what makes it bindable
    //doc: into a variable and inspectable through `LastResult`. A record is usually enough.

    private sealed record TextResultContext(string Value) : StepResultContext;
}
