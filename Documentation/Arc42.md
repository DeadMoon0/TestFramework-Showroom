# TestFramework-Showroom - arc42 Architecture Documentation

> Date: 2026-10-14

## 1. Introduction and Goals

TestFramework-Showroom is the example and onboarding solution for the TestFramework ecosystem.
It does not introduce a new runtime; instead, it demonstrates how the existing packages are meant to be consumed.

Primary goals:

- provide small, readable examples for first contact with the framework
- show how core concepts map to executable tests
- demonstrate how multiple extension packages are used from a consumer perspective
- offer container-backed web, browser and Azure samples that teams can adapt for real integration tests

## 2. Constraints

- Runtime target is .NET 8 (`net8.0`).
- Projects are test projects, not production libraries.
- Every lane consumes the published packages through `PackageReference`, pinned in `Directory.Packages.props`.
- `TestFramework.Showroom.Basic` depends on `TestFramework.Core`, `TestFramework.Config`, `TestFramework.Simple` and `TestFramework.LocalIO`.
- `TestFramework.Showroom.Web` depends on `TestFramework.Web` and `TestFramework.Container.Web`.
- `TestFramework.Showroom.UI` depends on `TestFramework.UI.Browser` and `TestFramework.Container.Web`.
- `TestFramework.Showroom.Azure` depends on `TestFramework.Azure` and `TestFramework.Container.Azure`, plus the local Azure Functions project as test infrastructure.
- The Web, UI and Azure lanes need a Docker daemon, and the UI lane a browser. Neither is configured by hand: containers publish every address at run time, and a missing daemon or browser is a skip with its reason.

## 3. System Scope and Context

The showroom sits at the outer edge of the ecosystem.
It acts as a consumer of other repositories and translates architecture into runnable examples.

Relevant collaborators:

- `TestFramework-Core`: provides the base timeline model used by almost every sample
- `TestFramework-LocalIO`: supports command/file-oriented examples in the basic sample set
- `TestFramework-Web` and `TestFramework-UI`: the REST, SQL, stub and browser steps of the Web and UI lanes
- `TestFramework-Azure`: powers the Azure showroom scenarios
- `TestFramework-Container`: the container environments behind the Web, UI and Azure lanes
- `Web/OrdersApi` and `Web/StorefrontSite`: the application and the site the Web and UI lanes test
- local Azure Functions sample app: provides endpoints and triggers required by the Azure examples
- developers and onboarding team members: primary audience consuming the solution as executable documentation

The scope is educational and demonstrational, not foundational runtime logic.

## 4. Solution Strategy

The solution is split into four lanes:

- `TestFramework.Showroom.Basic`: focused, concept-oriented examples for the core mental model
- `TestFramework.Showroom.Web`: REST APIs, SQL Server and stubbed dependencies, hosted in containers
- `TestFramework.Showroom.UI`: a real browser against a container-hosted site
- `TestFramework.Showroom.Azure`: service-oriented scenarios that compose the Azure extension with container-hosted emulators

The strategy is to progress from smallest-possible examples to realistic orchestrations.
This keeps learning incremental: first understand timeline mechanics, then move to richer environment integration.

## 5. Building Block View

Main building blocks:

- `TestFramework.Showroom.Basic`: seventeen focused samples, from the minimal timeline through variables, artifacts, events, control flow, assertions, IO contracts, retry, persistent environments, parallelism, the artifact lifecycle, error paths, interactive triggers and in-process mocking
- `TestFramework.Showroom.Web`: a facility declaration and five modules covering REST, SQL Server, schema from models, stubs and the full container lane
- `TestFramework.Showroom.UI`: a facility declaration and three modules covering shopping in a browser, real interactions and the evidence a run keeps
- `TestFramework.Showroom.Azure`: ten modules covering configuration, Blob Storage, Table Storage, Cosmos DB, Service Bus, SQL Server, integrated orchestration, composition rules, Function Apps and a persistent hosted fixture
- `Azure/FunctionApp`: supporting Azure Functions application used by the Azure showroom scenarios
- solution root README: guides readers through the learning path and recommended entry points

The solution structure mirrors the learning journey: concept examples first, infrastructure-backed examples second.

## 6. Runtime View

Typical runtime paths:

1. Basic learning path
	 A developer opens one of the `TestFramework.Showroom.Basic` files, runs the xUnit test, and observes how a timeline is built, executed, and asserted.

2. Container-backed path
	 A developer starts Docker and runs one of the Web, UI or Azure tests. The environment starts the containers the run needs and publishes their addresses under the resources' identifiers; the test then uses the real TestFramework packages to set up artifacts, trigger operations, wait for external effects, and assert the results.

3. Integrated orchestration path
	 The `A6_IntegratedAzure` sample combines multiple services and multiple phases of orchestration into one timeline to demonstrate how the framework supports complete end-to-end workflows.

The runtime value of the showroom is not hidden logic, but observability into package usage patterns.

## 7. Deployment View

Deployment/build shape:

- `TestFramework.Showroom.Basic`: test project only, runs directly under the test runner
- `TestFramework.Showroom.Web`, `TestFramework.Showroom.UI` and `TestFramework.Showroom.Azure`: test projects that start their own containers through Docker
- `Web/OrdersApi` and `Web/StorefrontSite`: built or copied into containers by the environment, never referenced by the test assemblies
- `Azure/FunctionApp`: executable Azure Functions application used as supporting infrastructure for the Azure samples

This means the showroom has both a pure consumer layer and a small supporting infrastructure layer for richer demos.

## 8. Cross-Cutting Concepts

- executable documentation: examples are not pseudo-code; they are test projects intended to run
- progressive disclosure: examples start with minimal timelines and scale up to integrated cloud scenarios
- consumer-first perspective: the code demonstrates package usage exactly how downstream projects are expected to consume it
- configuration-through-ConfigInstance: Azure samples follow the same configuration builder pattern recommended in the other repos
- naming by scenario: numbered files communicate learning order and conceptual grouping

## 9. Architecture Decisions

- Keep each lane in its own project.
	Rationale: separates cognitive load between framework fundamentals and infrastructure-heavy scenarios, and lets a reader run one lane without the others' prerequisites.

- Consume packages through `PackageReference` rather than project references for the main example projects.
	Rationale: validates the intended downstream usage model.

- Ship an auxiliary Azure Functions project in the same solution.
	Rationale: Azure examples need a controllable endpoint surface without depending only on abstract documentation.

- Organize examples as numbered narrative modules.
	Rationale: encourages a guided learning path and makes onboarding repeatable.

## 10. Quality Requirements

- Learnability: new team members should be able to start from `01_MinimalTimeline.cs` and build up understanding incrementally
- Accuracy: examples should match the current public package APIs and recommended usage patterns
- Executability: samples should be runnable tests, not static snippets only
- Coverage breadth: the showroom should cover both core concepts and realistic extension scenarios
- Reusability: teams should be able to copy and adapt samples into their own integration suites

## 11. Risks and Technical Debt

- Drift risk: example projects can become outdated when package APIs evolve
- Infrastructure friction: the container lanes need Docker, and the UI lane a browser; both skip with a reason when missing, but a first run still downloads images
- Style variance: the examples intentionally mix serious and humorous commentary, which may reduce consistency for some audiences
- No dedicated CI evidence in this repo: there is no checked-in workflow file proving continuous validation of the showroom solution itself
- Dependency sensitivity: because the showroom depends on published packages, mismatches between package versions and example assumptions can break examples indirectly

## 12. Glossary

- Showroom: repository containing runnable consumer examples for the TestFramework ecosystem
- Basic samples: concept-focused examples that explain the core mental model
- Web, UI and Azure samples: container-backed examples of the matching extension packages
- Timeline: ordered workflow built with the TestFramework DSL
- ConfigInstance: builder used to prepare configuration and dependency injection for a run
- FunctionApp: local Azure Functions support project used by Azure showroom scenarios
