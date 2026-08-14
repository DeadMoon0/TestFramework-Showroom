using System.Net;
using TestFramework.Config;
using TestFramework.Container.Sources;
using TestFramework.Container.Web;
using TestFramework.Web.Extensions;
using TestFramework.Web.Identifier;
using TestFramework.Web.Sql;
using TestFramework.Web.Stub;
using TestFramework.Web.Stub.Mappings;

namespace TestFramework.Showroom.Web;

// ══════════════════════════════════════════════════════════════════════════════
//  WEB SYSTEMS DIVISION - FACILITY DECLARATION
//
//  Everything the web modules run against is declared once, here. Three resources,
//  three definitions, no surprises: a database, an application, and a dependency
//  we have chosen to replace with something more cooperative.
//
//  Note that none of these definitions say where anything runs. That decision
//  belongs to the environment, and the environment is chosen at the last possible
//  moment, by the test. This is not indecision. It is the reason the same timeline
//  works against a container today and a real deployment on the day somebody
//  finally approves the budget.
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>The row model the tests seed, query and generate a table from.</summary>
public sealed class ShowroomOrder
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Quantity { get; set; }
    public decimal Total { get; set; }
}

internal static class WebShowroom
{
    // ─── The database ─────────────────────────────────────────────────────────
    // The table is derived from the model above rather than from a script that
    // somebody would have to remember to update. The generator handles tables,
    // columns, keys and identities. It does not handle indexes or foreign keys,
    // and it will not pretend otherwise, which is more than can be said for most
    // things that generate schema.

    internal sealed class OrdersSqlDefinition : DockerSqlDefinition
    {
        public override SqlIdentifier Identifier => "orders-db";

        protected override void Configure(DockerSqlBuilder builder) => builder
            .WithDatabase("ShowroomOrders")
            .WithSchemaFromModels<ShowroomOrder>()
            .WithResetMode(SqlResetMode.RecreateDatabase);
        // ^ Recreated every run. Yesterday's rows are yesterday's problem and are
        //   not invited to participate in today's conclusions.
    }

    // ─── The dependency we replaced ───────────────────────────────────────────
    // A stub is data, not code. It cannot run your callbacks, hold your state, or
    // develop opinions. It answers what it was told to answer and keeps a log of
    // everyone who asked, which turns out to be the useful half anyway.

    internal sealed class PricingStubDefinition : StubDefinition
    {
        public override StubIdentifier Identifier => "pricing";

        protected override void Configure(StubMappingBuilder builder) => builder
            .OnPost("/api/quotes")
                .WithBodyContaining("\"quantity\"")
                .RespondJson(HttpStatusCode.Created, new { status = "quoted", total = 42.50m })
            .OnGet("/api/health")
                .RespondJson(HttpStatusCode.OK, new { status = "healthy" });
        // ^ Declared most specific first. The server takes the first mapping that
        //   matches, exactly like a queue, and unlike most things in this building.
    }

    // ─── The application under test ───────────────────────────────────────────
    // Named by its project file. The framework builds it, puts it in an image, and
    // hands it addresses for the two resources above. At no point does this test
    // assembly load the application's code, which means it also cannot accidentally
    // assert against an implementation detail while nobody is looking.

    internal sealed class OrdersApiDefinition : DockerApiDefinition
    {
        public override ApiIdentifier Identifier => "orders";

        public override ContainerSource Source =>
            ContainerSource.Project("../Web/OrdersApi/OrdersApi.csproj");
        // ^ Relative to this very file, resolved at compile time. It reads the way
        //   it looks in the repository, which is the whole trick.

        protected override void Configure(DockerApiBuilder builder) => builder
            .WithHealthPath("/health")
            .UseSql<OrdersSqlDefinition>("ConnectionStrings:Orders")
            .UseStub<PricingStubDefinition>("Services:Pricing:BaseUrl");
        // ^ Both bindings inject the address a *container* reaches, never the one
        //   this process uses. Getting that backwards is the single most popular
        //   way to spend an afternoon, and the framework has removed the option.
    }

    // ─── Configuration ────────────────────────────────────────────────────────
    // No settings file. Every address is filled in at run time by the environment,
    // so there is nothing to keep in step and nothing to leave stale.

    internal static ConfigInstance BuildConfig() =>
        ConfigInstance.Create()
            .LoadWebConfig()
            // ^ Required even with nothing to load: it registers the stores the
            //   environment publishes into. An empty shelf is still a shelf.
            .AddWebSqlModels(models => models.For<ShowroomOrder>()
                .Table("Orders")
                .Key(x => x.Id)
                .Identity(x => x.Id)
                .MaxLength(x => x.Name, 200)
                .Precision(x => x.Total, 18, 2))
            // ^ Lengths and precision are declared because a CLR type does not
            //   carry them, and the generator refuses to invent them on your behalf.
            .Build();

    /// <summary>The full facility: database, stub and application.</summary>
    internal static DockerWebEnvironment CreateEnvironment() =>
        DockerWebEnvironment.For<OrdersSqlDefinition>()
            .IncludeStub<PricingStubDefinition>()
            .Include<OrdersApiDefinition>();

    /// <summary>Only the database, for modules that have no interest in the application.</summary>
    internal static DockerWebEnvironment CreateDatabaseOnlyEnvironment() =>
        DockerWebEnvironment.For<OrdersSqlDefinition>();
}
