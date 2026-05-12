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

// ══════════════════════════════════════════════════════════════════════════════
//  CLOUD INFRASTRUCTURE DIVISION - PARTICIPANT ORIENTATION MODULE A5
//  "Schema, Keys, And The Kind Of Storage That Remembers What You Meant"
//
//  SQL enters the showroom with more ceremony than the previous modules because
//  relational storage asks for explicit schema knowledge. That is not a flaw.
//  That is the deal. You get joins, keys, and consequences.
//
//  The framework meets SQL halfway:
//    1. You provide the DbContext.
//    2. The config registers it for artifact handling.
//    3. First use handles migrations or EnsureCreated automatically.
//
//  In return, the test gets tracked rows, query-based discovery, and cleanup
//  without hand-written setup scripts wandering through the suite like feral shell history.
// ══════════════════════════════════════════════════════════════════════════════

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

// ─── Shared setup helper ──────────────────────────────────────────────────────
// One helper keeps the DI registration and EF setup in one place so the tests
// can talk about behavior instead of plumbing and connection-string archeology.

internal static class ShowroomSqlSetup
{
    internal static ConfigInstance BuildConfig() =>
        ConfigInstance.Create()
        .LoadDockerAzureConfig()
        .AddService((services, _) =>
        {
            services.AddDbContext<ShowroomDbContext>((serviceProvider, opts) =>
                opts.UseSqlServer(serviceProvider.GetRequiredService<ConfigStore<SqlDatabaseConfig>>().GetConfig("MainSql").ConnectionString));

            services.AddSqlArtifactContexts(reg =>
            {
                reg.AddDefault<ShowroomDbContext>();
                reg.ApplyMigrationsOnFirstUse();
                // ^ With no migrations in this sample, first use falls back to EnsureCreated.
                //   After that, the process reuses the initialized schema like a respectable freeloader.
            });
            })
            .Build();
}

// ─── Module A5.1: Single-column primary key ──────────────────────────────────

[Collection("AzureShowroom")]
public class SqlServer_BasicUpsert(ITestOutputHelper outputHelper)
{
    // First example: insert one product row, verify it, and let cleanup close
    // the file on that tiny piece of evidence before it starts a family.

    private static readonly Timeline _timeline = Timeline.Create()
        .SetupArtifact("product")
        .Build();

    [Fact]
    public async Task Run()
    {
        var configSub = ShowroomSqlSetup.BuildConfig();

        var run = await _timeline
            .SetupRun(configSub.BuildServiceProvider(), outputHelper)
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
            .Select(d => d.Row.Name)
            .Should().Be("Calibration Widget");

        run.SqlArtifact<ShowroomProduct>("product")
            .Select(d => d.Row.Price)
            .Should().Be(9.99m);
        // ^ Row inserted, values verified, no mystery left in the outcome. A rare and beautiful state.
    }
}

// ─── Module A5.2: Composite primary key ──────────────────────────────────────

[Collection("AzureShowroom")]
public class SqlServer_CompositePrimaryKey(ITestOutputHelper outputHelper)
{
    // Second example: composite keys. Same artifact mechanics, stricter key order.
    // The values must be supplied in the same order the model declared them or the database will become educational.

    private static readonly Timeline _timeline = Timeline.Create()
        .SetupArtifact("invoiceLine")
        .Build();

    [Fact]
    public async Task Run()
    {
        var configSub = ShowroomSqlSetup.BuildConfig();

        var run = await _timeline
            .SetupRun(configSub.BuildServiceProvider(), outputHelper)
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
            .Select(d => d.Row.Quantity)
            .Should().Be(5);
    }
}

// ─── Module A5.3: Query finder (LINQ over EF Core) ───────────────────────────

[Collection("AzureShowroom")]
public class SqlServer_QueryFinder(ITestOutputHelper outputHelper)
{
    // Third example: query for rows when the exact key is not the point. The
    // framework evaluates the LINQ query, captures the matches as artifacts, and
    // still cleans up the full seeded set afterward because somebody around here remembers standards.

    private static readonly Timeline _timeline = Timeline.Create()
        .SetupArtifact("prodTools1")
        .SetupArtifact("prodTools2")
        .SetupArtifact("prodOther")
        .FindArtifacts(
            "toolsProducts",  // Matching rows come back as toolsProducts_0, toolsProducts_1, and so on. Predictable names. Wild concept.
            AzureTF.ArtifactFinder.DB.SqlQuery<ShowroomProduct>(
                "MainSql",
                q => q.Where(p => p.Category == "Instruments")))
        // ^ Only the instrument rows come back as found artifacts. The snack remains judged and excluded.
        .Build();

    [Fact]
    public async Task Run()
    {
        var configSub = ShowroomSqlSetup.BuildConfig();

        var run = await _timeline
            .SetupRun(configSub.BuildServiceProvider(), outputHelper)
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
            .Select(d => d.Row.Category)
            .Should().Be("Instruments");
        // ^ Matching category confirmed. Query semantics did their job and nobody had to count indexes manually.
    }
}
