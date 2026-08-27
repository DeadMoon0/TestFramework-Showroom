# Configuration Patterns

There is one place to say where a resource is, and one place to ask.

- `ConfigInstance` is the setup path: the file, the overrides, the services.
- `context.Configured<T>("identifier")` is how a step reads a configured resource.

Nothing else reads configuration, and that is the whole point of this note.

## Saying it

```csharp
using TestFramework.Config;

ConfigInstance config = ConfigInstance
    .FromJsonFile("local.testSettings.json")
    .LoadAzureConfig()
    .Build();

TimelineRun run = await timeline
    .SetupRun(config.BuildServiceProvider(), outputHelper)
    .RunAsync();
```

`LoadAzureConfig()` does not read the file. It says which sections this package understands and what each one
describes, and the run reads them when it composes its resources — once, before the first step. A malformed
entry fails the run there, with the package's own message, rather than at the moment some step happens to
touch it.

## Asking for it

```csharp
public override Task<MyResult?> Execute(RunContext context)
{
    SqlDatabaseConfig sql = context.Configured<SqlDatabaseConfig>("MainSql");
    ...
}
```

The identifier is the same `MainSql` the timeline's artifacts name. One name, resolved by whoever needs it.

**`Configured<T>` deliberately does not tell you where the answer came from.** An address someone wrote in a
file and an address a container decided when it started arrive the same way. That is what lets one timeline
run against a deployed database and a containerized one without changing a line.

For a value rather than a whole record — and for the finished run rather than a step — `run.Values` answers
the same question:

```csharp
string databaseName = run.Values.Require(
    ValueRef.For(AzureEnvironmentResourceKinds.Sql, "MainSql", AzureEnvironmentResourceKinds.DatabaseNameValue),
    ResourceVantage.Host);
```

## A database context

The one case worth spelling out, because the obvious shape does not work:

```csharp
services.AddSqlArtifactContexts(registry =>
    registry.AddDefault<MyDbContext>(options => new MyDbContext(options)));
```

You register **how to construct** your context and are handed options that already point at the database this
run is using. It reads back-to-front from ordinary EF registration on purpose: the framework owns the address
and you own everything else.

`AddDbContext` cannot do this. It takes the connection string when the registration is built, from a service
provider, with no run in sight — so a containerized database could only ever be reached by writing its address
back into somebody's configuration after the container started.

## If you are coming from `ConfigStore<T>`

Earlier versions registered a typed store per record — `ConfigStore<SqlDatabaseConfig>` in Azure,
`WebConfigStore<ApiConfig>` in Web — and had you resolve it from the service provider. Both are gone.

```csharp
// before
var sql = provider.GetRequiredService<ConfigStore<SqlDatabaseConfig>>().GetConfig("MainSql");

// now
var sql = context.Configured<SqlDatabaseConfig>("MainSql");
```

A store could only hold what somebody wrote down before anything started, so a step reading one got a
placeholder for every address a container decides at startup. This note used to explain why `ConfigInstance`
and `ConfigStore<T>` were not two competing setup models; the honest answer turned out to be that the second
one should not have existed.
