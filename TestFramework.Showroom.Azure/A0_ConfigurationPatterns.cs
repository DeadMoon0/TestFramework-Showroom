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

// ══════════════════════════════════════════════════════════════════════════════
//  CLOUD INFRASTRUCTURE DIVISION - PARTICIPANT ORIENTATION MODULE A0
//  "One Setup Root. Several Honest Helpers."
//
//  ConfigInstance and ConfigStore<T> kept showing up together, which is the
//  sort of thing that makes people ask whether the framework has two setup
//  models. It does not. It has one model and a few specialists that know where
//  to stand.
//
//  The rule is:
//    1. ConfigInstance owns setup.
//    2. Azure loads typed stores into the provider ConfigInstance builds.
//    3. Advanced services only ask for typed stores when they actually need them.
//
//  Short version: one root model, one provider, several specialized helpers.
//  Less mythology, more plumbing.
// ══════════════════════════════════════════════════════════════════════════════

public class ConfigurationPatterns_DefaultPath(ITestOutputHelper outputHelper)
{
    // Start with the default path: build config, build provider, run timeline.
    // No typed store spelunking required.

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

public class ConfigurationPatterns_AdvancedMixedPath(ITestOutputHelper outputHelper)
{
    // Advanced modules still start with ConfigInstance. The difference is that
    // services can resolve typed config stores from the provider later, after
    // the setup machinery has already done the heavy lifting.

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

        SqlDatabaseConfig sql = provider
            .GetRequiredService<ConfigStore<SqlDatabaseConfig>>()
            .GetConfig("MainSql");

        Assert.Equal("master", sql.DatabaseName);
        // Only the resources required by the run need to be materialized.
        // This sample touches SQL, so MainSql is the one we drag into the light.
    }
}