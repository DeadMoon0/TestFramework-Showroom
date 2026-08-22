using Microsoft.Extensions.DependencyInjection;
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

//doc: Four sources of truth, one run, no opportunity to lie.
//doc:
//doc: The previous chapters each looked at one surface. This one looks at all of them at once, which is the
//doc: only arrangement in which an application cannot get away with anything:
//doc:
//doc: - the **response** says what it *claims* happened,
//doc: - the **database row** says what it actually *wrote*,
//doc: - the **stub log** says what it actually *sent*,
//doc: - the **unmatched list** says what it did that *nobody authorised*.
//doc:
//doc: An application can fake any one of those. Faking all four requires it to be correct, which we
//doc: understand is the outcome you were hoping for anyway.

//doc: Everything, at once. Read the four assertions at the bottom as one sentence: created, wrote 6, sent 6,
//doc: and nothing else. Then the fifth: the price came back from the stub, went through the application and
//doc: landed in the database. Three systems agreeing on one number is not a coincidence you should ever have
//doc: to take on faith.
//doc:
//doc: The finder here is the case chapter W2 was preparing you for. The *application* wrote this row, so the
//doc: test reads it and leaves it: `MarkReadonly()`. Without it teardown would delete the row, because that
//doc: is the default everywhere.

public class Container_TheWholeLane(ITestOutputHelper outputHelper)
{
    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(
            WebExt.Api.Http("orders")
                .Post("api/orders")
                .WithJsonBody(Var.Const(new { name = "Complete Order", quantity = 6 }))
                .Call())
            .Name("create")
        .WaitForEvent(WebExt.Stub.Called("pricing", HttpMethod.Post, "/api/quotes"))
            .WithTimeOut(TimeSpan.FromSeconds(30))
            .Name("quoted")
        .FindArtifact(
            "written",
            WebExt.ArtifactFinder.Sql.Where<ShowroomOrder>("orders-db", "Name = @name")
                .WithParameter("name", Var.Const("Complete Order")))
            .MarkReadonly()
        //   ^ The application wrote this row, so the test reads it and leaves it. Without
        //     MarkReadonly() teardown would delete it, which is the default everywhere.
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

//doc: Before anything is built or started, the environment writes down exactly what it intends to do: which
//doc: project, which framework, which images, and which of those it worked out rather than being told. This
//doc: chapter reads that plan back.
//doc:
//doc: It exists because the most expensive question in container-backed testing has always been "what did it
//doc: actually run", and the historical answer was to find out by experiment, at length, while a colleague
//doc: waited.
//doc:
//doc: Two things the plan proves here. The target framework was *derived* from the project, which targets
//doc: exactly one - had it targeted two, the run would have refused rather than picked one, because a project
//doc: quietly changing what your test runs is not a feature. (The escape hatch is
//doc: `WithTargetFramework(...)` on the source, and the exception says so.)
//doc:
//doc: And the settings the application was handed, verbatim. Note the addresses: `sqlserver,1433` and
//doc: `http://stub-pricing/` are what another *container* uses. The test process reaches the same database on
//doc: a completely different address, and both are correct. Getting that backwards is the single most popular
//doc: way to spend an afternoon, and the framework has removed the option.

public class Container_ThePlanIsOnTheRecord(ITestOutputHelper outputHelper)
{
    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(WebExt.Api.IsLive("orders"))
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

//doc: And the asymmetry that catches people out: a database container is worth keeping warm, and an
//doc: application container is not.
//doc:
//doc: Starting a database costs real seconds, so it is reused and the reset mode keeps it honest between
//doc: runs. An application container is *not* kept, and that is deliberate: if it were, an edit-and-rerun
//doc: cycle would quietly test the previous build, and you would spend the afternoon arguing with a fix that
//doc: was already correct. There is a reset mode for stale data. There has never been one for a stale binary.
//doc:
//doc: The assertion below reads zero because `OrdersSqlDefinition` in `WebShowroom.cs` chose
//doc: `SqlResetMode.RecreateDatabase`. Choose `SqlResetMode.None` instead and this assertion becomes a
//doc: lottery whose prize is an intermittent failure three weeks from now.

public class Container_WhatSurvivesBetweenRuns(ITestOutputHelper outputHelper)
{
    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(WebExt.Sql.Scalar<int>("orders-db", "SELECT COUNT(1) FROM [Orders]"))
            .Name("count")
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
            .SetEnv(WebShowroom.CreateDatabaseOnlyEnvironment())
            .RunAsync();

        run.EnsureRanToCompletion();

        run.SqlScalar<int>("count").Should().Be(0);
        // ^ Empty, because this database definition recreates itself every run.
        //   Choose SqlResetMode.None instead and this assertion becomes a lottery
        //   whose prize is an intermittent failure three weeks from now.
    }
}
