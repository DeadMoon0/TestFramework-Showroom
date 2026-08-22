using Microsoft.Extensions.DependencyInjection;
using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Assertions;
using TestFramework.Core.Variables;
using TestFramework.Web;
using TestFramework.Web.Sql.Artifacts;
using TestFramework.Web.Sql.Steps.IsLive;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Web;

//doc: Rows have identity. Statements have consequences. Totals have neither.
//doc:
//doc: The framework sorts everything a database can do into three boxes and refuses to let anything sit in
//doc: two of them:
//doc:
//doc: - **A row is an artifact.** It has a key and a lifetime. It is seeded before the run and removed
//doc:   after, without anybody writing a teardown script that eventually deletes production.
//doc: - **A statement is a step.** It causes something, so it belongs in the ordering.
//doc: - **A total is an observation.** It has no identity, no lifetime, and no business pretending to be
//doc:   either.
//doc:
//doc: There are three ways to put a row in front of a test, and they differ in exactly one respect: who is
//doc: responsible for it afterwards.
//doc:
//doc: - `SetupArtifact` + `AddArtifact` - the test creates it.
//doc: - `RegisterArtifact` - something else created it, the test adopts it.
//doc: - `FindArtifact` - located by predicate.
//doc:
//doc: And here is the part worth memorising: **none of those three decides teardown.** Deleting is the
//doc: default for all of them. The one way out is `MarkReadonly()` on the declaring step, which the
//doc: compiler offers on `RegisterArtifact` and the `Find…` verbs and does not offer on `SetupArtifact` at
//doc: all - an artifact the test created is not something the test gets to disown.
//doc:
//doc: That default is deliberate, and it is the safe one rather than the polite one: a test that leaves its
//doc: own rows behind poisons the next run. Say `MarkReadonly()` when you only meant to look, because
//doc: deleting data you merely looked at is not tidying up, it is an incident with a ticket number.

//doc: A row is an artifact: seeded on setup, verified in the run, removed on teardown. The removal is not
//doc: optional and does not require your attention, which is the correct amount of attention for a thing
//doc: that has never once been interesting.
//doc:
//doc: Three different surfaces are asked three different questions here - the liveness probe, the seeded
//doc: row, and a scalar count - and the last one is the point of the "totals are observations" rule: the
//doc: count is read through a step, not tracked as a thing.

public class Sql_SeededRowIsAnArtifact(ITestOutputHelper outputHelper)
{
    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(WebExt.Sql.IsLive("orders-db", SqlAlivenessLevel.Database))
            .Name("live")
        .SetupArtifact("seeded")
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
            .AddArtifact(
                "seeded",
                WebExt.Artifact.Sql.Row<ShowroomOrder>("orders-db", Var.Const("1")),
                new SqlRowArtifactData<ShowroomOrder>(new ShowroomOrder { Id = 1, Name = "Seeded Order", Quantity = 7, Total = 12.34m }))
            //   ^ Key values are supplied in the order the model declared them.
            //     Supply them in a different order and the database will explain the
            //     mistake at length, in its own time, and in its own dialect.
            .RunAsync();

        run.EnsureRanToCompletion();

        run.SqlProbe("live").Select(probe => probe.Success).Should().Be(true);
        run.SqlRow<ShowroomOrder>("seeded").Select(order => order.Name).Should().Be("Seeded Order");
        run.SqlRow<ShowroomOrder>("seeded").Select(order => order.Total).Should().Be(12.34m);
        run.SqlScalar<int>("count").Should().Be(1);
        // ^ The decimal survived the round trip intact. It was declared with a
        //   precision, so nothing quietly rounded it on the way past.
    }
}

//doc: `RegisterArtifact` is for the row the *application* wrote. You know its key, you want to read it and
//doc: assert on it, and you want it gone afterwards.
//doc:
//doc: Read that last part twice, because it is the whole difference from a finder: registering a row makes
//doc: the test its owner, and an owner cleans up. This is usually what you want for a row your test caused
//doc: to exist. It is emphatically not what you want for a row that was already there when you arrived.
//doc:
//doc: The `INSERT` here stands in for the application. In a real suite it would be an HTTP call, and the row
//doc: would appear without the test ever touching SQL - which is chapter W5.

public class Sql_RegisterArtifactAdoptsAnExistingRow(ITestOutputHelper outputHelper)
{
    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(
            WebExt.Sql.Execute("orders-db", "INSERT INTO [Orders] ([Name], [Quantity], [Total]) VALUES (@name, @quantity, @total)")
                .WithParameter("name", Var.Const("Adopted Order"))
                .WithParameter("quantity", Var.Const(3))
                .WithParameter("total", Var.Const(30m)))
            .Name("insert")
        //   ^ Stands in for the application writing the row. In a real suite this is
        //     an HTTP call, and the row appears without the test ever touching SQL.
        .RegisterArtifact("adopted", WebExt.Artifact.Sql.Row<ShowroomOrder>("orders-db", Var.Const("1")))
        //   ^ Adopted by key. It is resolved, it is assertable, and it will be
        //     removed at teardown exactly like a row the test had seeded itself.
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

        run.SqlRow<ShowroomOrder>("adopted").Select(order => order.Name).Should().Be("Adopted Order");
        run.SqlRow<ShowroomOrder>("adopted").Select(order => order.Total).Should().Be(30m);
    }
}

//doc: A finder locates rows by predicate rather than by key. Those rows belong to whoever wrote them, so
//doc: this is where `MarkReadonly()` earns its keep - and it is the one chapter in the Showroom that exists
//doc: mainly to show the opt-out.
//doc:
//doc: Worth knowing precisely, because the run log says so out loud: teardown walks every artifact, reaches
//doc: this one, records that it is marked readonly and being left in place, and moves on. That line is
//doc: informational. It is not a warning, it is not a failure, and it does not mean cleanup was skipped by
//doc: accident. It means you told the framework not to delete something that was never yours.
//doc:
//doc: Drop the `MarkReadonly()` and teardown deletes these rows instead - which is exactly why it is not
//doc: the default. The two seeded rows in the run below have no such protection, and none is wanted: the
//doc: test created them, so the test cleans them up, including the one the predicate never matched.

public class Sql_FinderObservesWithoutOwning(ITestOutputHelper outputHelper)
{
    private static readonly Timeline _timeline = Timeline.Create()
        .SetupArtifact("first")
        .SetupArtifact("second")
        .FindArtifact(
            "bulk",
            WebExt.ArtifactFinder.Sql.Where<ShowroomOrder>("orders-db", "Quantity >= @minimum")
                .WithParameter("minimum", Var.Const(10)))
            .MarkReadonly()
        //   ^ Parameters are variable-backed. Nothing is concatenated into the
        //     statement, which closes an entire genre of afternoon.
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
            .AddArtifact("first",
                WebExt.Artifact.Sql.Row<ShowroomOrder>("orders-db", Var.Const("1")),
                new SqlRowArtifactData<ShowroomOrder>(new ShowroomOrder { Id = 1, Name = "Bulk Order", Quantity = 25, Total = 250m }))
            .AddArtifact("second",
                WebExt.Artifact.Sql.Row<ShowroomOrder>("orders-db", Var.Const("2")),
                new SqlRowArtifactData<ShowroomOrder>(new ShowroomOrder { Id = 2, Name = "Small Order", Quantity = 2, Total = 20m }))
            //   ^ Seeded for contrast. The predicate excludes it; teardown does not,
            //     because the test seeded it and therefore owns it.
            .RunAsync();

        run.EnsureRanToCompletion();

        run.SqlRow<ShowroomOrder>("bulk").Select(order => order.Name).Should().Be("Bulk Order");
        run.SqlRow<ShowroomOrder>("bulk").Select(order => order.Quantity).Should().Be(25);
    }
}

//doc: Last, the difference between acting and observing, in one timeline. `Execute` changes rows and reports
//doc: how many. `Scalar` reads a single value and changes nothing. They occupy different phases of the run
//doc: for exactly that reason, and the planner keeps them in order so the story stays readable afterwards.
//doc:
//doc: The read-back is the unglamorous part and also the only part that has ever caught a transaction that
//doc: was never committed: the update reports one row changed, and a separate connection confirms it.

public class Sql_StatementsAndObservations(ITestOutputHelper outputHelper)
{
    private static readonly Timeline _timeline = Timeline.Create()
        .SetupArtifact("target")
        .Trigger(
            WebExt.Sql.Execute("orders-db", "UPDATE [Orders] SET [Quantity] = @quantity WHERE [Id] = @id")
                .WithParameter("quantity", Var.Const(99))
                .WithParameter("id", Var.Const(1)))
            .Name("update")
        .Trigger(
            WebExt.Sql.Scalar<int>("orders-db", "SELECT [Quantity] FROM [Orders] WHERE [Id] = @id")
                .WithParameter("id", Var.Const(1)))
            .Name("read-back")
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
            .AddArtifact("target",
                WebExt.Artifact.Sql.Row<ShowroomOrder>("orders-db", Var.Const("1")),
                new SqlRowArtifactData<ShowroomOrder>(new ShowroomOrder { Id = 1, Name = "Amendable Order", Quantity = 1, Total = 10m }))
            .RunAsync();

        run.EnsureRanToCompletion();

        run.SqlAffectedRows("update").Should().Be(1);
        run.SqlScalar<int>("read-back").Should().Be(99);
        // ^ One row changed, and the change is visible from a separate connection.
        //   Confirming that in a test is unglamorous work and also the only work
        //   that has ever caught a transaction that was never committed.
    }
}
