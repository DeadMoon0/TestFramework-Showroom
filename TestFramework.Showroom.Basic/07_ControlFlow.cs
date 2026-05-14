using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using TestFramework.Simple;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

public class ControlFlow_Conditional(ITestOutputHelper outputHelper)
{
    // Control flow lets the timeline make decisions without cloning half the
    // builder into separate tests just because one branch should not run and the other has delusions of relevance.

    private readonly Timeline _timeline = Timeline.Create()
        .Conditional(Var.RefImmutable<bool>("doPathA"), thenBranch =>
        {
            //           ^ Control flow reads immutable values because the path
            //             must be decided before execution starts moving and absolutely before anyone improvises a tragedy.
            thenBranch.Trigger(SimpleExt.Trigger.MessageBox("Hello from Path A"));
        })
        .Conditional(Var.RefImmutable<bool>("doPathB"), thenBranch =>
        {
            thenBranch.Trigger(SimpleExt.Trigger.MessageBox("Hello from Path B"));
        })
        .Build();

    [Fact]
    public async Task RunA()
    {
        var run = await this._timeline.SetupRun(outputHelper)
            .AddVariable("doPathA", true) // Setup-time values are immutable inputs by definition. Paperwork has spoken.
            .AddVariable("doPathB", false)
            .RunAsync();
        run.EnsureRanToCompletion();
    }

    [Fact]
    public async Task RunB()
    {
        var run = await this._timeline.SetupRun(outputHelper)
            .AddVariable("doPathA", false)
            .AddVariable("doPathB", true)
            .RunAsync();
        run.EnsureRanToCompletion();
    }
}

public class ControlFlow_ForEach(ITestOutputHelper outputHelper)
{
    // ForEach is what you use when repetition is real but copy-paste is beneath
    // your dignity. One loop definition, many concrete iterations, zero manual duplication rituals.

    private readonly Timeline _timeline = Timeline.Create()
        .ForEach(Var.RefImmutable<string[]>("messages"), "item", loop =>
        {
            loop.Trigger(SimpleExt.Trigger.MessageBox(Var.Ref<string>("item").Transform(item => $"Hello: {item}")));
        })
        .Build();

    [Fact]
    public async Task RunA()
    {
        var run = await this._timeline.SetupRun(outputHelper)
            .AddVariable<string[]>("messages", ["First", "Second", "Last"])
            .RunAsync();
        run.EnsureRanToCompletion();
    }
}