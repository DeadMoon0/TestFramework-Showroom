using TestFramework.Core.Timelines;
using TestFramework.Showroom.UI;
using TestFramework.UI.Browser;
using TestFramework.UI.Browser.Structure;
using TestFramework.UI.Browser.Targeting;
using TestFramework.UI.Structure;
using Xunit.Abstractions;

namespace TestFramework.Showroom.UI;

// ══════════════════════════════════════════════════════════════════════════════
//  CHAPTER U1 - THE STOREFRONT
//
//  The browser is one more door into the system under test. This chapter opens
//  it: a container hosts the storefront, a real browser walks in, presses the
//  buttons a customer would press, and the run keeps the receipts.
//
//  Three things to watch for. The test never says where the site is - the
//  container publishes that. It never says WHAT a button is, only what it says -
//  and one of the buttons deliberately says more than the test does. And when it
//  checks the page's structure, it names the site's own elements, not the markup
//  fashion of the year.
// ══════════════════════════════════════════════════════════════════════════════

//doc: The UI lane drives a containerized site with a real browser, addressed by identifier and
//doc: spoken to in the words a customer sees.
[Trait("Category", "DockerSmoke")]
public class U1_Storefront(ITestOutputHelper output)
{
    // ─── What the catalogue is built like ─────────────────────────────────────
    // The site's own element names, a cardinality, and one attribute worth a rule.
    // Subset matching absorbs everything this expectation does not mention, so a
    // redesign that adds a banner changes nothing here.

    private static readonly WebElementStructure Catalogue = WebElementStructure
        .OneElement("section")
            .Containing(x => x
                .OneElement("shop-product-list")
                    .WithAttribute("data-count", "3")
                    .Containing(inner => inner
                        .Exactly(3, "shop-product")
                            .WithAttribute("data-sku")));

    // ─── The timeline ──────────────────────────────────────────────────────────
    // A shopping trip, then an audit of the shop's shape. "Checkout" is pressed by
    // a test that does not know the button says "Checkout now" - which is the
    // point, and which the run will admit to.

    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(BrowserExt.Session("storefront")
            .Navigate("/")
            .Click("Add Anvil to cart")
            .Expect("1 item in cart")
            .Click("Add Rope to cart")
            .Expect("2 items in cart")
            .Click("Checkout")
            .Expect("Order placed"))
            .Name("shopping")
        .Trigger(BrowserExt.Page("storefront").CompareStructure(Target.Section("Catalogue"), Catalogue))
            .Name("catalogue-shape")
        .Build();

    [UiDockerFact]
    public async Task Run()
    {
        TimelineRun run = await _timeline
            .SetupRun(UiShowroom.BuildConfig().BuildServiceProvider(), output)
            .SetEnv(UiShowroom.CreateEnvironment())
            .RunAsync();

        run.EnsureRanToCompletion();

        // The browser really went where the container put the site - an address
        // nobody in this file has ever seen.
        run.UiUrl("storefront").Should().Contain("http");

        // The shape held, by the site's own element names.
        run.UiDifferences("catalogue-shape").Should().HaveNoItems();

        // And the confession: exactly one press needed a loose match - the button
        // that says "Checkout now" to a test that said "Checkout". Resilience is
        // not the framework being vague; it is the framework being tolerant in
        // writing. A suite that wants none of it asserts HaveNoItems() here and
        // fixes its wording instead.
        run.UiLooseMatches("storefront").Should().HaveCount(1);
        run.UiWeakestMatch("storefront").Should().Contain("Loose");
    }
}
