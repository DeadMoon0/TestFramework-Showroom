using System.Threading.Tasks;
using TestFramework.Core.Timelines;
using TestFramework.UI.Browser;
using TestFramework.UI.Browser.Configuration;
using Xunit.Abstractions;

namespace TestFramework.Showroom.UI;

//doc: A run says what happened. This chapter is about the other half: what it *looked* like.
//doc:
//doc: A browser test that walked four pages and a queue test that moved three messages come out of the
//doc: report as the same thing - a row of named boxes. So the run has somewhere to put pictures, and the
//doc: browser pack fills it in without being asked. The framework calls them **widgets**: a file the run
//doc: produced, described, and filed against the step and the attempt that produced it.
//doc:
//doc: Three things are worth taking from this chapter, and only the first needs any code.

[Trait("Category", "DockerSmoke")]
public class U3_Evidence(ITestOutputHelper output)
{
    //doc: ### 1. A picture you asked for, by name
    //doc:
    //doc: `Screenshot("...")` is a step in the trip like any other, and the name is how you get it back
    //doc: afterwards. Two of them here, either side of the checkout, which is the pair that answers "was
    //doc: the cart right before it went wrong".

    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(BrowserExt.Session("storefront")
            .Navigate("/")
            .Click("Add Anvil to cart")
            .Expect("1 item in cart")
            .Screenshot("cart-filled")
            .Click("Checkout")
            .Expect("Order placed")
            .Screenshot("order-placed"))
            .Label("shopping")
        .Build();

    [UiDockerFact]
    public async Task Run()
    {
        //doc: ### 2. How much is kept is the suite's decision
        //doc:
        //doc: `UiWidgetCapture.EveryAction` photographs after every action, which turns a failed step from
        //doc: "the button was not there" into a strip of pictures showing the page it was actually on.
        //doc: Worth the disk while a flow is being written or a flaky one chased down.
        //doc:
        //doc: The default is `OnFailure`, which is what U1 and U2 run on: nothing on a green suite,
        //doc: everything a reader needs on a red one. It is a suite-level choice rather than a
        //doc: per-machine one on purpose - what a run records is part of what the run means, and two
        //doc: people running the same suite should get the same evidence out of it.
        //doc:
        //doc: Whichever is set, a screenshot the test asked for by name is always taken. That is not a
        //doc: policy about evidence; it is the test saying what it wants recorded, and a setting that
        //doc: could silently discard it would make the verb a lie.

        TimelineRun run = await _timeline
            .SetupRun(RunServices.Owned(UiShowroom.BuildConfig(UiWidgetCapture.EveryAction).BuildServiceProvider()), output.WriteLine)
            .SetEnv(UiShowroom.CreateEnvironment())
            .RunAsync();

        run.EnsureRanToCompletion();

        // Both pictures exist, and the run can hand back where it put them - which is what a failure
        // message needs when it wants to name the file rather than describe it.
        run.UiScreenshot("storefront", "cart-filled").Should().NotBeNull();
        run.UiScreenshot("storefront", "order-placed").Should().NotBeNull();

        //doc: ### 3. Where it all went, and why not into a folder of our own
        //doc:
        //doc: Look at the **Widgets** section at the end of the report below. Every file the run produced
        //doc: is listed there once, named by where it came from - `shopping · attempt 1 · cart-filled` -
        //doc: with its size and its path relative to the run's output.
        //doc:
        //doc: That manifest is the point of the section existing. On a build agent there is no window to
        //doc: open, and a screenshot nobody can find is a screenshot that was not taken.
        //doc:
        //doc: This lane used to write its evidence into a folder of its own instead, named after a
        //doc: timestamp and a fresh identifier, sharing no key with the run that produced it. That is a
        //doc: private arrangement between one package and one tool, which a third package cannot join -
        //doc: and the reason a widget is a *run* concept rather than a browser one. A container's log and
        //doc: a generated site config travel the same way, filed against the component that produced
        //doc: them, and the debugger draws all three without knowing which package sent what.
        //doc:
        //doc: One more thing the same mechanism buys, which needs no code here at all: a run stopped at a
        //doc: breakpoint can be *asked* for a fresh picture. The last one taken is by definition out of
        //doc: date - the browser is sitting on a page nobody has photographed - so the tool has a button
        //doc: for it, and this lane answers by photographing every session it has open. Unless the
        //doc: browser is being held open for a person to look at, in which case it says so rather than
        //doc: quietly waiting.
    }
}
