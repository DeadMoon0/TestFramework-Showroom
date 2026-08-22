using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TestFramework.Web.Sql.Model;
using TestFramework.Web.Sql.Schema;
using TestFramework.Web.Sql.Steps;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Web;

//doc: Furniture assembled from the instructions you already wrote.
//doc:
//doc: The model map already knows the table, the columns, the key and which values the database assigns. It
//doc: seemed wasteful to make somebody re-type all of that into a script and then maintain two copies of the
//doc: same opinion until they disagree, which they do, usually on a Friday. So: tables can be generated from
//doc: the models.
//doc:
//doc: Read the warning at the end of this chapter before you enjoy that too much.
//doc:
//doc: This is also the one chapter in the web lane that needs no Docker daemon - it is three plain `[Fact]`s
//doc: that generate strings and read them back. The asymmetry is in the code rather than in a README on
//doc: purpose, and the output panels below show the actual generated DDL, because these tests print it.

//doc: First, what a CLR type cannot tell you. A property says `string`. A column needs to know how long,
//doc: whether it accepts nothing at all, and who assigns it. The generator will not guess at any of that, on
//doc: the grounds that a plausible guess is worse than a clear refusal - so length and precision are
//doc: declared alongside the mapping, next to the table and key they belong with.
//doc:
//doc: Note the `IF OBJECT_ID(...) IS NULL` guard in the output. A database that survives between runs is not
//doc: asked to create the same table twice and become upset about it.

public class Schema_DeclaredAlongsideTheMapping(ITestOutputHelper outputHelper)
{
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

//doc: The same information, said with attributes instead of a builder. Both routes reach the same generator,
//doc: so pick whichever keeps the knowledge closest to whoever maintains it.
//doc:
//doc: Three details in this one type are each a rule:
//doc:
//doc: - `[Table]`, `[Key]` and `[DatabaseGenerated]` say the same things the builder said.
//doc: - `TrackingCode` is nullable in the type, therefore nullable in the column. That is the one decision
//doc:   the type is allowed to make on its own; it has earned that much.
//doc: - `[Column(TypeName = "money")]` is the escape hatch. When the generator cannot derive a type, or you
//doc:   simply want a different one, say it outright and it is emitted verbatim.

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

//doc: With nothing but the attributes, `CreateDefault()` needs no builder at all - and the output shows what
//doc: each attribute turned into, including the `NVARCHAR(MAX) NULL` that the nullable property produced.

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

//doc: And the useful form: a script rather than a string. `CreateTablesScript` produces a `SqlScript` with a
//doc: description and batches, which can be handed to `WebExt.Sql.Script(...)` like any other script, or to
//doc: a container definition through `WithSchemaFromModels` - which is exactly what `WebShowroom.cs` does
//doc: for the database the other chapters run against.

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

    //doc: ## Please read this part
    //doc:
    //doc: Generated schema covers tables, columns, nullability, identities and primary keys. It does not
    //doc: cover foreign keys, indexes, check constraints or collations, and it is not a migration tool. It
    //doc: will not become one no matter how convenient that would be.
    //doc:
    //doc: More importantly: a table generated from test-side models proves that your models agree with
    //doc: *themselves*. If the real schema is owned by somebody else - by migrations, by a database team, by
    //doc: a stored procedure written in 2011 by a contractor nobody can locate - then generating your own
    //doc: version and testing against it produces a green suite and no information whatsoever.
    //doc:
    //doc: Use generation for fixtures you own. Mirror the real thing with a script when you do not. The
    //doc: framework cannot tell the difference between those two situations, but you can, and that is why
    //doc: you are here and it is not.
}
