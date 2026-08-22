using Microsoft.Extensions.DependencyInjection;
using System.Net;
using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Assertions;
using TestFramework.Core.Variables;
using TestFramework.Web;
using TestFramework.Web.Trigger.IsLive;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Web;

//doc: An HTTP call is a step. Its response is a step result. That is the whole idea, and it is worth
//doc: stating plainly because a surprising number of test suites have arrived at the opposite conclusion
//doc: and now contain a helper class named `ApiHelperHelper` that nobody is willing to open.
//doc:
//doc: Two rules you will be tested on later:
//doc:
//doc: 1. **Timelines name an identifier, never a URL.** Here that identifier is `orders`. The address is
//doc:    somebody else's business and changes without warning, like the weather and the parking
//doc:    arrangements - and it is the environment, chosen by the test at the last possible moment, that
//doc:    decides what `orders` resolves to.
//doc: 2. **A non-2xx status is a result.** It is returned, it is asserted on, and it is not an exception.
//doc:    Only transport failures raise, because a socket that refuses to open genuinely has nothing to say
//doc:    about your business logic.
//doc:
//doc: What `orders` and the rest of the facility actually are is declared once, in `WebShowroom.cs` - a
//doc: database, an application and a stub, none of which say where they run.

//doc: First: is anyone home. `IsLive` asks at a stated level, and the two levels are different questions
//doc: the framework declines to conflate. `Reachable` proves the socket opened. `Healthy` proves the health
//doc: path answered. That is more discipline than most monitoring dashboards manage.

public class RestApi_Liveness(ITestOutputHelper outputHelper)
{
    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(WebExt.Api.IsLive("orders", ApiAlivenessLevel.Healthy))
            .Name("live")
        .Build();

    [DockerFact]
    [Trait("Category", "DockerSmoke")]
    public async Task Run()
    {
        // The provider is owned by this test, so it is named and disposed rather than
        // built inline and abandoned. BuildServiceProvider returns the concrete
        // ServiceProvider to make that ownership visible.
        await using ServiceProvider provider = WebShowroom.BuildConfig().BuildServiceProvider();

        TimelineRun run = await _timeline
            .SetupRun(provider, outputHelper)
            .SetEnv(WebShowroom.CreateEnvironment())
            .RunAsync();

        run.EnsureRanToCompletion();

        run.ApiProbe("live").Select(probe => probe.Success).Should().Be(true);
        // ^ The application answered. Everything after this point is its own fault.
    }
}

//doc: Then: send something, get something back. Every part of a request is variable-backed - path, route
//doc: values, query, headers, body. This is not decoration. It is what lets one timeline run with a hundred
//doc: different inputs without a single line of it being edited by a human under time pressure at the end
//doc: of a sprint.
//doc:
//doc: Two calls, two names, and afterwards four questions asked of two different steps: status, a response
//doc: header, and the body of the second call. Naming the steps is what makes that readable - `create` and
//doc: `list` rather than result[0] and result[1].
//doc:
//doc: Use the framework's own assertion surface here, and not for style reasons: those assertions are
//doc: signalled to an attached debugger session and can be collected by an assertion scope. An outside
//doc: assertion package would work exactly once and then quietly stop reporting anything, which is the
//doc: worst possible way for a tool to fail.

public class RestApi_PostAndRead(ITestOutputHelper outputHelper)
{
    private static readonly Timeline _timeline = Timeline.Create()
        .SetVariable("orderName", Var.Const("Calibration Order"))
        .Trigger(
            WebExt.Api.Http("orders")
                .Post("api/orders")
                .WithJsonBody(Var.Const(new { name = "Calibration Order", quantity = 3 }))
                .Call())
            .Name("create")
        .Trigger(WebExt.Api.Http("orders").Get("api/orders").Call())
            .Name("list")
        .Build();

    [DockerFact]
    [Trait("Category", "DockerSmoke")]
    public async Task Run()
    {
        // The provider is owned by this test, so it is named and disposed rather than
        // built inline and abandoned. BuildServiceProvider returns the concrete
        // ServiceProvider to make that ownership visible.
        await using ServiceProvider provider = WebShowroom.BuildConfig().BuildServiceProvider();

        TimelineRun run = await _timeline
            .SetupRun(provider, outputHelper)
            .SetEnv(WebShowroom.CreateEnvironment())
            .RunAsync();

        run.EnsureRanToCompletion();

        // Assertions go through the framework's own fluent surface. They are signalled
        // to the debugging interface and collected by assertion scopes. A third-party
        // assertion package would work exactly once and then quietly stop reporting
        // anything, which is the worst possible way for a tool to fail.
        run.ApiStatus("create").Should().Be(HttpStatusCode.Created);
        run.ApiHeader("create", "Location").Should().StartWith("/api/orders/");
        run.ApiStatus("list").Should().Be(HttpStatusCode.OK);
        run.ApiBody("list").Should().Contain("Calibration Order");
    }
}

//doc: And the rule that saves the most code: the status code is data. The application rejects an order with
//doc: no name, and the test asserts that it does. Nothing throws, nothing is caught, and no comment is
//doc: required explaining that the try/catch is "just for the negative case".
//doc:
//doc: Look at `EnsureRanToCompletion()` sitting there in a test about a rejected request. The run completed:
//doc: a 400 is not a failed step. The application was asked a question and gave a perfectly clear answer.

public class RestApi_UnsuccessfulStatusIsAResult(ITestOutputHelper outputHelper)
{
    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(
            WebExt.Api.Http("orders")
                .Post("api/orders")
                .WithJsonBody(Var.Const(new { name = "", quantity = 1 }))
                .Call())
            .Name("rejected")
        .Build();

    [DockerFact]
    [Trait("Category", "DockerSmoke")]
    public async Task Run()
    {
        // The provider is owned by this test, so it is named and disposed rather than
        // built inline and abandoned. BuildServiceProvider returns the concrete
        // ServiceProvider to make that ownership visible.
        await using ServiceProvider provider = WebShowroom.BuildConfig().BuildServiceProvider();

        TimelineRun run = await _timeline
            .SetupRun(provider, outputHelper)
            .SetEnv(WebShowroom.CreateEnvironment())
            .RunAsync();

        run.EnsureRanToCompletion();
        // ^ Note that the run completed. A rejected request is not a failed step.
        //   The application was asked a question and gave a perfectly clear answer.

        run.ApiStatus("rejected").Should().Be(HttpStatusCode.BadRequest);
        run.ApiBody("rejected").Should().Contain("Name is required");
    }
}
