using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Assertions;
using TestFramework.Core.Variables;
using TestFramework.LocalIO;
using TestFramework.Simple;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

//doc: `EnsureRanToCompletion()` answers one question: did everything finish. This chapter is about asking
//doc: narrower ones. The fluent layer lets you interrogate the run directly - a named step, a whole batch
//doc: of iterations, a variable - instead of spelunking through arrays and hoping index 3 still means what
//doc: it meant last Tuesday before the refactor and the regrettable optimism.
//doc:
//doc: Use these rather than a third-party assertion library, and not for style reasons. A framework
//doc: assertion is signalled to an attached debugger session and can be collected by an assertion scope. An
//doc: outside assertion throws on the spot, invisibly to both - it would work exactly once and then quietly
//doc: stop reporting anything, which is the worst possible way for a tool to fail.

//doc: Everything here starts with `Name(...)`. Name the step once and the lookup is stable; skip it and you
//doc: are doing archaeological work with index-based reasoning, which is usually a cry for help.

public class RunAssertions_Basic(ITestOutputHelper outputHelper)
{
    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(SimpleExt.Trigger.Message("Is anyone out there?"))
            .Name("ping")
        .Build();

    //doc: `run.Step("ping").Should().HaveCompleted()` - readable on success, specific on failure. That is
    //doc: the whole bar. It is not a heroic bar, and it is the difference between "the run failed" and
    //doc: "the step named ping failed".

    [Fact]
    public async Task Assert_StepCompleted()
    {
        var run = await this._timeline.SetupRun(outputHelper).RunAsync();
        run.EnsureRanToCompletion();

        run.Step("ping").Should().HaveCompleted();
        //               ^ Readable on success, useful on failure. That is the bar. It is not a heroic bar.
    }

    //doc: `And()` chains further assertions against the same handle. The second call here is redundant on
    //doc: purpose: the point is the shape of the API, and yes, it is a little theatrical.

    [Fact]
    public async Task Assert_StepCompleted_AndChained()
    {
        var run = await this._timeline.SetupRun(outputHelper).RunAsync();
        run.EnsureRanToCompletion();

        run.Step("ping").Should()
            .HaveCompleted()
            .And().HaveCompleted(); // Redundant on purpose. Chaining is part of the API shape and, frankly, a little theatrical.
    }
}

//doc: A loop gives many steps the same name, so assertions come in a plural form. `Steps("greet")` returns
//doc: the whole batch and `AllHaveCompleted()` judges it in one line, instead of hand-checking each
//doc: iteration like a tax auditor with trust issues.
//doc:
//doc: Note the literal collection passed to `ForEach` here. Chapter 07 used `Var.RefImmutable`; when the
//doc: collection is known where the timeline is written, the plain overload says so with less ceremony.

public class RunAssertions_ForEach(ITestOutputHelper outputHelper)
{
    private readonly Timeline _timeline = Timeline.Create()
        .ForEach(["Alice", "Bob", "Charlie"], "item", loop =>
        {
            loop.Trigger(SimpleExt.Trigger.Message(Var.Ref<string>("item")))
                .Name("greet");
            // ^ Same label, many instances. That is what makes grouped assertions work and your file remain shorter than a legal warning.
        })
        .Build();

    [Fact]
    public async Task Assert_AllIterationsCompleted()
    {
        var run = await this._timeline.SetupRun(outputHelper).RunAsync();
        run.EnsureRanToCompletion();

        run.Steps("greet").Should().AllHaveCompleted();
        //  ^ One assertion over the whole batch. Cleaner signal, less repetition, fewer chances for creative inconsistency.
    }
}

//doc: Last, the scope. Inside `using (run.AssertionScope())` a failing assertion is recorded instead of
//doc: thrown, and disposing the scope raises everything it collected as one exception. One failure is
//doc: rarely the whole story, and finding out about the second one on the next run is a slow way to work.
//doc:
//doc: What it collects is assertion failures, and only those. It is not a try/catch: anything else that
//doc: throws inside the block still leaves the block on the spot.

public class RunAssertions_Scope(ITestOutputHelper outputHelper)
{
    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(SimpleExt.Trigger.Message("Are we good?"))
            .Name("check")
        .Build();

    [Fact]
    public async Task Assert_AllAtOnce()
    {
        var run = await this._timeline.SetupRun(outputHelper).RunAsync();
        run.EnsureRanToCompletion();

        using (run.AssertionScope())
        //         ^ Inside the scope, failures are collected instead of thrown immediately, which is a polite way of lining up bad news.
        {
            run.Step("check").Should().HaveCompleted();
            run.Step("check").Should().HaveCompleted(); // Still redundant. Still intentional. We are teaching a shape, not winning a brevity contest.
        }
        // When the scope closes, all collected failures arrive together. One report. One moment of truth. Maybe one dramatic exhale.
    }

    //doc: One last thing about all three panels below: none of them mention an assertion. Assertions are
    //doc: not part of the run's own report - the run reports what it *did*, and the assertions are what you
    //doc: concluded from it afterwards. They surface in a debugger session, or in the exception when one
    //doc: fails; a passing run prints exactly what it would have printed without them.
}
