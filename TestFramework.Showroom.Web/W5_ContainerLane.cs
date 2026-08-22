using System.Net;
using System.Net.Http;
using TestFramework.Container.Sources;
using TestFramework.Container.Web;
using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Assertions;
using TestFramework.Core.Variables;
using TestFramework.Web;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Web;

// ══════════════════════════════════════════════════════════════════════════════
//  WEB SYSTEMS DIVISION - PARTICIPANT ORIENTATION MODULE W5
//  "Four Sources Of Truth, One Run, No Opportunity To Lie"
//
//  The previous modules each looked at one surface. This one looks at all of them
//  at once, which is the only arrangement in which an application cannot get away
//  with anything:
//
//    the response       says what it CLAIMS happened
//    the database row   says what it actually WROTE
//    the stub log       says what it actually SENT
//    the unmatched list says what it did that NOBODY AUTHORISED
//
//  An application can fake any one of those. Faking all four requires it to be
//  correct, which we understand is the outcome you were hoping for anyway.
// ══════════════════════════════════════════════════════════════════════════════

// ─── Module W5.1: Everything, at once ────────────────────────────────────────

public class Container_TheWholeLane(ITestOutputHelper outputHelper)
{
    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(WebExt.Api.Http("orders")
            .Post("api/orders")
            .WithJsonBody(Var.Const(new { name = "Complete Order", quantity = 6 }))
            .Call()).Name("create")
        .WaitForEvent(WebExt.Stub.Called("pricing", HttpMethod.Post, "/api/quotes"))
            .WithTimeOut(TimeSpan.FromSeconds(30)).Name("quoted")
        .FindArtifact("written", WebExt.ArtifactFinder.Sql.Where<ShowroomOrder>("orders-db", "Name = @name")
            .WithParameter("name", Var.Const("Complete Order")))
            .MarkReadonly()
        //   ^ The application wrote this row, so the test reads it and leaves it. Without
        //     MarkReadonly() teardown would delete it, which is the default everywhere.
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

        run.ApiStatus("create").Should().Be(HttpStatusCode.Created);                        // claimed
        run.SqlRow<ShowroomOrder>("written").Select(order => order.Quantity).Should().Be(6); // wrote
        run.StubCall("quoted").Select(call => call.Body).Should().Contain("\"quantity\":6"); // sent
        run.StubUnmatchedCalls("audit").Should().HaveCount(0);                               // and nothing else

        // The price came back from the stub, went through the application, and landed
        // in the database. Three systems agreeing on one number is not a coincidence
        // you should ever have to take on faith.
        run.SqlRow<ShowroomOrder>("written").Select(order => order.Total).Should().Be(42.50m);
    }
}

// ─── Module W5.2: What was actually put in the container ─────────────────────

public class Container_ThePlanIsOnTheRecord(ITestOutputHelper outputHelper)
{
    // Before anything is built or started, the environment writes down exactly what
    // it intends to do: which project, which framework, which images, and which of
    // those it worked out rather than being told.
    //
    // This exists because the most expensive question in container-backed testing
    // has always been "what did it actually run", and the historical answer was to
    // find out by experiment, at length, while a colleague waited.

    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(WebExt.Api.IsLive("orders")).Name("live")
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

        ApiComponentState? apis = run.EnvironmentContext.GetState<ApiComponentState>(DockerWebEnvironment.ApiComponentId);
        Assert.NotNull(apis);
        RunningApi api = apis!.GetRequiredApi("orders");

        outputHelper.WriteLine(string.Join(Environment.NewLine, api.Plan.ToLogLines("orders")));

        Assert.Equal(ContainerSourceKind.Project, api.Plan.Kind);
        Assert.EndsWith("OrdersApi.csproj", api.Plan.ProjectPath!, StringComparison.Ordinal);
        Assert.Equal("net8.0", api.Plan.TargetFramework);
        // ^ Derived from the project, which targets exactly one framework. Had it
        //   targeted two, the run would have refused rather than picked one, because
        //   a project quietly changing what your test runs is not a feature.

        // And the settings the application was handed, verbatim. Note the address:
        // it is the one another CONTAINER uses. The test process reaches the same
        // database on a completely different address, and both are correct.
        outputHelper.WriteLine(api.SettingsJson);
        Assert.Contains("Data Source=sqlserver,1433", api.SettingsJson, StringComparison.Ordinal);
        Assert.Contains("http://stub-pricing/", api.SettingsJson, StringComparison.Ordinal);
    }
}

// ─── Module W5.3: The database persists, the application does not ────────────

public class Container_WhatSurvivesBetweenRuns(ITestOutputHelper outputHelper)
{
    // A database container is worth keeping warm; starting one costs real seconds
    // and reset modes exist to keep it honest between runs.
    //
    // An application container is NOT kept. If it were, an edit-and-rerun cycle
    // would quietly test the previous build, and you would spend the afternoon
    // arguing with a fix that was already correct. There is a reset mode for stale
    // data. There has never been one for a stale binary.

    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(WebExt.Sql.Scalar<int>("orders-db", "SELECT COUNT(1) FROM [Orders]")).Name("count")
        .Build();

    [DockerFact]
    [Trait("Category", "DockerSmoke")]
    public async Task Run()
    {
        TimelineRun run = await _timeline
            .SetupRun(WebShowroom.BuildConfig().BuildServiceProvider(), outputHelper)
            .SetEnv(WebShowroom.CreateDatabaseOnlyEnvironment())
            .RunAsync();

        run.EnsureRanToCompletion();

        run.SqlScalar<int>("count").Should().Be(0);
        // ^ Empty, because this database definition recreates itself every run.
        //   Choose SqlResetMode.None instead and this assertion becomes a lottery
        //   whose prize is an intermittent failure three weeks from now.
    }
}
