# TestFramework-Showroom - arc42 Architecture Documentation

> Date: 2026-04-20

## 1. Introduction and Goals

TestFramework-Showroom is the example and onboarding solution for the TestFramework ecosystem.
It does not introduce a new runtime; instead, it demonstrates how the existing packages are meant to be consumed.

Primary goals:

- provide small, readable examples for first contact with the framework
- show how core concepts map to executable tests
- demonstrate how multiple extension packages are used from a consumer perspective
- offer end-to-end Azure-backed samples that teams can adapt for real integration tests

## 2. Constraints

- Runtime target is .NET 8 (`net8.0`).
- Projects are test projects, not production libraries.
- `TestFramework.Showroom.Basic` depends on published TestFramework packages such as `TestFramework.Core`, `TestFramework.Simple`, and `TestFramework.LocalIO`.
- `TestFramework.Showroom.Azure` depends on `TestFramework.Core`, `TestFramework.Config`, and `TestFramework.Azure` plus a local Azure Functions project used as test infrastructure.
- The Azure examples require external infrastructure and a developer-local `local.testSettings.json` copied from the checked-in example template to run successfully.

## 3. System Scope and Context

The showroom sits at the outer edge of the ecosystem.
It acts as a consumer of other repositories and translates architecture into runnable examples.

Relevant collaborators:

- `TestFramework-Core`: provides the base timeline model used by almost every sample
- `TestFramework-LocalIO`: supports command/file-oriented examples in the basic sample set
- `TestFramework-Azure`: powers the Azure showroom scenarios
- local Azure Functions sample app: provides endpoints and triggers required by the Azure examples
- developers and onboarding team members: primary audience consuming the solution as executable documentation

The scope is educational and demonstrational, not foundational runtime logic.

## 4. Solution Strategy

The solution is split into two complementary tracks:

- `TestFramework.Showroom.Basic`: focused, concept-oriented examples for the core mental model
- `TestFramework.Showroom.Azure`: service-oriented scenarios that compose the Azure extension with real infrastructure assumptions

The strategy is to progress from smallest-possible examples to realistic orchestrations.
This keeps learning incremental: first understand timeline mechanics, then move to richer environment integration.

## 5. Building Block View

Main building blocks:

- `TestFramework.Showroom.Basic`: ten focused samples covering minimal timelines, message boxes, debug output, variables, artifacts, events, control flow, fluent assertions, step validations, and IO contracts
- `TestFramework.Showroom.Azure`: six Azure scenario modules covering Blob Storage, Table Storage, Cosmos DB, Service Bus, SQL Server, and an integrated orchestration sample
- `Azure/FunctionApp`: supporting Azure Functions application used by the Azure showroom scenarios
- solution root README: guides readers through the learning path and recommended entry points

The solution structure mirrors the learning journey: concept examples first, infrastructure-backed examples second.

## 6. Runtime View

Typical runtime paths:

1. Basic learning path
	 A developer opens one of the `TestFramework.Showroom.Basic` files, runs the xUnit test, and observes how a timeline is built, executed, and asserted.

2. Azure sample path
	 A developer copies `example.local.testsettings.json` to `local.testSettings.json`, fills in local or test-only values, runs the local or deployed infrastructure dependencies, and executes one of the Azure showroom tests. The test then uses the real TestFramework packages to set up artifacts, trigger operations, wait for external effects, and assert the results.

3. Integrated orchestration path
	 The `A6_IntegratedAzure` sample combines multiple services and multiple phases of orchestration into one timeline to demonstrate how the framework supports complete end-to-end workflows.

The runtime value of the showroom is not hidden logic, but observability into package usage patterns.

## 7. Deployment View

Deployment/build shape:

- `TestFramework.Showroom.Basic`: test project only, runs directly under the test runner
- `TestFramework.Showroom.Azure`: test project only, but requires package dependencies and external Azure configuration
- `Azure/FunctionApp`: executable Azure Functions application used as supporting infrastructure for the Azure samples

This means the showroom has both a pure consumer layer and a small supporting infrastructure layer for richer demos.

## 8. Cross-Cutting Concepts

- executable documentation: examples are not pseudo-code; they are test projects intended to run
- progressive disclosure: examples start with minimal timelines and scale up to integrated cloud scenarios
- consumer-first perspective: the code demonstrates package usage exactly how downstream projects are expected to consume it
- configuration-through-ConfigInstance: Azure samples follow the same configuration builder pattern recommended in the other repos
- naming by scenario: numbered files communicate learning order and conceptual grouping

## 9. Architecture Decisions

- Keep showroom examples in separate projects for Basic and Azure.
	Rationale: separates cognitive load between framework fundamentals and infrastructure-heavy scenarios.

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
- Infrastructure friction: Azure samples have a higher setup cost and may block onboarding if prerequisites are unclear
- Style variance: the examples intentionally mix serious and humorous commentary, which may reduce consistency for some audiences
- No dedicated CI evidence in this repo: there is no checked-in workflow file proving continuous validation of the showroom solution itself
- Dependency sensitivity: because the showroom depends on published packages, mismatches between package versions and example assumptions can break examples indirectly

## 12. Glossary

- Showroom: repository containing runnable consumer examples for the TestFramework ecosystem
- Basic samples: concept-focused examples that explain the core mental model
- Azure samples: infrastructure-backed examples using `TestFramework.Azure` and `TestFramework.Config`
- Timeline: ordered workflow built with the TestFramework DSL
- ConfigInstance: builder used to prepare configuration and dependency injection for a run
- FunctionApp: local Azure Functions support project used by Azure showroom scenarios
