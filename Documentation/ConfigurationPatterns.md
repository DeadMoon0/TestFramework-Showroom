# Configuration Patterns

This note explains the two configuration shapes that appear in the Showroom and how they relate.

The short version is:

- `ConfigInstance` is the normal setup path for timeline runs.
- typed stores such as Azure's `ConfigStore<T>` are advanced runtime lookup services that live inside the provider built by `ConfigInstance`.

They are not competing root models.

## Pattern 1: Default Consumer Path

Use this when you want to:

- load JSON settings
- apply a few overrides
- register ordinary services
- build the `IServiceProvider` used by `SetupRun(...)`

Example:

```csharp
using TestFramework.Config;

ConfigInstance config = ConfigInstance
    .FromJsonFile("local.testSettings.json")
    .Build();

TimelineRun run = await timeline
    .SetupRun(config.BuildServiceProvider(), outputHelper)
    .RunAsync();
```

This is the default path. Most users should learn this first.

## Pattern 2: Advanced Mixed Path

Use this when a richer module needs named resource records at runtime.

In the Azure showroom, SQL-backed examples use `ConfigStore<SqlDatabaseConfig>` so EF and SQL artifact helpers can resolve a named SQL resource such as `MainSql`.

That still does not replace `ConfigInstance`.

Example shape:

```csharp
internal static ConfigInstance BuildConfig() =>
    ConfigInstance.Create()
        .LoadDockerAzureConfig()
        .AddService((services, _) =>
        {
            services.AddDbContext<MyDbContext>((serviceProvider, opts) =>
                opts.UseSqlServer(
                    serviceProvider
                        .GetRequiredService<ConfigStore<SqlDatabaseConfig>>()
                        .GetConfig("MainSql")
                        .ConnectionString));
        })
        .Build();
```

Read that flow like this:

1. `ConfigInstance` still owns the setup pipeline.
2. Azure config helpers populate typed stores inside DI.
3. advanced services resolve named records from those stores at runtime.

## Which One Should I Pick?

- You are writing a normal test and only need configuration plus service registration: use `ConfigInstance`.
- You are following an advanced Azure or SQL sample and see `ConfigStore<T>`: keep using `ConfigInstance` for setup and treat the store as a module-owned runtime dependency.
- If both appear in one sample, that is still one setup model, not two.
