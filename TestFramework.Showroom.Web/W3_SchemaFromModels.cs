using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TestFramework.Web.Sql.Model;
using TestFramework.Web.Sql.Schema;
using TestFramework.Web.Sql.Steps;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Web;

// ══════════════════════════════════════════════════════════════════════════════
//  WEB SYSTEMS DIVISION - PARTICIPANT ORIENTATION MODULE W3
//  "Furniture Assembled From The Instructions You Already Wrote"
//
//  The model map already knows the table, the columns, the key and which values
//  the database assigns. It seemed wasteful to make somebody re-type all of that
//  into a script and then maintain two copies of the same opinion until they
//  disagree, which they do, usually on a Friday.
//
//  So: tables can be generated from the models. Read the warning at the bottom of
//  this module before you enjoy that too much.
// ══════════════════════════════════════════════════════════════════════════════

// ─── Module W3.1: What a CLR type cannot tell you ────────────────────────────

public class Schema_DeclaredAlongsideTheMapping(ITestOutputHelper outputHelper)
{
    // A property says "string". A column needs to know how long, whether it accepts
    // nothing at all, and who assigns it. The generator will not guess at any of
    // that, on the grounds that a plausible guess is worse than a clear refusal.

    [Fact]
    public void Run()
    {
        SqlModelBuilder models = new();
        models.For<ShowroomOrder>()
            .Schema("sales").Table("Orders")
            .Key(x => x.Id).Identity(x => x.Id)     // the database assigns it
            .MaxLength(x => x.Name, 200)            // NVARCHAR(200), not NVARCHAR(MAX)
            .Precision(x => x.Total, 18, 2);        // currency, and it will stay currency

        string ddl = SqlSchema.CreateTable(SqlModelRegistry.CreateDefault(models).Resolve<ShowroomOrder>());
        outputHelper.WriteLine(ddl);

        Assert.Contains("IF OBJECT_ID(N'[sales].[Orders]', N'U') IS NULL", ddl, StringComparison.Ordinal);
        Assert.Contains("[Id] INT IDENTITY(1,1) NOT NULL", ddl, StringComparison.Ordinal);
        Assert.Contains("[Name] NVARCHAR(200) NOT NULL", ddl, StringComparison.Ordinal);
        Assert.Contains("[Total] DECIMAL(18,2) NOT NULL", ddl, StringComparison.Ordinal);
        Assert.Contains("CONSTRAINT [PK_Orders] PRIMARY KEY ([Id])", ddl, StringComparison.Ordinal);
        // ^ Guarded by an existence check, so a database that survives between runs
        //   is not asked to create the same table twice and become upset about it.
    }
}

// ─── Module W3.2: The same thing, said with attributes ───────────────────────

[Table("Shipments", Schema = "logistics")]
public sealed class ShowroomShipment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [MaxLength(64)]
    public string Carrier { get; set; } = "";

    // Nullable in the type, therefore nullable in the column. The type is allowed to
    // decide this one on its own; it has earned that much.
    public string? TrackingCode { get; set; }

    [Column(TypeName = "money")]
    public decimal Freight { get; set; }
    // ^ The escape hatch. When the generator cannot derive a type, or you simply
    //   want a different one, say it outright and it will be emitted verbatim.
}

public class Schema_FromAttributes(ITestOutputHelper outputHelper)
{
    [Fact]
    public void Run()
    {
        string ddl = SqlSchema.CreateTable(SqlModelRegistry.CreateDefault().Resolve<ShowroomShipment>());
        outputHelper.WriteLine(ddl);

        Assert.Contains("CREATE TABLE [logistics].[Shipments]", ddl, StringComparison.Ordinal);
        Assert.Contains("[Carrier] NVARCHAR(64) NOT NULL", ddl, StringComparison.Ordinal);
        Assert.Contains("[TrackingCode] NVARCHAR(MAX) NULL", ddl, StringComparison.Ordinal);
        Assert.Contains("[Freight] money NOT NULL", ddl, StringComparison.Ordinal);
    }
}

// ─── Module W3.3: A runnable script, and a word of caution ───────────────────

public class Schema_AsARunnableScript(ITestOutputHelper outputHelper)
{
    [Fact]
    public void Run()
    {
        SqlScript script = SqlSchema.CreateTablesScript(typeof(ShowroomShipment));
        outputHelper.WriteLine(script.Description);

        Assert.Equal("schema for ShowroomShipment", script.Description);
        Assert.Single(script.SplitBatches());
        // ^ One batch. It can be handed to WebExt.Sql.Script(...) like any other
        //   script, or to a container definition through WithSchemaFromModels.
    }

    // ─── PLEASE READ THIS PART ────────────────────────────────────────────────
    //
    //  Generated schema covers tables, columns, nullability, identities and primary
    //  keys. It does not cover foreign keys, indexes, check constraints or
    //  collations, and it is not a migration tool. It will not become one no matter
    //  how convenient that would be.
    //
    //  More importantly: a table generated from test-side models proves that your
    //  models agree with THEMSELVES. If the real schema is owned by somebody else -
    //  by migrations, by a database team, by a stored procedure written in 2011 by
    //  a contractor nobody can locate - then generating your own version and testing
    //  against it produces a green suite and no information whatsoever.
    //
    //  Use generation for fixtures you own. Mirror the real thing with a script when
    //  you do not. The framework cannot tell the difference between those two
    //  situations, but you can, and that is why you are here and it is not.
    // ──────────────────────────────────────────────────────────────────────────
}
