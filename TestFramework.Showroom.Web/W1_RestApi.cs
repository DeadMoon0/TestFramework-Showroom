using System.Net;
using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Assertions;
using TestFramework.Core.Variables;
using TestFramework.Web;
using TestFramework.Web.Trigger.IsLive;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Web;

// ══════════════════════════════════════════════════════════════════════════════
//  WEB SYSTEMS DIVISION - PARTICIPANT ORIENTATION MODULE W1
//  "The Request, The Response, And The Persistent Myth That 404 Is An Emergency"
//
//  An HTTP call is a step. Its response is a step result. That is the whole idea,
//  and it is worth stating plainly because a surprising number of test suites have
//  arrived at the opposite conclusion and now contain a helper class named
//  ApiHelperHelper that nobody is willing to open.
//
//  Two rules you will be tested on later:
//    1. Timelines name an identifier. They never name a URL. The address is
//       somebody else's business and changes without warning, like the weather
//       and the parking arrangements.
//    2. A non-2xx status is a RESULT. It is returned, it is asserted on, and it is
//       not an exception. Only transport failures raise, because a socket that
//       refuses to open genuinely has nothing to say about your business logic.
// ══════════════════════════════════════════════════════════════════════════════

// ─── Module W1.1: Ask whether anyone is home ─────────────────────────────────

public class RestApi_Liveness(ITestOutputHelper outputHelper)
{
    // Reachable proves the socket opened. Healthy proves the health path answered.
    // They are different questions and the framework declines to conflate them,
    // which is more discipline than most monitoring dashboards manage.

    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(WebExt.Api.IsLive("orders", ApiAlivenessLevel.Healthy)).Name("live")
        .Build();

    [DockerFact]
    [Trait("Category", "DockerSmoke")]
    public async Task Run()
    {
        TimelineRun run = await _timeline
            .SetupRun(WebShowroom.BuildConfig().BuildServiceProvider(), outputHelper)
            .SetEnv(WebShowroom.CreateEnvironment())
            .RunAsync();

        run.EnsureRanToCompletion();

        run.ApiProbe("live").Select(probe => probe.Success).Should().Be(true);
        // ^ The application answered. Everything after this point is its own fault.
    }
}

// ─── Module W1.2: Send something, get something back ─────────────────────────

public class RestApi_PostAndRead(ITestOutputHelper outputHelper)
{
    // Every part of a request is variable-backed: path, route values, query, headers,
    // body. This is not decoration. It is what lets one timeline run with a hundred
    // different inputs without a single line of it being edited by a human under
    // time pressure at the end of a sprint.

    private static readonly Timeline _timeline = Timeline.Create()
        .SetVariable("orderName", Var.Const("Calibration Order"))
        .Trigger(WebExt.Api.Http("orders")
            .Post("api/orders")
            .WithJsonBody(Var.Const(new { name = "Calibration Order", quantity = 3 }))
            .Call()).Name("create")
        .Trigger(WebExt.Api.Http("orders").Get("api/orders").Call()).Name("list")
        .Build();

    [DockerFact]
    [Trait("Category", "DockerSmoke")]
    public async Task Run()
    {
        TimelineRun run = await _timeline
            .SetupRun(WebShowroom.BuildConfig().BuildServiceProvider(), outputHelper)
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

// ─── Module W1.3: The status code is data ────────────────────────────────────

public class RestApi_UnsuccessfulStatusIsAResult(ITestOutputHelper outputHelper)
{
    // The application rejects an order with no name. We assert that it does.
    // Nothing throws, nothing is caught, and no comment is required explaining
    // that the try/catch is "just for the negative case".

    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(WebExt.Api.Http("orders")
            .Post("api/orders")
            .WithJsonBody(Var.Const(new { name = "", quantity = 1 }))
            .Call()).Name("rejected")
        .Build();

    [DockerFact]
    [Trait("Category", "DockerSmoke")]
    public async Task Run()
    {
        TimelineRun run = await _timeline
            .SetupRun(WebShowroom.BuildConfig().BuildServiceProvider(), outputHelper)
            .SetEnv(WebShowroom.CreateEnvironment())
            .RunAsync();

        run.EnsureRanToCompletion();
        // ^ Note that the run completed. A rejected request is not a failed step.
        //   The application was asked a question and gave a perfectly clear answer.

        run.ApiStatus("rejected").Should().Be(HttpStatusCode.BadRequest);
        run.ApiBody("rejected").Should().Contain("Name is required");
    }
}
