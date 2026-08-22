using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http;
using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Assertions;
using TestFramework.Core.Variables;
using TestFramework.Web;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Web;

//doc: What it told you, and what it told everyone else.
//doc:
//doc: An application does two things. It answers you, and it goes and talks to other systems. Test suites
//doc: have historically been excellent at the first and entirely blind to the second, which is why so many
//doc: of them pass right up until the invoice arrives.
//doc:
//doc: A stub fixes that, and not primarily by returning a canned answer. It fixes it by **keeping a
//doc: record**. Every request it received, with body and headers, available for inspection. The canned
//doc: answer is the cover story; the log is the evidence.
//doc:
//doc: Three chapters: prove the call happened, prove nothing *else* happened, and understand why a stub
//doc: declaration is data rather than code.

//doc: The application prices an order by asking the pricing service. We do not take its word for that. We
//doc: ask the pricing service.
//doc:
//doc: Two step kinds do the work. `Stub.Called(...)` is an event: it waits until a matching request shows up
//doc: in the stub's log. `Stub.Calls(...)` is a read: it takes everything the stub recorded, so the test can
//doc: count.
//doc:
//doc: Note how the waiting works, because it constrains the design. The framework polls the stub's own log;
//doc: the stub never calls back into this process. A container cannot reach into the machine that started
//doc: it, and any design that needs it to will work beautifully on one laptop and nowhere else.

public class Stub_TheCallIsObservable(ITestOutputHelper outputHelper)
{
    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(
            WebExt.Api.Http("orders")
                .Post("api/orders")
                .WithJsonBody(Var.Const(new { name = "Priced Order", quantity = 4 }))
                .Call())
            .Name("create")
        .WaitForEvent(WebExt.Stub.Called("pricing", HttpMethod.Post, "/api/quotes"))
            .WithTimeOut(TimeSpan.FromSeconds(30))
            .Name("quoted")
        //   ^ Waiting is done by polling the stub's own log, never by the stub
        //     calling back into this process. A container cannot reach into the
        //     machine that started it, and any design that needs it to will work
        //     beautifully on one laptop and nowhere else.
        .Trigger(WebExt.Stub.Calls("pricing"))
            .Name("everything")
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

//doc: And now the assertion nobody thinks to write. Every request the stub could not match is recorded and
//doc: answered with 404, and those are the interesting ones: a new dependency somebody added, a path that
//doc: was renamed, a health check nobody mentioned.
//doc:
//doc: Nothing else in a test suite will ever tell you about them. `StubUnmatchedCalls(...).Should()
//doc: .HaveCount(0)` costs one line and is the only line that notices when the application quietly grows a
//doc: new friend.
//doc:
//doc: `Stub.Reset(...)` comes first, for a reason that generalises: the log accumulates, and nothing empties
//doc: it on your behalf. A count is only meaningful if you know where the counting started. This chapter is
//doc: about this run, not about the accumulated history of everything that came before it.

public class Stub_UnmatchedCallsAreTheInterestingOnes(ITestOutputHelper outputHelper)
{
    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(WebExt.Stub.Reset("pricing"))
            .Name("clean")
        //   ^ Clear the log first, so what follows is about this run and not about
        //     the accumulated history of everything that came before it.
        .Trigger(
            WebExt.Api.Http("orders")
                .Post("api/orders")
                .WithJsonBody(Var.Const(new { name = "Well Behaved Order", quantity = 1 }))
                .Call())
            .Name("create")
        .Trigger(WebExt.Stub.Calls("pricing"))
            .Name("audit")
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

        run.ApiStatus("create").Should().Be(HttpStatusCode.Created);
        run.StubUnmatchedCalls("audit").Should().HaveCount(0);
        // ^ The application asked for nothing we had not declared. Today.
    }
}

//doc: Last, a design constraint worth understanding rather than working around. Look at
//doc: `PricingStubDefinition` in `WebShowroom.cs`: there is no delegate in it. No lambda computes the
//doc: response, because the server running that declaration may be in a different container, on a different
//doc: machine, in a different building, and it cannot call your code. It was never going to be able to.
//doc:
//doc: What you get instead is matching on method, path, headers and body, plus templating over the request
//doc: when the answer has to quote the question. That covers the honest cases. If a stub needs real logic,
//doc: the thing being tested has usually been stubbed at the wrong altitude, and no amount of cleverness in
//doc: the stub is going to fix a decision made three layers up.
//doc:
//doc: The assertion here is a zero, and a perfectly respectable one: the health mapping was declared and
//doc: never called. A stub is a statement about what is *allowed* to be called, not a prediction of what
//doc: will be.

public class Stub_DeclarationsAreData(ITestOutputHelper outputHelper)
{
    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(WebExt.Stub.Calls("pricing", HttpMethod.Get, "/api/health"))
            .Name("health-calls")
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

        run.StubCalls("health-calls").Should().HaveCount(0);
        // ^ The health mapping was declared and never used. That is a perfectly
        //   respectable outcome: a stub is a statement about what is ALLOWED to be
        //   called, not a prediction of what will be.
    }
}
