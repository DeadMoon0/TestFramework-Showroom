![Icon](https://raw.githubusercontent.com/DeadMoon0/TestFramework-Common/96ef4240c1e55ba95a20b99285219a61407c6355/Assets/Icon.svg)

# TestFramework-Showroom

TestFramework is a timeline-based test framework for building integration-style test workflows.
Instead of scattering setup, actions, waits, and assertions across ad-hoc test code, it lets you model the whole run as one readable execution flow.

This solution is the example and learning space for that ecosystem.

If you want a documentation-first walkthrough before opening the test projects, start with [Documentation/StartHere.md](./Documentation/StartHere.md).

## Learning Path

Treat Showroom as the teaching surface, not as the proof harness.

- Start here when you want isolated, runnable examples that teach one concept at a time.
- Move to ConsumerScenarios when you want proof that a real user journey still works once those concepts are combined.
- Use [Documentation/LocalToDockerToLive.md](./Documentation/LocalToDockerToLive.md) when your real question is how the same scenario evolves across environments rather than how one API works in isolation.

## Quickstart

Run the basic example suite:

```bash
dotnet test TestFramework.Showroom.Basic/TestFramework.Showroom.Basic.csproj --configuration Release
```

Start with these files in order:

- `TestFramework.Showroom.Basic/01_MinimalTimeline.cs`
- `TestFramework.Showroom.Basic/02_MessageTimeline.cs`
- `TestFramework.Showroom.Basic/03_DebugOutput.cs`
- `TestFramework.Showroom.Basic/04_Variables.cs`
- `TestFramework.Showroom.Basic/05_Artifacts.cs`
- `TestFramework.Showroom.Basic/06_Events.cs`
- `TestFramework.Showroom.Basic/07_ControlFlow.cs`
- `TestFramework.Showroom.Basic/08_FluentAssertions.cs`
- `TestFramework.Showroom.Basic/09_StepValidations.cs`
- `TestFramework.Showroom.Basic/10_IOContracts.cs`
- `TestFramework.Showroom.Basic/11_Retry.cs`
- `TestFramework.Showroom.Basic/12_PersistentEnvironment.cs`
- `TestFramework.Showroom.Basic/13_Parallel.cs`
- `TestFramework.Showroom.Basic/14_ArtifactLifecycle.cs`
- `TestFramework.Showroom.Basic/15_ErrorPaths.cs`
- `TestFramework.Showroom.Basic/16_InteractiveTriggers.cs`

Then continue with the web lane in `TestFramework.Showroom.Web/` (see [Web Example Setup](#web-example-setup)) or the cloud lane in `TestFramework.Showroom.Azure/`.

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

## Web Example Setup

`TestFramework.Showroom.Web` covers the web lane: REST APIs, the SQL Server behind them, the
dependencies they call, and the container environment that supplies all three.

1. Run the suite. There is nothing to configure and no settings file to fill in — every address is
   published at run time by `DockerWebEnvironment`.
2. Docker is not a precondition to *run* this: every chapter that needs a daemon skips itself with a
   reason when none answers, so a machine without Docker gets thirteen explained skips instead of
   thirteen container errors. Start Docker Desktop when you want those chapters to actually execute.

```bash
dotnet test TestFramework.Showroom.Web/TestFramework.Showroom.Web.csproj --configuration Release
```

Read the modules in order:

- `TestFramework.Showroom.Web/WebShowroom.cs` — the facility: one database, one stub, one application, declared once
- `TestFramework.Showroom.Web/W1_RestApi.cs` — requests as steps, responses as results, and why a 404 is neither
- `TestFramework.Showroom.Web/W2_SqlServer.cs` — rows are artifacts, statements are steps, totals are observations
- `TestFramework.Showroom.Web/W3_SchemaFromModels.cs` — generating tables from the model map (runs without Docker)
- `TestFramework.Showroom.Web/W4_Stubs.cs` — asserting what the application sent *outwards*
- `TestFramework.Showroom.Web/W5_ContainerLane.cs` — all four sources of truth in one run

Only `W3` runs without a Docker daemon. Everything else carries both `[DockerFact]` and
`Category=DockerSmoke`, because the two answer different questions: the trait answers "should the
fast lane run this?" and needs the runner to remember a filter, while the skip answers "will this
fail environmentally?" and needs nothing from anybody.

### The Application Under Test

`Web/OrdersApi` is the specimen. Note what is missing from
`TestFramework.Showroom.Web.csproj`: there is **no project reference to it**. The application is
named by its project file through `ContainerSource.Project(...)`, the framework builds it and puts
it in an image, and the test assembly never loads a line of it.

That is what makes "the test does not depend on the implementation" a fact rather than a habit. The
API is reached by path and identifier, exactly as a deployed one would be.

### Why The Web Modules Run Serially

`W0_FacilityRules.cs` disables test parallelisation for this project. Several modules building the
same application project concurrently collide in the build system's own working directory, and each
full-facility module starts three containers. A production suite would share one environment across
a collection, as the Azure wing does; the showroom keeps every module standalone so it can be read
standalone, and pays for it in wall-clock time.

## Azure Example Setup

`TestFramework.Showroom.Azure` runs against the container-backed Azure environment by default.

1. Docker is not a precondition to *run* this lane: every chapter needs a daemon and every chapter
   skips itself with a reason when none answers. Start Docker Desktop when you want them to execute.
2. Create `TestFramework.Showroom.Azure/local.testSettings.json` from the placeholder shape in [example.local.testsettings.json](https://github.com/DeadMoon0/TestFramework-Container/blob/main/TestFramework.Container.Azure/example.local.testsettings.json) and fill in your own local or test-only values. Do not commit populated secrets.
3. Run the Azure showroom tests. Blob, Table, Cosmos, SQL, and Service Bus samples use `DockerAzureEnvironment` from `TestFramework.Container`.
4. The integrated Function App sample in `A6_IntegratedAzure.cs` now runs through the same container-backed Function App path as the normal Container.Azure smoke suite.
5. `A7_ComponentComposition.cs` demonstrates the new container composition model directly: shared dependencies, contract-selected providers, and exclusive dependency failures.
6. `A8_FunctionApps.cs` is the dedicated Function App chapter: when to use in-process vs local Docker vs deployed remote, plus liveness, route discovery, explicit request shaping, and default-route selection.
7. `A9_PersistentHostedFixture.cs` is the dedicated persistent-hosting chapter: one hosted container stack, many fresh run environments, and run-local config layering on top of a reused persistent slice.

Read `TestFramework.Showroom.Azure/A0_ConfigurationPatterns.cs` first if you want the shortest runnable explanation of why `ConfigInstance` and `ConfigStore<T>` can appear in the same Azure sample without representing two competing setup models.

### Azure Configuration Pattern

For almost all showroom scenarios, keep one ownership rule in mind:

- `ConfigInstance` is the setup entry point.
- module-specific typed stores such as Azure's `ConfigStore<T>` live inside the provider that `ConfigInstance` builds.

That means the normal learning path is:

1. create or load a `ConfigInstance`
2. apply Azure helpers such as `LoadDockerAzureConfig()`
3. build the provider for `SetupRun(...)`
4. let advanced services resolve typed stores from DI only when they need named resource records at runtime

If you want the side-by-side comparison between the simple path and the advanced mixed path, read [Documentation/ConfigurationPatterns.md](./Documentation/ConfigurationPatterns.md) before diving into `A5_SqlServer.cs` or `A6_IntegratedAzure.cs`.

### Function App Path Guide

- Use `A8_FunctionApps.cs` when you want the normal remote Function App trigger surface against a local Docker-backed host.
- Use `TestFramework.Azure` in-process builders when you want hostless Function App tests with no container bootstrap.
- Use the same remote Function App trigger surface against real Azure when you already have a deployed app and only need real `FunctionAppConfig` values.

Container/bootstrap remains a `TestFramework.Container.Azure` concern. The Azure trigger APIs stay the same across local and deployed paths.

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

- If Blob, Table, Cosmos, SQL, or Service Bus examples *skip* with "Requires Docker Desktop or another reachable Windows Docker named pipe.", the daemon is not answering. The gate also sets `DOCKER_HOST` from whichever Docker Desktop named pipe exists, so a machine that has Docker running usually needs nothing else.
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
- `TestFramework.Showroom.Basic/13_Parallel.cs` for the phase-first scheduler: mergeable prepare work, explicit barriers, and serialized artifact setup
- `TestFramework.Showroom.Basic/14_ArtifactLifecycle.cs` for the explicit artifact lifecycle: declare, populate, discover, and assert
- `TestFramework.Showroom.Basic/15_ErrorPaths.cs` for timeout, discovery mismatch, and formatted recovery output in the normal test path
- `TestFramework.Showroom.Basic/12_PersistentEnvironment.cs` for the low-level Core persistent environment primitive without Docker or config wrappers
- `TestFramework.Showroom.Web` for the web lane: REST APIs, SQL Server, stubbed dependencies, and the container environment behind them
- `TestFramework.Showroom.Azure` for Azure-oriented scenarios built on the Azure extension package
- `A7_ComponentComposition.cs` for the definition-graph composition rules behind the container-backed Azure environment
- `A8_FunctionApps.cs` for dedicated remote Function App usage patterns
- `A9_PersistentHostedFixture.cs` for xUnit-hosted persistent environment reuse

## How Showroom And ConsumerScenarios Differ

Showroom and ConsumerScenarios are both intentional, but they do different jobs.

- Showroom teaches isolated patterns and small narrative chapters.
- ConsumerScenarios validates composed user journeys and catches friction between modules.
- Showroom is where a new reader should learn first.
- ConsumerScenarios is where the same reader should look next when asking "does this still hold up in a realistic workflow?"

## What You Can Do With It

With this solution you can:

- learn the core framework by reading small focused examples
- compare basic timeline patterns before moving to larger integrations
- see how Azure scenarios are composed in real timeline code
- use the examples as onboarding material or starting points for your own tests

## Patterns Intentionally Left To Core Docs

Some concepts appear in Showroom only lightly because their main contract belongs to `TestFramework.Core`:

- modifier semantics such as `.WithRetry(...)`, `.WithTimeOut(...)`, and explicit `.DoNotParallelize()` barriers
- assertion composition and step-result inspection patterns
- extension-author concerns such as custom step, event, and artifact base types

Use Showroom to see those ideas in context, but use the Core docs when you need the full contract.

## Related Repositories

- [TestFramework-Core](https://github.com/DeadMoon0/TestFramework-Core) for the main engine used by nearly every sample
- [TestFramework-Azure](https://github.com/DeadMoon0/TestFramework-Azure) for the Azure-specific extension demonstrated by the Azure showroom samples
- [TestFramework-LocalIO](https://github.com/DeadMoon0/TestFramework-LocalIO) for local file and command-based scenarios that can complement the basic examples

## Where To Start

- Begin with `TestFramework.Showroom.Basic/01_MinimalTimeline.cs` to see the smallest possible timeline
- Follow with `02_MessageTimeline.cs` and `03_DebugOutput.cs` for the message trigger and debug output basics before adding more framework concepts
- Continue with `04_Variables.cs`, `05_Artifacts.cs`, `06_Events.cs`, `07_ControlFlow.cs`, `08_FluentAssertions.cs`, `09_StepValidations.cs`, `10_IOContracts.cs`, and `11_Retry.cs` to understand the core workflow model
- Read `14_ArtifactLifecycle.cs` when you want the artifact model spelled out as declare vs register vs discover instead of learning it indirectly across multiple chapters
- Read `15_ErrorPaths.cs` when you want to see how failure and recovery guidance show up in executable tests instead of only in docs
- Follow with `13_Parallel.cs` when you want to see how the scheduler groups Prepare work, honors `.DoNotParallelize()`, and still serializes setup for artifact types that require it
- Use `TestFramework.Showroom.Basic/12_PersistentEnvironment.cs` when you want the Core-only persistent environment reuse primitive before moving to hosted/container wrappers
- Move to `TestFramework.Showroom.Azure/A1_BlobStorage.cs` through `A6_IntegratedAzure.cs` when you want cloud-backed scenarios
- Start with `TestFramework.Showroom.Azure/A0_ConfigurationPatterns.cs` if the config ownership model is the main thing you want to understand before the service-specific Azure chapters
- Follow with `TestFramework.Showroom.Azure/A7_ComponentComposition.cs` when you want the container composition semantics behind multi-Function-App stacks
- Use `TestFramework.Showroom.Azure/A8_FunctionApps.cs` when you want the focused Function App HTTP chapter
- Use `TestFramework.Showroom.Azure/A9_PersistentHostedFixture.cs` when you want the hosted persistent-fixture pattern for larger Docker-backed suites

## Documentation Map

- Architecture overview: [Documentation/Arc42.md](./Documentation/Arc42.md)
- Configuration patterns: [Documentation/ConfigurationPatterns.md](./Documentation/ConfigurationPatterns.md)
- Local to Docker to live migration: [Documentation/LocalToDockerToLive.md](./Documentation/LocalToDockerToLive.md)
- Guided onboarding: [Documentation/StartHere.md](./Documentation/StartHere.md)
- Basic examples: [TestFramework.Showroom.Basic](./TestFramework.Showroom.Basic)
- Azure examples: [TestFramework.Showroom.Azure](./TestFramework.Showroom.Azure)
- Local Azure Functions support app: [Azure/FunctionApp](./Azure/FunctionApp)
