# TestFramework.Showroom Start Here

This guide is the documentation-first wrapper around the showroom test projects.
Use it when you want to learn the framework in sequence before reading the xUnit files directly.

## Reading Order

1. Read the root [README](../README.md) for the ecosystem overview.
2. Open `TestFramework.Showroom.Basic/01_MinimalTimeline.cs` to see the smallest complete timeline.
3. Continue through the Basic chapter in order so each concept builds on the previous one.
4. Move to the Web, UI or Azure chapter only after the Core flow feels natural. Each is independent of the
   others; pick the one closest to the system you test.

## Basic Chapter Map

- `01_MinimalTimeline.cs`: smallest build -> run -> assert shape
- `02_MessageTimeline.cs`: simple interaction flow
- `03_DebugOutput.cs`: logging and readable output during a run
- `04_Variables.cs`: runtime data flow through variables
- `05_Artifacts.cs`: tracked runtime resources
- `06_Events.cs`: waits and external synchronization
- `07_ControlFlow.cs`: conditional and repeated composition
- `08_FluentAssertions.cs`: run inspection and assertion style
- `09_StepValidations.cs`: validating step behavior and outcomes
- `10_IOContracts.cs`: declared inputs and outputs
- `11_Retry.cs`: retry as a cross-cutting modifier
- `12_PersistentEnvironment.cs`: low-level persistent environment reuse in pure Core
- `13_Parallel.cs`: mergeable prepare work, explicit barriers, and setup serialization
- `14_ArtifactLifecycle.cs`: artifact lifecycle as a consumer contract: declare, populate, discover, assert
- `15_ErrorPaths.cs`: timeout exhaustion, discovery mismatches, and reading formatted recovery output
- `16_InteractiveTriggers.cs`: a trigger that opens a real message box and waits for the person watching
- `17_Mocking.cs`: the system under test hosted in-process, one dependency replaced by a declared double

## Web Chapter Map

- `WebShowroom.cs`: the facility - one database, one stub, one application, declared once
- `W1_RestApi.cs`: requests as steps, responses as results
- `W2_SqlServer.cs`: rows as artifacts, statements as steps
- `W3_SchemaFromModels.cs`: tables generated from the model map, without Docker
- `W4_Stubs.cs`: asserting what the application sent outwards
- `W5_ContainerLane.cs`: all four sources of truth in one run

## UI Chapter Map

- `UiShowroom.cs`: the facility - one site and one browser entry, neither with an address
- `U1_Storefront.cs`: a browser shops in the words a customer sees
- `U2_ShopFloor.cs`: interactions a weaker tool fakes
- `U3_Evidence.cs`: screenshots and step widgets the run keeps

## Azure Chapter Map

- `A0_ConfigurationPatterns.cs`: runnable explanation of how a resource is addressed - declared once by name with `ConfigInstance`, asked for from the run
- `A1` to `A5`: focused Azure building blocks
- `A6_IntegratedAzure.cs`: capstone multi-service flow
- `A7_ComponentComposition.cs`: container-backed composition model
- `A8_FunctionApps.cs`: Function App path chooser and HTTP patterns
- `A9_PersistentHostedFixture.cs`: persistent hosted fixture reuse with run-local config layering

## Function App Path Chooser

Use the Function App examples in this order depending on where the app runs.

1. In-process hostless logic checks: use `AzureExt.Trigger.FunctionApp.InProcessHttp(...)` in the Azure package tests or your own focused tests.
2. Local Docker-backed host: use the showroom Azure chapter with `DockerAzureEnvironment`, especially `A8_FunctionApps.cs`.
3. Deployed remote host: keep the same remote Function App trigger surface from `TestFramework.Azure`, but provide real `FunctionAppConfig` values instead of the Docker-backed environment.

The key boundary is simple: Timeline stays the same shape, `TestFramework.Azure` owns the trigger surface, and `TestFramework.Container.Azure` owns local Docker bootstrap.

## How To Use The Showroom Well

- Read examples in order before jumping to integrated scenarios.
- Treat the examples as runnable documentation, not just as test files.
- Use the root README and Arc42 doc for architecture questions that the examples intentionally keep short.
- Use `TestFramework.Core` docs when you need the full contract behind a modifier or runtime abstraction.