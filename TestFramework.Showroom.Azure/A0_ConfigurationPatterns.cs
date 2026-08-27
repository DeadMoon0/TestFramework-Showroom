using TestFramework.Azure;
using TestFramework.Core.Environment.Graph;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Azure.Configuration;
using TestFramework.Azure.Configuration.SpecificConfigs;
using TestFramework.Azure.Extensions;
using TestFramework.Config;
using TestFramework.Container.Azure;
using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using Xunit.Abstractions;

namespace TestFramework.Showroom.Azure;

//doc: One place to say it. One place to ask.
//doc:
//doc: 1. `ConfigInstance` owns setup - the file, the overrides, the services.
//doc: 2. `LoadAzureConfig()` says which sections this package reads and what each one describes.
//doc: 3. A step asks the run: `context.Configured<SqlDatabaseConfig>("MainSql")`.
//doc:
//doc: This chapter used to open by explaining why `ConfigInstance` and a typed `ConfigStore<T>` both showed
//doc: up in Azure samples without being two competing setup models. The honest answer turned out to be that
//doc: the store should not have been there: it could only hold what somebody wrote down before anything
//doc: started, so a step reading it got a placeholder for every address a container decides when it starts.
//doc: The stores are gone, and asking the run replaced them everywhere.
//doc:
//doc: What that buys is the thing this chapter is really about: the same timeline runs against a deployed
//doc: resource and a containerized one, and nothing in it says which. The address is looked up by name, and
//doc: whoever supplied it - a file, or an emulator that started ninety seconds ago - answers.
//doc:
//doc: Everything the cloud chapters run against - storage, Cosmos, SQL, several Service Bus definitions and
//doc: a Function App - is declared once in `AzureShowroom.cs`, and `AzureShowroom.CreateEnvironment()` is
//doc: what every chapter passes to `SetEnv`.
//doc:
//doc: Declaring all of it costs nothing, and the two panels below prove it rather than asserting it. Both
//doc: runs pass the *same* whole facility, and each reports what it decided to build. The blob run says
//doc: `components [azure-reset, azurite]`; the SQL run says `components [azure-reset, mssql]`. A run creates
//doc: the components its steps and artifacts actually ask for, so a chapter that touches one blob does not
//doc: start a Cosmos emulator to prove a point.
//doc:
//doc: While you are in that panel, notice the stage before the Main Stage. Environment components are
//doc: created in a preparatory stage and torn down in the cleanup stage, both without you writing either.

//doc: The default path, and the one to copy: build config, build provider, run timeline.
//doc:
//doc: The one detail worth imitating is the `await using`. `BuildServiceProvider()` hands back the concrete
//doc: `ServiceProvider` rather than an `IServiceProvider`, and that is not an accident - the provider owns
//doc: every singleton it created, including the ones holding Azure clients and connections. Naming it makes
//doc: the obligation visible; disposing it asynchronously is required rather than tidy, because one of those
//doc: singletons is `IAsyncDisposable` only, and the container refuses a synchronous dispose of such a
//doc: service instead of blocking on it.

public class ConfigurationPatterns_DefaultPath(ITestOutputHelper outputHelper)
{
    private static readonly Timeline _timeline = Timeline.Create()
        .SetupArtifact("blob")
        .Build();

    [DockerFact]
    [Trait("Category", "DockerSmoke")]
    public async Task Run()
    {
        ConfigInstance config = ConfigInstance.Create()
            .LoadDockerAzureConfig()
            .Build();

        // The provider is owned by this test, so it is named and disposed rather than
        // built inline and abandoned. BuildServiceProvider returns the concrete
        // ServiceProvider to make that ownership visible.
        await using ServiceProvider provider = config.BuildServiceProvider();

        var run = await _timeline
            .SetupRun(provider, outputHelper)
            .SetEnv(AzureShowroom.CreateEnvironment())
            .AddBlobArtifact(
                "blob",
                "MainStorage",
                "showroom/config-pattern-default.txt",
                Encoding.UTF8.GetBytes("ConfigInstance owns setup."))
            .RunAsync();

        run.EnsureRanToCompletion();
        run.BlobArtifact("blob").Should().Exist();
    }
}

//doc: The advanced path is the same path with one extra move at the end. It still starts with a
//doc: `ConfigInstance` - here the shared one from chapter A5 - and the difference is only that afterwards
//doc: the test asks the finished run what it was actually using.
//doc:
//doc: `run.Values` is how a configured resource is addressed: by kind, then by identifier. That is the same
//doc: `MainSql` the timeline's artifact names, which is the whole point of identifiers - one name, resolved
//doc: by whoever needs it.
//doc:
//doc: Ask the *run*, not the service provider. A provider can only ever hand back what somebody wrote down
//doc: before anything started, and half of what a run knows is decided later - a container binds a port when
//doc: it starts, and no configuration file can hold that. Asking the run gets whichever of the two is true
//doc: here, and a timeline written this way does not change when the database moves into a container.
//doc:
//doc: This run touches SQL, so `MainSql` is the resource it drags into the light. The rest of the declared
//doc: facility stays declared.

public class ConfigurationPatterns_AdvancedMixedPath(ITestOutputHelper outputHelper)
{
    private static readonly Timeline _timeline = Timeline.Create()
        .SetupArtifact("product")
        .Build();

    [DockerFact]
    [Trait("Category", "DockerSmoke")]
    public async Task Run()
    {
        ConfigInstance config = ShowroomSqlSetup.BuildConfig();

        // Owned here, so disposed here. BuildServiceProvider hands back the concrete
        // ServiceProvider precisely so that obligation is visible: the provider owns every
        // singleton it created, including the ones holding clients and connections.
        await using ServiceProvider provider = config.BuildServiceProvider();

        var run = await _timeline
            .SetupRun(provider, outputHelper)
            .SetEnv(AzureShowroom.CreateEnvironment())
            .AddSqlArtifact(
                "product",
                "MainSql",
                new ShowroomProduct
                {
                    Sku = "CFG-001",
                    Name = "Config Store Proof",
                    Price = 1.00m,
                    Category = "Docs"
                },
                Var.Const("CFG-001"))
            .RunAsync();

        run.EnsureRanToCompletion();

        string databaseName = run.Values.Require(
            ValueRef.For(AzureEnvironmentResourceKinds.Sql, "MainSql", AzureEnvironmentResourceKinds.DatabaseNameValue),
            ResourceVantage.Host);

        Assert.Equal("master", databaseName);
        // Only the resources required by the run need to be materialized.
        // This sample touches SQL, so MainSql is the one we drag into the light.
    }
}
