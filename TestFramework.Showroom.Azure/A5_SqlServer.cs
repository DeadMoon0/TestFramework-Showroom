using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Azure;
using TestFramework.Azure.Configuration;
using TestFramework.Azure.Configuration.SpecificConfigs;
using TestFramework.Azure.DB.SqlServer;
using TestFramework.Azure.Extensions;
using TestFramework.Config;
using TestFramework.Container.Azure;
using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Azure;

//doc: Schema, keys, and the kind of storage that remembers what you meant.
//doc:
//doc: SQL arrives with more ceremony than the previous chapters because relational storage asks for explicit
//doc: schema knowledge. That is not a flaw, that is the deal: you get joins, keys, and consequences.
//doc:
//doc: The framework meets SQL halfway. You provide the `DbContext`; the config registers it for artifact
//doc: handling; first use handles migrations or falls back to `EnsureCreated`. In return the test gets
//doc: tracked rows, query-based discovery and cleanup, without hand-written setup scripts wandering through
//doc: the suite like feral shell history.
//doc:
//doc: Note which SQL this is. The web lane's chapter W2 talks to SQL through `TestFramework.Web`, which
//doc: models rows from a model map and speaks in statements. This is `TestFramework.Azure`, which speaks
//doc: through EF Core and a `DbContext` you already own. Same database, two different bargains - pick the one
//doc: that matches where your schema knowledge already lives.
//doc:
//doc: All three chapters resolve to `components [azure-reset, mssql]`.

//doc: The `DbContext` is the schema contract. Ignore it and the runtime will eventually explain, in detail,
//doc: why that was a mistake, possibly with line numbers. Two entities, because the interesting difference
//doc: between the chapters below is how many columns a key has.

// ─── Step 0: Define your entity and DbContext ─────────────────────────────────
// The DbContext is the schema contract. Ignore it and eventually the runtime
// will explain, in detail, why that was a mistake and possibly include line numbers.

public class ShowroomProduct
{
    [Key]
    public string Sku { get; set; } = "";      // Single-column primary key. Straightforward, useful, almost comforting.
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string Category { get; set; } = "";
}

public class ShowroomInvoiceLine
{
    // Composite primary key: both values together identify the row. Order matters later, as it always does in bureaucracies.
    public string InvoiceId { get; set; } = "";
    public int    LineNumber { get; set; }
    public string Sku        { get; set; } = "";
    public int    Quantity   { get; set; }
}

//doc: Two things in `OnModelCreating` are worth copying rather than skimming. Table names are prefixed so
//doc: that several sample contexts can share one database without friendly fire and passive-aggressive
//doc: migration notes. And the composite key is declared explicitly, because EF Core is not a mind reader
//doc: and frankly has enough to do.

public class ShowroomDbContext(DbContextOptions<ShowroomDbContext> options) : DbContext(options)
{
    public DbSet<ShowroomProduct>     Products     { get; set; } = null!;
    public DbSet<ShowroomInvoiceLine> InvoiceLines { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Prefix table names so multiple sample contexts can share one database without friendly fire and passive-aggressive migration notes.
        modelBuilder.Entity<ShowroomProduct>().ToTable("ShowroomProducts");

        // Composite keys are declared explicitly because EF Core is not a mind reader and frankly has enough to do.
        modelBuilder.Entity<ShowroomInvoiceLine>()
            .ToTable("ShowroomInvoiceLines")
            .HasKey(l => new { l.InvoiceId, l.LineNumber });
    }
}

//doc: The setup helper is the whole SQL-specific wiring, in one place, so the chapters can talk about
//doc: behaviour instead of connection-string archaeology. Two moves, and each is a decision:
//doc:
//doc: - `AddSqlArtifactContexts` is what lets `AddSqlArtifact` work at all: it tells the framework which
//doc:   context to reach for when it has to insert, read or delete a row. You register *how to build* your
//doc:   context and are handed options already pointing at the database this run is using - back-to-front
//doc:   from ordinary EF registration, because the framework owns the address and you own the rest. There is
//doc:   no `AddDbContext` here and there cannot be: it takes its connection string when the registration is
//doc:   built, with no run in sight, so a containerized database could only be reached by writing the
//doc:   address back into somebody's configuration after the container started.
//doc: - `ApplyMigrationsOnFirstUse()` does what it says, and with no migrations in this sample it falls back
//doc:   to `EnsureCreated`. After that the process reuses the initialised schema like a respectable
//doc:   freeloader.
//doc:
//doc: Chapter A0's advanced path is this same helper, which is why it can resolve a typed config store at the
//doc: end and find `MainSql` waiting there.

// ─── Shared setup helper ──────────────────────────────────────────────────────
// One helper keeps the DI registration and EF setup in one place so the tests
// can talk about behavior instead of plumbing and connection-string archeology.

internal static class ShowroomSqlSetup
{
    internal static ConfigInstance BuildConfig() =>
        ConfigInstance.Create()
        .LoadDockerAzureConfig()
        .AddService(services =>
        {
            // No AddDbContext, and nothing resolving a connection string here. A context registered that way
            // takes its address when the registration is built - from a service provider, with no run in
            // sight - so a containerized database could only be reached by writing its address back into
            // somebody's configuration. Handing over the options instead removes that problem rather than
            // hiding it: what arrives already points at the database this run is using.
            services.AddSqlArtifactContexts(reg =>
            {
                reg.AddDefault<ShowroomDbContext>(opts => new ShowroomDbContext(opts));
                reg.ApplyMigrationsOnFirstUse();
                // ^ With no migrations in this sample, first use falls back to EnsureCreated.
                //   After that, the process reuses the initialized schema like a respectable freeloader.
            });
            })
            .Build();
}

//doc: One row, one key. `AddSqlArtifact` takes the entity and then its key values, and the artifact behaves
//doc: like every other: created on setup, read back for assertions, removed on teardown.
//doc:
//doc: `Row(row => row.Name)` is the SQL flavour of the select-then-assert shape - the same idea as
//doc: `Entity(...)` for a table row and `Item(...)` for a Cosmos document. Different storage, one habit.

// ─── Module A5.1: Single-column primary key ──────────────────────────────────

public class SqlServer_BasicUpsert(ITestOutputHelper outputHelper)
{
    private static readonly Timeline _timeline = Timeline.Create()
        .SetupArtifact("product")
        .Build();

    [DockerFact]
    [Trait("Category", "DockerSmoke")]
    public async Task Run()
    {
        var configSub = ShowroomSqlSetup.BuildConfig();

        // The provider is owned by this test, so it is named and disposed rather than
        // built inline and abandoned. BuildServiceProvider returns the concrete
        // ServiceProvider to make that ownership visible.
        await using ServiceProvider provider = configSub.BuildServiceProvider();

        var run = await _timeline
            .SetupRun(provider, outputHelper)
            .SetEnv(AzureShowroom.CreateEnvironment())
            .AddSqlArtifact(
                "product",     // artifact name
                "MainSql",     // shared Azure showroom SQL identifier
                new ShowroomProduct { Sku = "SHOW-001", Name = "Calibration Widget", Price = 9.99m, Category = "Tools" },
                Var.Const("SHOW-001"))   // Primary key values are provided in key order. SQL likes discipline.
            .RunAsync();

        run.EnsureRanToCompletion();

        run.SqlArtifact<ShowroomProduct>("product").Should().Exist();

        run.SqlArtifact<ShowroomProduct>("product")
            .Row(row => row.Name)
            .Should().Be("Calibration Widget");

        run.SqlArtifact<ShowroomProduct>("product")
            .Row(row => row.Price)
            .Should().Be(9.99m);
        // ^ Row inserted, values verified, no mystery left in the outcome. A rare and beautiful state.
    }
}

//doc: Same artifact mechanics, stricter about one thing: with a composite key, the values must be supplied
//doc: in the order the model declared them. Get that wrong and the database becomes educational.
//doc:
//doc: Note that the second value is written as a string, `Var.Const("1")`, for an `int` column. Key values
//doc: travel as strings and are converted through EF metadata with the patience of a saint - which is what
//doc: lets one artifact mechanism serve `string`, `int` and everything else without a generic parameter per
//doc: key part.

// ─── Module A5.2: Composite primary key ──────────────────────────────────────

public class SqlServer_CompositePrimaryKey(ITestOutputHelper outputHelper)
{
    private static readonly Timeline _timeline = Timeline.Create()
        .SetupArtifact("invoiceLine")
        .Build();

    [DockerFact]
    [Trait("Category", "DockerSmoke")]
    public async Task Run()
    {
        var configSub = ShowroomSqlSetup.BuildConfig();

        // The provider is owned by this test, so it is named and disposed rather than
        // built inline and abandoned. BuildServiceProvider returns the concrete
        // ServiceProvider to make that ownership visible.
        await using ServiceProvider provider = configSub.BuildServiceProvider();

        var run = await _timeline
            .SetupRun(provider, outputHelper)
            .SetEnv(AzureShowroom.CreateEnvironment())
            .AddSqlArtifact(
                "invoiceLine",
                "MainSql",
                new ShowroomInvoiceLine { InvoiceId = "INV-2026-001", LineNumber = 1, Sku = "SHOW-001", Quantity = 5 },
                Var.Const("INV-2026-001"),  // First PK column.
                Var.Const("1"))             // Second PK column, converted through EF metadata with the patience of a saint.
            .RunAsync();

        run.EnsureRanToCompletion();

        run.SqlArtifact<ShowroomInvoiceLine>("invoiceLine").Should().Exist();

        run.SqlArtifact<ShowroomInvoiceLine>("invoiceLine")
            .Row(row => row.Quantity)
            .Should().Be(5);
    }
}

//doc: And discovery, when the exact key is not the point. The finder takes a LINQ expression over the entity
//doc: - `q => q.Where(p => p.Category == "Instruments")` - which the framework evaluates through EF Core,
//doc: capturing each match as its own artifact: `toolsProducts_0`, `toolsProducts_1`, and so on. Predictable
//doc: names. Wild concept.
//doc:
//doc: The third seeded row is there to be excluded. A finder test that seeds only matching rows proves that
//doc: seeding works; seeding a non-match is what proves the *filter* works. And the snack is still cleaned up
//doc: afterwards: the query ignored it, teardown did not, because the test seeded it either way. Justice
//doc: comes for all rows.

// ─── Module A5.3: Query finder (LINQ over EF Core) ───────────────────────────

public class SqlServer_QueryFinder(ITestOutputHelper outputHelper)
{
    private static readonly Timeline _timeline = Timeline.Create()
        .SetupArtifact("prodTools1")
        .SetupArtifact("prodTools2")
        .SetupArtifact("prodOther")
        .FindArtifacts(
            "toolsProducts",  // Matching rows come back as toolsProducts_0, toolsProducts_1, and so on. Predictable names. Wild concept.
            AzureExt.ArtifactFinder.DB.SqlQuery<ShowroomProduct>(
                "MainSql",
                q => q.Where(p => p.Category == "Instruments")))
        // ^ Only the instrument rows come back as found artifacts. The snack remains judged and excluded.
        .Build();

    [DockerFact]
    [Trait("Category", "DockerSmoke")]
    public async Task Run()
    {
        var configSub = ShowroomSqlSetup.BuildConfig();

        // The provider is owned by this test, so it is named and disposed rather than
        // built inline and abandoned. BuildServiceProvider returns the concrete
        // ServiceProvider to make that ownership visible.
        await using ServiceProvider provider = configSub.BuildServiceProvider();

        var run = await _timeline
            .SetupRun(provider, outputHelper)
            .SetEnv(AzureShowroom.CreateEnvironment())
            .AddSqlArtifact("prodTools1", "MainSql",
                new ShowroomProduct { Sku = "INST-001", Name = "Precision Gauge",     Price = 149m, Category = "Instruments" },
                Var.Const("INST-001"))
            .AddSqlArtifact("prodTools2", "MainSql",
                new ShowroomProduct { Sku = "INST-002", Name = "Thermal Probe",       Price = 229m, Category = "Instruments" },
                Var.Const("INST-002"))
            .AddSqlArtifact("prodOther", "MainSql",
                new ShowroomProduct { Sku = "SNCK-001", Name = "Vending Machine Snack", Price = 1.25m, Category = "Refreshments" },
                //                                                                                      ^ Seeded for contrast. Query ignores it. Cleanup does not. Justice comes for all rows.
                Var.Const("SNCK-001"))
            .RunAsync();

        run.EnsureRanToCompletion();

        run.SqlArtifact<ShowroomProduct>("toolsProducts_0").Should().Exist();
        run.SqlArtifact<ShowroomProduct>("toolsProducts_1").Should().Exist();

        run.SqlArtifact<ShowroomProduct>("toolsProducts_0")
            .Row(row => row.Category)
            .Should().Be("Instruments");
        // ^ Matching category confirmed. Query semantics did their job and nobody had to count indexes manually.
    }
}
