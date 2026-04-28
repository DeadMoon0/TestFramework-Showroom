using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

public class ControlFlow_Conditional(ITestOutputHelper outputHelper)
{
    // You want to skip part of the timeline? Why — what did it do to you? Fine, here you go:

    private readonly Timeline _timeline = Timeline.Create()
        .Conditional(Var.RefImmutable<bool>("doPathA"), thenBranch =>
        {
            //           ^ For control flows, use immutable references because I need to know what you want before I start running the timeline.
            thenBranch.Trigger(Simple.Simple.Trigger.MessageBox("Hello from Path A"));
        })
        .Conditional(Var.RefImmutable<bool>("doPathB"), thenBranch =>
        {
            thenBranch.Trigger(Simple.Simple.Trigger.MessageBox("Hello from Path B"));
        })
        .Build();

    [Fact]
    public async Task RunA()
    {
        var run = await this._timeline.SetupRun(outputHelper)
            .AddVariable("doPathA", true) // Every variable passed in at setup is an immutable reference and thus can be used.
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
    // Let copy-paste have an end. The solution — you ask? ForEach.

    private readonly Timeline _timeline = Timeline.Create()
        .ForEach(Var.RefImmutable<string[]>("messages"), "item", loop =>
        {
            loop.Trigger(Simple.Simple.Trigger.MessageBox(Var.Ref<string>("item").Transform(item => $"Hello: {item}")));
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