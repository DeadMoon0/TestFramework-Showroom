using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Assertions;
using TestFramework.Core.Variables;
using TestFramework.Web;
using TestFramework.Web.Sql.Artifacts;
using TestFramework.Web.Sql.Steps.IsLive;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Web;

// ══════════════════════════════════════════════════════════════════════════════
//  WEB SYSTEMS DIVISION - PARTICIPANT ORIENTATION MODULE W2
//  "Rows Have Identity, Statements Have Consequences, Totals Have Neither"
//
//  The framework sorts everything a database can do into three boxes, and refuses
//  to let anything sit in two of them:
//
//    A ROW is an artifact.        It has a key and a lifetime. It is seeded before
//                                the run and removed after, without anybody writing
//                                a teardown script that eventually deletes production.
//    A STATEMENT is a step.       It causes something. It belongs in the ordering.
//    A TOTAL is an observation.   It has no identity, no lifetime, and no business
//                                pretending to be either.
//
//  There are three ways to put a row in front of a test, and they differ in exactly
//  one respect: who is responsible for it afterwards.
//
//    SetupArtifact + AddArtifact   the test creates it, and OWNS it
//    RegisterArtifact              something else created it, the test ADOPTS it
//    FindArtifact                  located by predicate, and only OBSERVED
//
//  Ownership decides teardown, and teardown is where a careless test does its real
//  damage. An owned row is removed. An observed row is left exactly where it was
//  found, because deleting data you merely looked at is not tidying up, it is an
//  incident with a ticket number.
// ══════════════════════════════════════════════════════════════════════════════

// ─── Module W2.1: A row is an artifact ───────────────────────────────────────

public class Sql_SeededRowIsAnArtifact(ITestOutputHelper outputHelper)
{
    // Seeded on setup, verified in the run, removed on teardown. The removal is not
    // optional and does not require your attention, which is the correct amount of
    // attention for a thing that has never once been interesting.

    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(WebExt.Sql.IsLive("orders-db", SqlAlivenessLevel.Database)).Name("live")
        .SetupArtifact("seeded")
        .Trigger(WebExt.Sql.Scalar<int>("orders-db", "SELECT COUNT(1) FROM [Orders]")).Name("count")
        .Build();

    [Fact]
    [Trait("Category", "DockerSmoke")]
    public async Task Run()
    {
        TimelineRun run = await _timeline
            .SetupRun(WebShowroom.BuildConfig().BuildServiceProvider(), outputHelper)
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

// ─── Module W2.2: Adopting a row somebody else created ───────────────────────

public class Sql_RegisterArtifactAdoptsAnExistingRow(ITestOutputHelper outputHelper)
{
    // RegisterArtifact is for the row the APPLICATION wrote. You know its key, you
    // want to read it and assert on it, and you want it gone afterwards.
    //
    // Read that last part twice, because it is the whole difference from a finder:
    // registering a row with an ordinary reference makes the test its owner, and an
    // owner cleans up. This is usually what you want for a row your test caused to
    // exist. It is emphatically not what you want for a row that was already there
    // when you arrived.

    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(WebExt.Sql.Execute("orders-db", "INSERT INTO [Orders] ([Name], [Quantity], [Total]) VALUES (@name, @quantity, @total)")
            .WithParameter("name", Var.Const("Adopted Order"))
            .WithParameter("quantity", Var.Const(3))
            .WithParameter("total", Var.Const(30m))).Name("insert")
        //   ^ Stands in for the application writing the row. In a real suite this is
        //     an HTTP call, and the row appears without the test ever touching SQL.
        .RegisterArtifact("adopted", WebExt.Artifact.Sql.Row<ShowroomOrder>("orders-db", Var.Const("1")))
        //   ^ Adopted by key. It is resolved, it is assertable, and it will be
        //     removed at teardown exactly like a row the test had seeded itself.
        .Build();

    [Fact]
    [Trait("Category", "DockerSmoke")]
    public async Task Run()
    {
        TimelineRun run = await _timeline
            .SetupRun(WebShowroom.BuildConfig().BuildServiceProvider(), outputHelper)
            .SetEnv(WebShowroom.CreateDatabaseOnlyEnvironment())
            .RunAsync();

        run.EnsureRanToCompletion();

        run.SqlRow<ShowroomOrder>("adopted").Select(order => order.Name).Should().Be("Adopted Order");
        run.SqlRow<ShowroomOrder>("adopted").Select(order => order.Total).Should().Be(30m);
    }
}

// ─── Module W2.3: Finding rows nobody told you the key of ────────────────────

public class Sql_FinderObservesWithoutOwning(ITestOutputHelper outputHelper)
{
    // A finder locates rows by predicate. What it finds is OBSERVED, not OWNED, and
    // teardown leaves it alone.
    //
    // Worth knowing precisely, because the run log says so out loud: teardown walks
    // every artifact, notices that this one carries no way to remove itself, records
    // that it is being left in place, and moves on. That line is informational. It
    // is not a warning, it is not a failure, and it does not mean cleanup was
    // skipped by accident. It means the framework declined to delete something that
    // was never yours.

    private static readonly Timeline _timeline = Timeline.Create()
        .SetupArtifact("first")
        .SetupArtifact("second")
        .FindArtifact("bulk", WebExt.ArtifactFinder.Sql.Where<ShowroomOrder>("orders-db", "Quantity >= @minimum")
            .WithParameter("minimum", Var.Const(10)))
        //   ^ Parameters are variable-backed. Nothing is concatenated into the
        //     statement, which closes an entire genre of afternoon.
        .Build();

    [Fact]
    [Trait("Category", "DockerSmoke")]
    public async Task Run()
    {
        TimelineRun run = await _timeline
            .SetupRun(WebShowroom.BuildConfig().BuildServiceProvider(), outputHelper)
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

// ─── Module W2.4: Statements act, scalars observe ────────────────────────────

public class Sql_StatementsAndObservations(ITestOutputHelper outputHelper)
{
    // Execute changes rows and reports how many. Scalar reads a single value and
    // changes nothing. They occupy different phases of the run for that reason, and
    // the planner keeps them in order so the story stays readable afterwards.

    private static readonly Timeline _timeline = Timeline.Create()
        .SetupArtifact("target")
        .Trigger(WebExt.Sql.Execute("orders-db", "UPDATE [Orders] SET [Quantity] = @quantity WHERE [Id] = @id")
            .WithParameter("quantity", Var.Const(99))
            .WithParameter("id", Var.Const(1))).Name("update")
        .Trigger(WebExt.Sql.Scalar<int>("orders-db", "SELECT [Quantity] FROM [Orders] WHERE [Id] = @id")
            .WithParameter("id", Var.Const(1))).Name("read-back")
        .Build();

    [Fact]
    [Trait("Category", "DockerSmoke")]
    public async Task Run()
    {
        TimelineRun run = await _timeline
            .SetupRun(WebShowroom.BuildConfig().BuildServiceProvider(), outputHelper)
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
