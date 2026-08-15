using System.Net;
using System.Net.Http;
using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Assertions;
using TestFramework.Core.Variables;
using TestFramework.Web;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Web;

// ══════════════════════════════════════════════════════════════════════════════
//  WEB SYSTEMS DIVISION - PARTICIPANT ORIENTATION MODULE W4
//  "What It Told You, And What It Told Everyone Else"
//
//  An application does two things. It answers you, and it goes and talks to other
//  systems. Test suites have historically been excellent at the first and entirely
//  blind to the second, which is why so many of them pass right up until the
//  invoice arrives.
//
//  A stub fixes that, and not primarily by returning a canned answer. It fixes it
//  by KEEPING A RECORD. Every request it received, with body and headers, available
//  for inspection. The canned answer is the cover story. The log is the evidence.
// ══════════════════════════════════════════════════════════════════════════════

// ─── Module W4.1: Prove the call actually happened ───────────────────────────

public class Stub_TheCallIsObservable(ITestOutputHelper outputHelper)
{
    // The application prices an order by asking the pricing service. We do not take
    // its word for that. We ask the pricing service.

    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(WebExt.Api.Http("orders")
            .Post("api/orders")
            .WithJsonBody(Var.Const(new { name = "Priced Order", quantity = 4 }))
            .Call()).Name("create")
        .WaitForEvent(WebExt.Stub.Called("pricing", HttpMethod.Post, "/api/quotes"))
            .WithTimeOut(TimeSpan.FromSeconds(30)).Name("quoted")
        //   ^ Waiting is done by polling the stub's own log, never by the stub
        //     calling back into this process. A container cannot reach into the
        //     machine that started it, and any design that needs it to will work
        //     beautifully on one laptop and nowhere else.
        .Trigger(WebExt.Stub.Calls("pricing")).Name("everything")
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

        // What the application said:
        run.ApiStatus("create").Should().Be(HttpStatusCode.Created);
        run.ApiBody("create").Should().Contain("quoted");

        // What the application did, which is a different question with a different answer:
        run.StubCall("quoted").Select(call => call.Body).Should().Contain("\"quantity\":4");
        run.StubCalls("everything").Should().HaveCount(1);
        // ^ Exactly one quote request. Not two. A retry loop that fires twice is
        //   invisible from the response and expensive from the invoice.
    }
}

// ─── Module W4.2: The assertion nobody thinks to write ───────────────────────

public class Stub_UnmatchedCallsAreTheInterestingOnes(ITestOutputHelper outputHelper)
{
    // Every request the stub could not match is recorded and answered with 404.
    // Those are the calls to endpoints the test never declared: a new dependency
    // somebody added, a path that was renamed, a health check nobody mentioned.
    //
    // Nothing else in a test suite will ever tell you about them. Assert on this
    // one. It costs a line and it is the only line that notices when the
    // application quietly grows a new friend.

    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(WebExt.Stub.Reset("pricing")).Name("clean")
        //   ^ Clear the log first, so what follows is about this run and not about
        //     the accumulated history of everything that came before it.
        .Trigger(WebExt.Api.Http("orders")
            .Post("api/orders")
            .WithJsonBody(Var.Const(new { name = "Well Behaved Order", quantity = 1 }))
            .Call()).Name("create")
        .Trigger(WebExt.Stub.Calls("pricing")).Name("audit")
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

        run.ApiStatus("create").Should().Be(HttpStatusCode.Created);
        run.StubUnmatchedCalls("audit").Should().HaveCount(0);
        // ^ The application asked for nothing we had not declared. Today.
    }
}

// ─── Module W4.3: A declaration is data, and that is on purpose ──────────────

public class Stub_DeclarationsAreData(ITestOutputHelper outputHelper)
{
    // Look at PricingStubDefinition in WebShowroom.cs. There is no delegate in it.
    // No lambda computes the response, because the server running that declaration
    // may be in a different container, on a different machine, in a different
    // building, and it cannot call your code. It was never going to be able to.
    //
    // What you get instead is matching on method, path, headers and body, plus
    // templating over the request when the answer has to quote the question. That
    // covers the honest cases. If a stub needs real logic, the thing being tested
    // has usually been stubbed at the wrong altitude, and no amount of cleverness
    // in the stub is going to fix a decision made three layers up.

    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(WebExt.Stub.Calls("pricing", HttpMethod.Get, "/api/health")).Name("health-calls")
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

        run.StubCalls("health-calls").Should().HaveCount(0);
        // ^ The health mapping was declared and never used. That is a perfectly
        //   respectable outcome: a stub is a statement about what is ALLOWED to be
        //   called, not a prediction of what will be.
    }
}
