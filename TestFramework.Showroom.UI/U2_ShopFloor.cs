using TestFramework.Core.Timelines;
using TestFramework.UI.Browser;
using TestFramework.UI.Browser.Layouting;
using TestFramework.UI.Browser.Reading;
using TestFramework.UI.Browser.Targeting;
using Xunit.Abstractions;

namespace TestFramework.Showroom.UI;

//doc: U1 proved a browser can shop. This chapter proves it can *behave* like a hand and a keyboard,
//doc: which is a harder claim and the one that decides whether a browser test is worth writing at all.
//doc:
//doc: Five interactions, each chosen because a weaker tool fakes it:
//doc:
//doc: - **The pointer rests, and leaves.** The stock tip exists only under the pointer, so both halves
//doc:   are the behaviour. `MouseAway` is how a test says "the hand moved on", and `ExpectNot` *waits*
//doc:   for the tip to go - absence is never assumed, which is the difference between proving a thing
//doc:   disappeared and checking before it appeared.
//doc: - **The search is typed, not filled.** The filter listens to the keys themselves. `Fill` is one
//doc:   motion, right for a form and wrong for behaviour that happens per keystroke.
//doc: - **The drag has to land.** The drop zone only reacts to the HTML5 drag contract, so
//doc:   "1 item in cart" is proof the pointer really travelled rather than that an event was dispatched.
//doc: - **The confirmation is never printed.** The cart reports on `data-state`, which is the channel
//doc:   components actually use, and the wait watches exactly that instead of a sentence someone might
//doc:   reword.
//doc: - **The cookie comes from the jar.** Read through the browser rather than out of the page, which
//doc:   is why an `HttpOnly` cookie would answer here and would be invisible to script.
//doc:
//doc: Same container and same rule as U1: nobody in this file knows an address. This time nobody speaks
//doc: loosely either - the run's audit stays empty, and the chapter asserts that rather than hoping.
[Trait("Category", "DockerSmoke")]
public class U2_ShopFloor(ITestOutputHelper output)
{
    private static readonly Timeline _timeline = Timeline.Create()

        .Trigger(BrowserExt.Session("storefront")
            .Navigate("/")
            .Hover(Target.Text("Anvil"))
            .Expect("3 in stock")
            .MouseAway()
            .ExpectNot("3 in stock")

            .Type("Search products", "rope"))
            .Name("browsing")

        // The shelf thins out as a count, waited on rather than asserted once.
        .WaitForEvent(BrowserExt.Events.CountIs("storefront", Target.Css("shop-product:not([hidden])"), 1))
            .WithTimeOut(TimeSpan.FromSeconds(10)).Name("filtered")

        // Pressed, moved, released - the search is cleared first so the shelf is whole again.
        .Trigger(BrowserExt.Session("storefront")
            .Read(Value.Count(Target.Css("shop-product:not([hidden])")), "visibleProducts")
            .Fill("Search products", "")
            .DragTo(Target.Text("Rope"), Target.Section("Cart"))
            .Expect("1 item in cart")
            .Click("Checkout now")
            .Read(Value.Cookie("storefront-visited"), "visited"))
            .Name("shopping")

        .WaitForEvent(BrowserExt.Events.AttributeEquals("storefront", Target.Section("Cart"), "data-state", "confirmed"))
            .WithTimeOut(TimeSpan.FromSeconds(10)).Name("confirmed")

        // First-party scrolling, pinned by geometry: InViewport can only pass if the page really moved.
        .Trigger(BrowserExt.Session("storefront").ScrollToBottom()).Name("to-the-end")
        .Trigger(BrowserExt.Page("storefront").CheckLayout(ExpectedLayout
            .InViewport(Target.TestId("provenance-end"))))
            .Name("record-visible")
        .Build();

    [UiDockerFact]
    public async Task Run()
    {
        TimelineRun run = await _timeline
            .SetupRun(UiShowroom.BuildConfig().BuildServiceProvider(), output)
            .SetEnv(UiShowroom.CreateEnvironment())
            .RunAsync();

        run.EnsureRanToCompletion();

        // The typed search left exactly one product on the shelf, counted by
        // the same ladder the verbs act with.
        run.Variable<int>("visibleProducts").Should().Be(1);

        // The cookie came from the browser's own jar, not from the page's DOM.
        run.Variable<string>("visited").Should().Be("yes");

        // U1 confesses one loose match; this chapter speaks exactly, and the
        // audit proves it - along with the fact that not one of these
        // interactions was a script.
        run.UiLooseMatches("storefront").Should().HaveNoItems();
        run.UiScripts("storefront").Should().HaveNoItems();
    }
}
