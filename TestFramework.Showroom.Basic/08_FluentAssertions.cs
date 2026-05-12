using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Assertions;
using TestFramework.Core.Variables;
using TestFrameworkLocalIO;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

public class RunAssertions_Basic(ITestOutputHelper outputHelper)
{
    // The fluent assertion layer exists so you can ask the run direct, readable
    // questions instead of spelunking through arrays and hoping index 3 still
    // means what it meant last Tuesday before the refactor and the regrettable optimism.

    private readonly Timeline _timeline = Timeline.Create()
        .Trigger(Simple.Simple.Trigger.MessageBox("Is anyone out there?"))
            .Name("ping")
        //    ^ Name the step once. Future-you gets stable lookups instead of
        //      archaeological work and the slow realization that index-based reasoning was a cry for help.
        .Build();

    [Fact]
    public async Task Assert_StepCompleted()
    {
        var run = await this._timeline.SetupRun(outputHelper).RunAsync();
        run.EnsureRanToCompletion();

        run.Step("ping").Should().HaveCompleted();
        //               ^ Readable on success, useful on failure. That is the bar. It is not a heroic bar.
    }

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

public class RunAssertions_ForEach(ITestOutputHelper outputHelper)
{
    // Once a step label appears in a loop, you usually want to assert all of the
    // iterations together instead of hand-checking each one like a tax auditor with trust issues.

    private readonly Timeline _timeline = Timeline.Create()
        .ForEach(["Alice", "Bob", "Charlie"], "item", loop =>
        {
            loop.Trigger(Simple.Simple.Trigger.MessageBox(Var.Ref<string>("item"))).Name("greet");
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

public class RunAssertions_Scope(ITestOutputHelper outputHelper)
{
    // Assertion scopes are for the days when one failure is never the whole
    // story. Collect the damage first, then report it as one complete problem like a proper incident report with better lighting.

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
        //         ^ Inside the scope, failures are collected instead of thrown immediately, which is a polite way of lining up bad news.
        {
            run.Step("check").Should().HaveCompleted();
            run.Step("check").Should().HaveCompleted(); // Still redundant. Still intentional. We are teaching a shape, not winning a brevity contest.
        }
        // When the scope closes, all collected failures arrive together. One report. One moment of truth. Maybe one dramatic exhale.
    }
}
