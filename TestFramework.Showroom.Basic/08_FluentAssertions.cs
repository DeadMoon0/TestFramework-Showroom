using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Assertions;
using TestFrameworkLocalIO;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

public class FluentAssertions_Basic(ITestOutputHelper outputHelper)
{
    // Validation Division — Internal Memo #47.
    // Engineers before you used Debug.Assert and raw index arithmetic. We don't know what happened to them.
    // We do know they left behind a lot of off-by-one errors. This is the better way. You're welcome.

    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(Simple.Simple.Trigger.MessageBox("Is anyone out there?"))
            .Name("ping")
        //    ^ Name your step. Creates a paper trail. Also means you don't have to count to index 3 like an animal.
        .Build();

    [Fact]
    public async Task Assert_StepCompleted()
    {
        var run = await this._timeline.SetupRun(outputHelper).RunAsync();
        run.EnsureRanToCompletion();

        run.Step("ping").Should().HaveCompleted();
        //               ^ Reads like a sentence. Fails like a useful error message. Progress.
    }

    [Fact]
    public async Task Assert_StepCompleted_AndChained()
    {
        var run = await this._timeline.SetupRun(outputHelper).RunAsync();
        run.EnsureRanToCompletion();

        run.Step("ping").Should()
            .HaveCompleted()
            .And().HaveCompleted(); // verified twice. thorough. some would say excessive. those people are wrong.
    }
}

public class FluentAssertions_ForEach(ITestOutputHelper outputHelper)
{
    // Multi-subject batch compliance verification. Highly efficient.
    // All participants are expected to complete their assigned steps.
    // Non-completion is logged. Everything is logged. We have very good logs.

    private readonly Timeline _timeline = Timeline.Create()
        .ForEach(["Alice", "Bob", "Charlie"], "name", x => x
            .Trigger(LocalIO.Trigger.Cmd("echo Hello"))
                .Name("greet")
        //    ^ One label covers all iterations. Look them all up at once with Steps(). Maximum output, zero copy-paste.
        )
        .Build();

    [Fact]
    public async Task Assert_AllIterationsCompleted()
    {
        var run = await this._timeline.SetupRun(outputHelper).RunAsync();
        run.EnsureRanToCompletion();

        run.Steps("greet").Should().AllHaveCompleted();
        //  ^ Three subjects. One assertion. All accounted for. Thank you for your participation.
    }
}

public class FluentAssertions_Scope(ITestOutputHelper outputHelper)
{
    // You may have noticed that failing assertions stop the test immediately.
    // That's unfortunate. You fixed one thing. Now a second thing is broken. You fix that. A third thing. You may even wish to cry...
    // We've solved this. AssertionScope evaluates everything first, then reports all failures at once.
    // One detonation. Fully documented. Exactly as intended.

    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(Simple.Simple.Trigger.MessageBox("Are we good?"))
            .Name("check")
        .Build();

    [Fact]
    public async Task Assert_AllAtOnce()
    {
        var run = await this._timeline.SetupRun(outputHelper).RunAsync();
        run.EnsureRanToCompletion();

        using (run.AssertionScope())
        //         ^ Everything inside runs. No early exits. Failures are collected, not thrown. Yet.
        {
            run.Step("check").Should().HaveCompleted();
            run.Step("check").Should().HaveCompleted(); // redundant by design. trust the process.
        }
        // Scope disposed — if anything failed, you now get a MultipleAssertionsFailedException.
        // Complete. Numbered. Every single one. You're going to fix all of them today.
    }
}
