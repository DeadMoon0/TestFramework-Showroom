using TestFramework.Core.Timelines;
using TestFramework.Showroom.UI;
using TestFramework.UI.Browser;
using TestFramework.UI.Browser.Structure;
using TestFramework.UI.Browser.Targeting;
using TestFramework.UI.Structure;
using Xunit.Abstractions;

namespace TestFramework.Showroom.UI;

//doc: A browser is one more door into the system under test, and this chapter opens it. A container
//doc: hosts the storefront, a real browser walks in, presses the buttons a customer would press, and the
//doc: run keeps the receipts.
//doc:
//doc: The lane is worth reading even if you never write a browser test, because it is where the family's
//doc: rules are hardest to keep and therefore easiest to see. Three of them are on display here:
//doc:
//doc: 1. **The test never says where the site is.** It says `storefront`. A container publishes the
//doc:    address when it starts, on a port the operating system chooses, and nothing in this file has
//doc:    ever seen it - which is why the same chapter would run unchanged against a deployed site.
//doc: 2. **It never says what a button *is*, only what it says.** No CSS selector, no test id on the
//doc:    happy path: `Click("Checkout")`, the way a person would describe it. One of the buttons
//doc:    deliberately says more than the test does, and the run admits it rather than hiding it.
//doc: 3. **A structure check names the site's own elements**, not the markup fashion of the year. The
//doc:    expectation below mentions `shop-product-list`, and a redesign that adds a banner around it
//doc:    changes nothing.

//doc: What the catalogue is built like: the site's own element names, a cardinality, and one attribute
//doc: worth a rule. Subset matching absorbs everything the expectation does not mention, which is what
//doc: makes this a rule about the shop rather than a snapshot of it - assert the whole tree and every
//doc: future change is a failure, including the ones nobody cares about.
[Trait("Category", "DockerSmoke")]
public class U1_Storefront(ITestOutputHelper output)
{
    private static readonly WebElementStructure Catalogue = WebElementStructure
        .OneElement("section")
            .Containing(x => x
                .OneElement("shop-product-list")
                    .WithAttribute("data-count", "3")
                    .Containing(inner => inner
                        .Exactly(3, "shop-product")
                            .WithAttribute("data-sku")));

    //doc: A shopping trip, then an audit of the shop's shape. Read the timeline as a sentence: navigate,
    //doc: add, add, check out - each `Expect` a wait rather than an assertion, so the test never races the
    //doc: page it is driving.
    //doc:
    //doc: `Click("Checkout")` is the interesting line. The button actually says "Checkout now", and the
    //doc: press lands anyway.

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

        // And the confession. Exactly one press needed a loose match - the button that says
        // "Checkout now" to a test that said "Checkout". Resilience is not the framework being vague;
        // it is the framework being tolerant in writing and then telling you it was. A suite that
        // wants none of that asserts HaveNoItems() here and fixes its own wording instead.
        run.UiLooseMatches("storefront").Should().HaveCount(1);
        run.UiWeakestMatch("storefront").Should().Contain("Loose");
    }
}
