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
- `TestFramework.Showroom.Basic/02_MSBTimeline.cs` (`MSB` = message-box timeline)
- `TestFramework.Showroom.Basic/03_DebugOutput.cs`
- `TestFramework.Showroom.Basic/04_Variables.cs`
- `TestFramework.Showroom.Basic/05_Artifacts.cs`
- `TestFramework.Showroom.Basic/06_Events.cs`
- `TestFramework.Showroom.Basic/07_ControlFlow.cs`
- `TestFramework.Showroom.Basic/08_FluentAssertions.cs`
- `TestFramework.Showroom.Basic/09_StepValidations.cs`
- `TestFramework.Showroom.Basic/10_IOContracts.cs`
- `TestFramework.Showroom.Basic/11_Retry.cs`

## Retry Coverage

`TestFramework.Showroom.Basic/11_Retry.cs` is the focused retry sample.
It keeps the scenario intentionally small because retry is a cross-cutting Core modifier rather than a LocalIO- or Azure-specific concept.

Use it like this when a step should tolerate transient failures:

```csharp
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Timelines;

Timeline timeline = Timeline.Create()
	.Trigger(step).WithRetry(3, CalcDelays.Fixed(TimeSpan.FromSeconds(1)))
	.Build();
```

For infrastructure-backed retry behavior, see the container smoke tests in this repository. For the full modifier contract, prefer the Core documentation first.

## Azure Example Setup

`TestFramework.Showroom.Azure` runs against the container-backed Azure environment by default.

1. Start Docker Desktop.
2. Create `TestFramework.Showroom.Azure/local.testSettings.json` from `TestFramework.Showroom.Azure/example.local.testsettings.json` and fill in your own local or test-only values. Do not commit populated secrets.
3. Run the Azure showroom tests. Blob, Table, Cosmos, SQL, and Service Bus samples use `DockerAzureEnvironment` from `TestFramework.Container`.
4. The integrated Function App sample in `A6_IntegratedAzure.cs` now runs through the same container-backed Function App path as the normal Container.Azure smoke suite.
5. `A7_ComponentComposition.cs` demonstrates the new container composition model directly: shared dependencies, contract-selected providers, and exclusive dependency failures.

### A6 Integrated Azure Contract

`A6_IntegratedAzure.cs` is the capstone sample. Treat it as a phase-by-phase orchestration example rather than a quickstart:

1. Setup phase: seed Blob and SQL artifacts and register the future Table artifact reference.
2. Ingestion phase: publish the Service Bus request and wait for the ingestion acknowledgement.
3. Discovery phase: query Cosmos for the candidate profile written by the ingestion function.
4. Analysis phase: call the Function App HTTP endpoint and wait for the analysis acknowledgement.
5. Collection phase: capture the Table artifact version and validate the cross-service result.

The configuration contract is stricter than A1-A5 because the sample spans multiple services and a Function App. The Function App definition remains the single source of truth for its storage, cosmos, and Service Bus bindings, and the shared showroom environment now materializes the matching defaults directly from the resource definitions. Service Bus emulator entities are declared through the fluent topology builder rather than an external JSON file.

### Service Bus Topology

The Azure showroom no longer uses `ShowroomAzure/ServiceBus/config.json`.
Service Bus entities are defined directly in code through `ConfigureServiceBusTopology(...)` on the Showroom resource definitions.

That means:

- `MainSBQueue` declares queue `sbq-main`
- `MainSBTopic` declares topic `sbt-main` with subscription `Default`
- `SampleSubmission` declares topic `sbt-int-in` with subscription `Default`
- `ProcessingReply` declares topic `sbt-int-out` with subscription `Default`

The examples now exercise the same fluent topology path that the container package README and smoke tests use.

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
- Follow with `02_MSBTimeline.cs` and `03_DebugOutput.cs` for the message-box trigger and debug output basics before adding more framework concepts
- Continue with `04_Variables.cs`, `05_Artifacts.cs`, `06_Events.cs`, `07_ControlFlow.cs`, `08_FluentAssertions.cs`, `09_StepValidations.cs`, `10_IOContracts.cs`, and `11_Retry.cs` to understand the core workflow model
- Move to `TestFramework.Showroom.Azure/A1_BlobStorage.cs` and `A6_IntegratedAzure.cs` when you want cloud-backed scenarios
- Follow with `TestFramework.Showroom.Azure/A7_ComponentComposition.cs` when you want the container composition semantics behind multi-Function-App stacks

## Documentation Map

- Architecture overview: [Documentation/Arc42.md](./Documentation/Arc42.md)
- Basic examples: [TestFramework.Showroom.Basic](./TestFramework.Showroom.Basic)
- Azure examples: [TestFramework.Showroom.Azure](./TestFramework.Showroom.Azure)
- Local Azure Functions support app: [Azure/FunctionApp](./Azure/FunctionApp)
