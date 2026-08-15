# Local To Docker To Live

> the framework is strongest when the same timeline idea survives across environments

That sentence is the claim this document exists to make checkable. The governing rule is narrow
enough to falsify:

**One scenario, three environments, and the only thing that changes is `SetEnv(...)`.**

If you find a scenario where moving from local to Docker to live changed the steps, the assertions,
or the shape of the timeline, then either the scenario was not parameterised properly or the claim
does not hold for that case. Both are worth knowing, and section 6 is the checklist that keeps you
out of the first category.

## 1. The claim, stated so you can check it

A TestFramework timeline is a plan: steps, events, variables, artifacts, assertions. An environment
is what those steps are pointed at. `SetEnv(...)` is the seam between them, and it is the only seam
this document asks you to move.

Read that as three concrete promises:

- The `Timeline.Create()...Build()` block is identical in all three stages.
- The assertions are identical in all three stages.
- Everything that differs is either an `IEnvironmentProvider` handed to `SetEnv(...)` or a value in
  the config instance handed to `SetupRun(...)`.

Stages 1 and 2 below are written from code in this repository that runs. Stage 3 is
**documented, not verified** — see the note at the end of section 5.

## 2. The scenario

The specimen is the Function App already in this repository: [`Azure/FunctionApp`](../Azure/FunctionApp).
No new sample is invented for this document, on purpose — a migration story told with a purpose-built
example proves only that purpose-built examples migrate well.

Two of its functions carry the story:

- `HttpTests.Run` — an anonymous HTTP trigger that returns a string. Enough to ask "did the request
  reach the function and come back?" without dragging in storage.
- `SampleIngestionFunction` / `AnalysisProcessingFunction` in `IntegrationFunctions.cs` — a Service
  Bus trigger and an HTTP trigger that read storage, Cosmos and Service Bus through their bindings.
  These are the ones that make the difference between the stages visible.

## 3. Stage 1 — local, in-process

Nothing is started. The function class is instantiated in the test process and invoked directly
through `AzureExt.Trigger.FunctionApp.InProcessHttp<TFunction>(...)`, which hands your lambda the
function instance and a proxy carrying the `HttpRequest`.

**What it proves**

- Routing and method selection: the right function runs for the request you built.
- Binding *contracts*: the function asks for the settings and inputs you think it asks for, and
  fails loudly when one is missing.
- Your own logic: branches, serialization, response shaping, error handling.
- Speed: a run costs milliseconds, so this is where a test about your code belongs.

**What it cannot prove**

- Real storage semantics — etags, conditional writes, container and blob naming rules, the actual
  behaviour of a missing table versus an empty one.
- Real Service Bus semantics — ordering, sessions, delivery counts, dead-lettering, lock renewal.
- Host behaviour — the Functions host's own binding resolution, startup, and settings expansion
  (`%ServiceBusTriggerTopicName%` and friends are host syntax; in-process, nothing expands them).
- Anything about how the app is packaged or deployed.

**Cross-link:** [`A8_FunctionApps.cs`](../TestFramework.Showroom.Azure/A8_FunctionApps.cs) is the
chapter that lays the remote and in-process Function App paths side by side.

## 4. Stage 2 — Docker

Same timeline. The emulators come up in containers, the Function App is built and hosted for real,
and the framework publishes the addresses at run time.

The diff from stage 1 is the point of this document:

```diff
  TimelineRun run = await _timeline
      .SetupRun(config.BuildServiceProvider(), outputHelper)
-     // stage 1: nothing is started; the function is called in-process
+     .SetEnv(DockerAzureEnvironment.For<ShowroomFunctionAppDefinition>())
      .RunAsync();
```

Three lines, one of them added. The timeline above it and the assertions below it do not move.

**What the extra line buys**

- A real Functions host, so `%Setting%` expansion, binding resolution and startup are exercised.
- Azurite, the Cosmos emulator, the Service Bus emulator and SQL Server behaving like the services
  rather than like your mental model of them.
- The composition graph: which containers a definition actually needs, and in what order.

**Cross-links:** [`A1_BlobStorage.cs`](../TestFramework.Showroom.Azure/A1_BlobStorage.cs) through
[`A6_IntegratedAzure.cs`](../TestFramework.Showroom.Azure/A6_IntegratedAzure.cs) for the per-service
chapters, and [`A9_PersistentHostedFixture.cs`](../TestFramework.Showroom.Azure/A9_PersistentHostedFixture.cs)
for keeping one container stack alive across a whole collection instead of rebuilding it per test.

Every chapter in that lane gates itself on a reachable Docker daemon and skips with a reason when
there is none, so stage 2 costs you an explained skip rather than a failure on a machine without
Docker.

## 5. Stage 3 — live Azure

Same timeline again. `SetEnv(...)` is dropped or replaced, and real `FunctionAppConfig` values —
`BaseUrl`, `Code`, `AdminCode` — are supplied through the config instance instead.

Two gotchas live only here, and both of them look like a broken framework the first time.

**ICMP ping never succeeds against Azure endpoints.** Azure's front ends do not answer ICMP echo, so
a liveness check built on ping reports "dead" for a perfectly healthy app. This is why
`FunctionAppTriggerConfig.DoPing` now defaults to `false`. Against a container on your own machine
ping works, so a suite that turned it on in stage 2 and never thought about it again will fail at
exactly the moment it moves to stage 3. Use an HTTP-level aliveness level such as
`AlivenessLevel.Reachable` instead — that is what `A8_FunctionApps.cs` does.

**Admin endpoints need the master key, not a function key.** The managed/admin path posts to the
host's admin surface, which does not accept a per-function key. `FunctionAppConfig.AdminCode` is the
master key, and the managed trigger sends `AdminCode ?? Code` in the `x-functions-key` header — so
leaving `AdminCode` unset silently falls back to a key the admin endpoint will reject, and the error
you get back is an authorization failure rather than "you configured the wrong kind of key". Set
`AdminCode` whenever you use the managed path; the plain HTTP path is happy with `Code`.

> **Documented, not verified.** Stages 1 and 2 are backed by code in this repository that runs.
> Stage 3 is written from the framework's own contracts and configuration surface and has not been
> executed against a live subscription as part of writing this document. Treat the two gotchas above
> as the known sharp edges, not as a claim that a live run has been observed end to end.

## 6. Migration checklist — parameterise before stage 1, not between stages

Do these before you write the first stage-1 test and stages 2 and 3 stay config-only changes. Do
them later and you will be editing timelines, which is exactly the thing this document claims you
should not have to do.

- **Name resources by identifier, never by connection string.** A step should ask for
  `"MainStorage"`, not for a connection string it happens to know. The connection string is an
  environment's answer to that identifier.
- **Reference artifacts, not paths.** `RegisterArtifact("newFile", ...FileRef(...))` survives the
  trip; a hardcoded `C:\...` does not.
- **No hardcoded ports or hostnames.** In stage 2 every address is published at run time; a literal
  `localhost:7071` is a stage-1-only assumption wearing a disguise.
- **No hardcoded queue, topic, container or database names in the test.** Declare them on the
  resource definition and let both the app and the test read them from there.
- **Keep app settings in the Function App definition.** The definition is the single source of truth
  for what the app binds to; a test that also knows those names has two sources of truth and will
  eventually disagree with itself.
- **No assumption that the store starts empty.** True in stage 1, arranged for you in stage 2 (see
  below), and emphatically untrue in stage 3.
- **No assumption that the store keeps anything between runs.** The mirror image of the previous
  point, and the one that bites when a stage-1 test quietly relies on in-process state.

## 7. What does not survive the trip

Not everything is portable, and pretending otherwise is how a migration document becomes a
disappointment.

- **Emulator quirks with no live equivalent.** `UseDevelopmentStorage=true`, the emulator's fixed
  well-known account key, the Service Bus emulator's namespace name, permissive CORS, and the fact
  that an emulator restarts in seconds. None of these exist in Azure. A test that leans on one of
  them is a test about the emulator.
- **Live behaviour with no emulator equivalent.** Managed identity and RBAC, throttling and 429s,
  geo-replication latency, private endpoints, and per-service features the emulators simply do not
  implement. These are the reason stage 3 exists at all.
- **The per-run purge.** The Azure hosted fixture keeps emulator containers alive across a whole
  collection, so their contents would survive from one run into the next. It therefore empties every
  resource the environment *declared* before each run — that is `AzureResetMode.PurgeDeclaredResources`,
  the default. Only declared resources are touched. There is no equivalent in stage 3, and you would
  not want one: nothing is going to purge your real storage account for you, so a stage-2 test that
  depends on starting empty needs its own arrange step before it moves to live.

## 8. Decision table

| Environment | What it proves | What it costs | Use it when |
| --- | --- | --- | --- |
| **Stage 1 — local, in-process** | Routing, binding contracts, your own logic, response shaping | Milliseconds; no daemon, no account | The test is about your code. This is most of your tests. |
| **Stage 2 — Docker** | Real host behaviour, real storage/Cosmos/Service Bus/SQL semantics, composition graph | Seconds to minutes per stack; needs a Docker daemon | The test is about how your code behaves against a service, not about the service's cloud identity. |
| **Stage 3 — live Azure** | Identity, RBAC, throttling, service features no emulator implements, real deployment | Minutes, a subscription, and real money; slowest and least deterministic | The test is about configuration, identity, or a feature that only exists in Azure. |

Decide once per suite, on what the suite needs to prove — not per test, and not by drifting upward
whenever something is hard to reproduce. Mixing the three mental models inside one suite is what
makes container tests feel slow and live tests feel flaky.

## Related reading

- [`Documentation/StartHere.md`](./StartHere.md) — the guided path through the showroom
- [`Documentation/ConfigurationPatterns.md`](./ConfigurationPatterns.md) — `ConfigInstance` versus `ConfigStore<T>`
- [`Documentation/Arc42.md`](./Arc42.md) — the architecture overview
