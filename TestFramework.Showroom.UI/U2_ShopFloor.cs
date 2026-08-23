using TestFramework.Core.Timelines;
using TestFramework.UI.Browser;
using TestFramework.UI.Browser.Layouting;
using TestFramework.UI.Browser.Reading;
using TestFramework.UI.Browser.Targeting;
using Xunit.Abstractions;

namespace TestFramework.Showroom.UI;

// ══════════════════════════════════════════════════════════════════════════════
//  CHAPTER U2 - THE SHOP FLOOR
//
//  U1 proved the browser can shop. This chapter proves it can BEHAVE like a
//  hand and a keyboard: the pointer rests on a product and the stock tip lives
//  exactly as long as it stays; a search is typed keystroke by keystroke and
//  the shelf thins out; a product is dragged onto the cart and lands; the
//  checkout confirms not in words but on an attribute; and the browser reads
//  back the cookie the shop planted - straight from the jar, where even an
//  HttpOnly cookie would answer.
//
//  Same container, same rule as U1: nobody in this file knows an address, and
//  this time nobody speaks loosely either - the run's audit stays empty.
// ══════════════════════════════════════════════════════════════════════════════

//doc: The UI lane's pointer and keyboard verbs - hover and leave, typed keystrokes, drag and drop,
//doc: attribute waits, cookie reads and first-party scrolling - against the containerized storefront.
[Trait("Category", "DockerSmoke")]
public class U2_ShopFloor(ITestOutputHelper output)
{
    private static readonly Timeline _timeline = Timeline.Create()

        // ─── The pointer arrives, and leaves ──────────────────────────────────
        // The stock tip exists only under the pointer. Both halves are the
        // behaviour: MouseAway is how a test says "the hand moved on", and
        // ExpectNot WAITS for the tip to go - absence is never assumed.
        .Trigger(BrowserExt.Session("storefront")
            .Navigate("/")
            .Hover(Target.Text("Anvil"))
            .Expect("3 in stock")
            .MouseAway()
            .ExpectNot("3 in stock")

            // Typed, not filled: the filter listens to the keys themselves, so
            // this is Type - Fill would be one motion, right for forms and
            // wrong for behaviour that happens per keystroke.
            .Type("Search products", "rope"))
            .Name("browsing")

        // The shelf thins out as a count, waited on rather than asserted once.
        .WaitForEvent(BrowserExt.Events.CountIs("storefront", Target.Css("shop-product:not([hidden])"), 1))
            .WithTimeOut(TimeSpan.FromSeconds(10)).Name("filtered")

        // ─── A drag that has to land ──────────────────────────────────────────
        // Pressed, moved, released: the drop zone only reacts to the HTML5 drag
        // contract, so "1 item in cart" is proof the pointer really travelled.
        .Trigger(BrowserExt.Session("storefront")
            .Read(Value.Count(Target.Css("shop-product:not([hidden])")), "visibleProducts")
            .Fill("Search products", "")
            .DragTo(Target.Text("Rope"), Target.Section("Cart"))
            .Expect("1 item in cart")
            .Click("Checkout now")
            .Read(Value.Cookie("storefront-visited"), "visited"))
            .Name("shopping")

        // ─── The confirmation nobody prints ───────────────────────────────────
        // The cart reports on data-state, not in a visible word - the channel
        // components actually use. The attribute wait watches exactly that.
        .WaitForEvent(BrowserExt.Events.AttributeEquals("storefront", Target.Section("Cart"), "data-state", "confirmed"))
            .WithTimeOut(TimeSpan.FromSeconds(10)).Name("confirmed")

        // ─── To the end of the record ─────────────────────────────────────────
        // First-party scrolling, pinned by geometry: InViewport can only pass
        // if the page really moved.
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
