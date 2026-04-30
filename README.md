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
- `TestFramework.Showroom.Basic/10_IOContracts.cs`

## Retry Coverage

There is no dedicated numbered showroom file for `.WithRetry(...)` yet.
That is intentional for now: retry is a cross-cutting Core modifier rather than a LocalIO- or Azure-specific concept.

Use it like this when a step should tolerate transient failures:

```csharp
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Timelines;

Timeline timeline = Timeline.Create()
	.Trigger(step).WithRetry(3, CalcDelays.Fixed(TimeSpan.FromSeconds(1)))
	.Build();
```

For infrastructure-backed retry behavior, see the container smoke tests in this repository. For modifier semantics, prefer the Core documentation first.

## Azure Example Setup

`TestFramework.Showroom.Azure` runs against the container-backed Azure environment by default.

1. Start Docker Desktop.
2. Run the Azure showroom tests. Blob, Table, Cosmos, SQL, and Service Bus samples use `DockerAzureEnvironment` from `TestFramework.Container`.
3. The integrated Function App sample in `A6_IntegratedAzure.cs` now runs through the same container-backed Function App path as the normal Container.Azure smoke suite.
4. `A7_ComponentComposition.cs` demonstrates the new container composition model directly: shared dependencies, contract-selected providers, and exclusive dependency failures.

### A6 Integrated Azure Contract

`A6_IntegratedAzure.cs` is the capstone sample. Treat it as a phase-by-phase orchestration example rather than a quickstart:

1. Setup phase: seed Blob and SQL artifacts and register the future Table artifact reference.
2. Ingestion phase: publish the Service Bus request and wait for the ingestion acknowledgement.
3. Discovery phase: query Cosmos for the candidate profile written by the ingestion function.
4. Analysis phase: call the Function App HTTP endpoint and wait for the analysis acknowledgement.
5. Collection phase: capture the Table artifact version and validate the cross-service result.

The configuration contract is stricter than A1-A5 because the sample spans multiple services and a Function App. The Function App definition is the single source of truth for its storage, cosmos, and Service Bus dependencies, while the showroom config still supplies the matching named placeholder registrations such as `MainDb`, `MainStorage`, `SampleSubmission`, and `ProcessingReply`.

### Azure Troubleshooting

- If Blob, Table, Cosmos, SQL, or Service Bus examples fail immediately, check that Docker Desktop is running before the test host starts.
- If `A6_IntegratedAzure` fails during setup, verify that the Function App definition bindings and showroom config store identifiers still line up exactly.
- If Service Bus waits time out, inspect the correlation IDs in the example and confirm that the function emits replies on the expected queue/topic.
- If SQL-backed samples fail, make sure migrations or schema initialization from the container-backed environment have completed before re-running.

Run the Azure sample suite:

```bash
dotnet test TestFramework.Showroom.Azure/TestFramework.Showroom.Azure.csproj --configuration Release
```

## What This Solution Covers

TestFramework-Showroom contains runnable examples that demonstrate how the other TestFramework repositories fit together.
It currently includes:

- `TestFramework.Showroom.Basic` for core concepts such as timelines, variables, artifacts, events, control flow, and validations
- `TestFramework.Showroom.Azure` for Azure-oriented scenarios built on the Azure extension package
- `A7_ComponentComposition.cs` for the definition-graph composition rules behind the container-backed Azure environment

## What You Can Do With It

With this solution you can:

- learn the core framework by reading small focused examples
- compare basic timeline patterns before moving to larger integrations
- see how Azure scenarios are composed in real timeline code
- use the examples as onboarding material or starting points for your own tests

## Patterns Intentionally Left To Core Docs

Some concepts appear in Showroom only lightly because their main contract belongs to `TestFramework.Core`:

- modifier semantics such as `.WithRetry(...)`, `.WithTimeOut(...)`, and exclusive execution
- assertion composition and step-result inspection patterns
- extension-author concerns such as custom step, event, and artifact base types

Use Showroom to see those ideas in context, but use the Core docs when you need the full contract.

## Related Repositories

- [TestFramework-Core](https://github.com/DeadMoon0/TestFramework-Core) for the main engine used by nearly every sample
- [TestFramework-Azure](https://github.com/DeadMoon0/TestFramework-Azure) for the Azure-specific extension demonstrated by the Azure showroom samples
- [TestFramework-LocalIO](https://github.com/DeadMoon0/TestFramework-LocalIO) for local file and command-based scenarios that can complement the basic examples

## Where To Start

- Begin with `TestFramework.Showroom.Basic/01_MinimalTimeline.cs` to see the smallest possible timeline
- Continue with `04_Variables.cs`, `05_Artifacts.cs`, and `09_StepValidations.cs` to understand the core workflow model
- Move to `10_IOContracts.cs` for local IO-oriented thinking, then to `TestFramework.Showroom.Azure/A1_BlobStorage.cs` and `A6_IntegratedAzure.cs` when you want cloud-backed scenarios
- Follow with `TestFramework.Showroom.Azure/A7_ComponentComposition.cs` when you want the container composition semantics behind multi-Function-App stacks

## Documentation Map

- Architecture overview: [Documentation/Arc42.md](./Documentation/Arc42.md)
- Basic examples: [TestFramework.Showroom.Basic](./TestFramework.Showroom.Basic)
- Azure examples: [TestFramework.Showroom.Azure](./TestFramework.Showroom.Azure)
- Local Azure Functions support app: [Azure/FunctionApp](./Azure/FunctionApp)
