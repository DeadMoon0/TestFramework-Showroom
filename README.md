# TestFramework-Showroom

TestFramework is a timeline-based test framework for building integration-style test workflows.
Instead of scattering setup, actions, waits, and assertions across ad-hoc test code, it lets you model the whole run as one readable execution flow.

This solution is the example and learning space for that ecosystem.

## Quickstart

Run the basic example suite:

```bash
dotnet test TestFramework.Showroom.Basic/TestFramework.Showroom.Basic.csproj --configuration Release
```

Start with these files in order:

- `TestFramework.Showroom.Basic/01_MinimalTimeline.cs`
- `TestFramework.Showroom.Basic/04_Variables.cs`
- `TestFramework.Showroom.Basic/05_Artifacts.cs`
- `TestFramework.Showroom.Basic/09_StepValidations.cs`

## Azure Example Setup

`TestFramework.Showroom.Azure` runs against the container-backed Azure environment by default.

1. Start Docker Desktop.
2. Run the Azure showroom tests. Blob, Table, Cosmos, SQL, and Service Bus samples use `DockerAzureEnvironment` from `TestFramework.Container`.
3. The integrated Function App sample in `A6_IntegratedAzure.cs` is currently skipped until there is a clean container-backed Function App path.

Run the Azure sample suite:

```bash
dotnet test TestFramework.Showroom.Azure/TestFramework.Showroom.Azure.csproj --configuration Release
```

## What This Solution Covers

TestFramework-Showroom contains runnable examples that demonstrate how the other TestFramework repositories fit together.
It currently includes:

- `TestFramework.Showroom.Basic` for core concepts such as timelines, variables, artifacts, events, control flow, and validations
- `TestFramework.Showroom.Azure` for Azure-oriented scenarios built on the Azure extension package

## What You Can Do With It

With this solution you can:

- learn the core framework by reading small focused examples
- compare basic timeline patterns before moving to larger integrations
- see how Azure scenarios are composed in real timeline code
- use the examples as onboarding material or starting points for your own tests

## Related Repositories

- [TestFramework-Core](https://github.com/DeadMoon0/TestFramework-Core) for the main engine used by nearly every sample
- [TestFramework-Azure](https://github.com/DeadMoon0/TestFramework-Azure) for the Azure-specific extension demonstrated by the Azure showroom samples
- [TestFramework-LocalIO](https://github.com/DeadMoon0/TestFramework-LocalIO) for local file and command-based scenarios that can complement the basic examples

## Where To Start

- Begin with `TestFramework.Showroom.Basic/01_MinimalTimeline.cs` to see the smallest possible timeline
- Continue with `04_Variables.cs`, `05_Artifacts.cs`, and `09_StepValidations.cs` to understand the core workflow model
- Move to `10_IOContracts.cs` for local IO-oriented thinking, then to `TestFramework.Showroom.Azure/A1_BlobStorage.cs` and `A6_IntegratedAzure.cs` when you want cloud-backed scenarios

## Documentation Map

- Architecture overview: [Documentation/Arc42.md](./Documentation/Arc42.md)
- Basic examples: [TestFramework.Showroom.Basic](./TestFramework.Showroom.Basic)
- Azure examples: [TestFramework.Showroom.Azure](./TestFramework.Showroom.Azure)
- Local Azure Functions support app: [Azure/FunctionApp](./Azure/FunctionApp)
