using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Azure;
using TestFramework.Azure.Configuration;
using TestFramework.Azure.Configuration.SpecificConfigs;
using TestFramework.Azure.DB.SqlServer;
using TestFramework.Azure.Extensions;
using TestFramework.Config;
using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Azure;

// ══════════════════════════════════════════════════════════════════════════════
//  CLOUD INFRASTRUCTURE DIVISION — PARTICIPANT ORIENTATION MODULE A5
//  "SQL Server: Relational Database Technology. The Classics. The Absolute Classics."
//
//  You want a schema. You want rows. You want transactions, foreign keys, and a
//  PRIMARY KEY that means something. Admirable. Ambitious. Welcome to SQL.
//
//  Unlike our other modules, SQL requires a little more setup:
//    1. An EF Core DbContext that tells the framework about your tables and keys.
//    2. A call to .AddSqlArtifactContexts() so the framework knows which DbContext to use.
//    3. The shared Azure showroom container config provides SqlDatabase:MainSql.
//
//  The framework will handle migrations on first use if you call ApplyMigrationsOnFirstUse().
//  If your context has no EF Core migrations (like the one in this file),
//  it calls EnsureCreated() instead. The tables will appear. This is fine.
//  Everything is fine.
// ══════════════════════════════════════════════════════════════════════════════

// ─── Step 0: Define your entity and DbContext ─────────────────────────────────
// The DbContext is the blueprint. EF Core reads it to understand your schema.
// Treat it like the instruction manual nobody reads but everyone eventually needs.

public class ShowroomProduct
{
    [Key]
    public string Sku { get; set; } = "";      // Primary key. One column. Simple. Powerful.
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string Category { get; set; } = "";
}

public class ShowroomInvoiceLine
{
    // Composite PK: InvoiceId + LineNumber together make a unique row.
    // Two columns, working as one. A partnership. A synergy.
    // Possibly a team-building exercise.
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
        // Use prefixed table names to avoid collisions with other DbContexts on the same database.
        modelBuilder.Entity<ShowroomProduct>().ToTable("ShowroomProducts");

        // Composite key must be declared explicitly. EF Core won't guess it.
        // We tried guessing once. It guessed wrong. We don't guess anymore.
        modelBuilder.Entity<ShowroomInvoiceLine>()
            .ToTable("ShowroomInvoiceLines")
            .HasKey(l => new { l.InvoiceId, l.LineNumber });
    }
}

// ─── A shared helper to register the DbContext in DI ─────────────────────────
// Every test class calls this instead of repeating the setup.
// We are against repeating. Repetition is the enemy of clarity.
// Also it violates our internal Style Guide §4c.

internal static class ShowroomSqlSetup
{
    internal static ConfigInstance BuildConfig() =>
        AzureShowroom.BuildConfig((services, _) =>
        {
            services.AddDbContext<ShowroomDbContext>((serviceProvider, opts) =>
                opts.UseSqlServer(serviceProvider.GetRequiredService<ConfigStore<SqlDatabaseConfig>>().GetConfig("MainSql").ConnectionString));

            services.AddSqlArtifactContexts(reg =>
            {
                reg.AddDefault<ShowroomDbContext>();
                reg.ApplyMigrationsOnFirstUse();
                // ^ First test class to run will call EnsureCreated (no migrations defined here).
                //   Subsequent test classes share the result via process-wide state.
                //   You may feel the urge to call this "magic." We prefer "engineering."
            });
        });
}

// ─── Module A5.1: Single-column primary key ──────────────────────────────────

[Collection("AzureShowroom")]
public class SqlServer_BasicUpsert(ITestOutputHelper outputHelper)
{
    // Insert a product. Verify it. Let the framework delete it.
    // This is the lifecycle of test data in this framework:
    // a brief flicker of existence, confirmed, then gracefully removed.
    // Like a spark in a controlled environment.
    // Very controlled. We have protocols.

    private static readonly Timeline _timeline = Timeline.Create()
        .SetupArtifact("product")
        .Build();

    [Fact]
    public async Task Run()
    {
        var configSub = ShowroomSqlSetup.BuildConfig();

        var run = await _timeline
            .SetupRun(configSub.BuildServiceProvider(), outputHelper)
            .SetEnv(TestFramework.Container.Azure.DockerAzureEnvironment.For<AzureShowroom.DefaultFunctionAppDefinition>())
            .AddSqlArtifact(
                "product",     // artifact name
                "MainSql",     // shared Azure showroom SQL identifier
                new ShowroomProduct { Sku = "SHOW-001", Name = "Calibration Widget", Price = 9.99m, Category = "Tools" },
                Var.Const("SHOW-001"))   // primary key value(s) — one per PK column, in key order
            .RunAsync();

        run.EnsureRanToCompletion();

        run.SqlArtifact<ShowroomProduct>("product").Should().Exist();

        run.SqlArtifact<ShowroomProduct>("product")
            .Select(d => d.Row.Name)
            .Should().Be("Calibration Widget");

        run.SqlArtifact<ShowroomProduct>("product")
            .Select(d => d.Row.Price)
            .Should().Be(9.99m);
        // ^ The row is in the database. The price is correct.
        //   Both facts confirmed in fewer lines than a post-incident report.
    }
}

// ─── Module A5.2: Composite primary key ──────────────────────────────────────

[Collection("AzureShowroom")]
public class SqlServer_CompositePrimaryKey(ITestOutputHelper outputHelper)
{
    // If your entity has a composite PK, pass the values in the SAME ORDER
    // as EF Core's HasKey() expression.
    // In this case: InvoiceId first, LineNumber second.
    // Order matters. Order has always mattered.
    // We cannot stress this enough. We have stressed it. Many times.

    private static readonly Timeline _timeline = Timeline.Create()
        .SetupArtifact("invoiceLine")
        .Build();

    [Fact]
    public async Task Run()
    {
        var configSub = ShowroomSqlSetup.BuildConfig();

        var run = await _timeline
            .SetupRun(configSub.BuildServiceProvider(), outputHelper)
            .SetEnv(TestFramework.Container.Azure.DockerAzureEnvironment.For<AzureShowroom.DefaultFunctionAppDefinition>())
            .AddSqlArtifact(
                "invoiceLine",
                "MainSql",
                new ShowroomInvoiceLine { InvoiceId = "INV-2026-001", LineNumber = 1, Sku = "SHOW-001", Quantity = 5 },
                Var.Const("INV-2026-001"),  // first PK column: InvoiceId
                Var.Const("1"))             // second PK column: LineNumber (as string — framework converts via EF Core metadata)
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
    // Don't know the exact PK? Build a LINQ query.
    // The framework evaluates it, picks up the matching rows,
    // registers them as artifacts (with cleanup!), and hands them back.
    //
    // The query is a lambda over IQueryable<T>. Real LINQ. Real EF Core.
    // If you can write a Where clause, you can use this.
    // We checked. Almost everyone can write a Where clause.
    // The rest can learn. That is what this showroom is for.

    private static readonly Timeline _timeline = Timeline.Create()
        .SetupArtifact("prodTools1")
        .SetupArtifact("prodTools2")
        .SetupArtifact("prodOther")
        .FindArtifactMulti(
            ["toolsProducts"],  // base name — results come back as toolsProducts_0, toolsProducts_1, etc.
            AzureTF.ArtifactFinder.DB.SqlQuery<ShowroomProduct>(
                "MainSql",
                q => q.Where(p => p.Category == "Instruments")))
        // ^ Only the "Instruments" rows come back. The others do not haunt you.
        //   They are cleaned up. All of them. Even the ones that didn't match.
        //   Clean slate policy. Firm but fair.
        .Build();

    [Fact]
    public async Task Run()
    {
        var configSub = ShowroomSqlSetup.BuildConfig();

        var run = await _timeline
            .SetupRun(configSub.BuildServiceProvider(), outputHelper)
            .SetEnv(TestFramework.Container.Azure.DockerAzureEnvironment.For<AzureShowroom.DefaultFunctionAppDefinition>())
            .AddSqlArtifact("prodTools1", "MainSql",
                new ShowroomProduct { Sku = "INST-001", Name = "Precision Gauge",     Price = 149m, Category = "Instruments" },
                Var.Const("INST-001"))
            .AddSqlArtifact("prodTools2", "MainSql",
                new ShowroomProduct { Sku = "INST-002", Name = "Thermal Probe",       Price = 229m, Category = "Instruments" },
                Var.Const("INST-002"))
            .AddSqlArtifact("prodOther", "MainSql",
                new ShowroomProduct { Sku = "SNCK-001", Name = "Vending Machine Snack", Price = 1.25m, Category = "Refreshments" },
                //                                                                                      ^ Not an instrument. Will not appear in results.
                //                                                                                        Will be cleaned up anyway. As it should be.
                Var.Const("SNCK-001"))
            .RunAsync();

        run.EnsureRanToCompletion();

        run.SqlArtifact<ShowroomProduct>("toolsProducts").Should().Exist();
        run.SqlArtifact<ShowroomProduct>("toolsProducts_1").Should().Exist();

        run.SqlArtifact<ShowroomProduct>("toolsProducts")
            .Select(d => d.Row.Category)
            .Should().Be("Instruments");
        // ^ Correct category. Correct everything.
        //   Two rows. Two assertions. A controlled and repeatable result.
        //   This is what peak test infrastructure looks like.
        //   We're proud of this one. Don't tell the other modules.
    }
}
