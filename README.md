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

The Azure examples in `TestFramework.Showroom.Azure` require real configuration and supporting infrastructure.

1. Review and update `TestFramework.Showroom.Azure/local.testSettings.json` with your own values.
2. Make sure the identifiers used by the samples exist in your environment, especially `MainStorage`, `MainSql`, `SampleSubmission`, `ProcessingReply`, and `FunctionApp:Default`.
3. If you want to run the Azure Function scenarios locally or against a controlled endpoint, inspect `Azure/FunctionApp` and the Azure showroom samples together.
4. Do not commit secrets or production credentials to the repository.

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
