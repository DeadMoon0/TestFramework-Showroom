using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using TestFramework.Simple;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Basic;

//doc: A timeline is a plan, and a plan can branch and repeat. `Conditional` includes a section or leaves
//doc: it out; `ForEach` expands one definition into as many concrete iterations as the collection has
//doc: items. Both exist so that repetition does not mean cloning half the builder into separate tests
//doc: because one branch should not run and the other has delusions of relevance.
//doc:
//doc: Both take an **immutable** reference, and that is a signature, not a suggestion: `Var.RefImmutable`
//doc: is the only thing the overload accepts, and the IO validator rejects a plan in which any step would
//doc: write to that variable. The reason is structural. The shape of the run is worked out before
//doc: execution starts, so a value that decides the shape cannot be one a step invents halfway through.
//doc: (There are plain overloads too, for when the value is a literal - chapter 08 uses one.)

//doc: Two conditionals, two flags, one timeline. Nothing here is a runtime `if`: the branch that was not
//doc: taken is absent from the plan. Both panels below report a Main Stage of exactly one step - not two
//doc: steps of which one was skipped - and the two runs differ only in which message that step logged.

public class ControlFlow_Conditional(ITestOutputHelper outputHelper)
{
    private readonly Timeline _timeline = Timeline.Create()
        .Conditional(Var.RefImmutable<bool>("doPathA"), thenBranch =>
        {
            //           ^ Control flow reads immutable values because the path
            //             must be decided before execution starts moving and absolutely before anyone improvises a tragedy.
            thenBranch.Trigger(SimpleExt.Trigger.Message("Hello from Path A"));
        })
        .Conditional(Var.RefImmutable<bool>("doPathB"), thenBranch =>
        {
            thenBranch.Trigger(SimpleExt.Trigger.Message("Hello from Path B"));
        })
        .Build();

    //doc: Path A on, path B off.

    [Fact]
    public async Task RunA()
    {
        var run = await this._timeline.SetupRun(outputHelper)
            .AddVariable("doPathA", true) // Setup-time values are immutable inputs by definition. Paperwork has spoken.
            .AddVariable("doPathB", false)
            .RunAsync();
        run.EnsureRanToCompletion();
    }

    //doc: The same field, the same `Build()`, the flags the other way round. Worth stating plainly: a
    //doc: timeline is frozen and reusable, and a run is the disposable part. Two tests sharing one plan is
    //doc: the normal case, not a trick.

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

//doc: `ForEach` names a loop variable - here `item` - and every step inside the body can reference it like
//doc: any other variable. One loop definition, three concrete iterations, zero manual duplication rituals.
//doc:
//doc: The panel shows what "expanded" means, and it is more literal than you might expect: three items
//doc: become **six** steps. Each iteration gets a `Set Variable` step that binds `item`, followed by the
//doc: body. The loop is not a construct the runtime interprets - it is steps, like everything else, which
//doc: is why each iteration reports its own resolved value. Chapter 08 asserts on all of them at once.

public class ControlFlow_ForEach(ITestOutputHelper outputHelper)
{
    private readonly Timeline _timeline = Timeline.Create()
        .ForEach(Var.RefImmutable<string[]>("messages"), "item", loop =>
        {
            loop.Trigger(SimpleExt.Trigger.Message(Var.Ref<string>("item").Transform(item => $"Hello: {item}")));
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
