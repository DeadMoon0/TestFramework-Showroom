<identity>
    <package>TestFramework.Showroom</package>
    <role>addon-skill</role>
</identity>

<objective>
    Explain how TestFramework.Showroom should be used as the consumer-validation and onboarding surface for the ecosystem, especially when an agent needs realistic example structure instead of low-level API discovery.
</objective>

<package_scope>
    Covers the showroom README, the Basic scenarios, the Azure walkthrough, and the distinction between consumer onboarding examples and deeper framework-extension guidance.
</package_scope>

<key_concepts>
    Showroom is not the canonical source for every framework detail.
    Showroom is the place to study teachable example flow, progressive scenario sequencing, and user-facing guidance.
    Use Showroom when the agent needs to answer "how should this feel to a normal user?" rather than only "which API exists?"
    The examples move from simple local scenarios toward integrated Azure flows.
</key_concepts>

<best_practices>
    Use Showroom as the consumer-facing proof surface.
    Prefer examples that read top-to-bottom and show success path plus diagnosable failure path when useful.
    Keep retry usage, Azure setup expectations, and configuration guidance explicit near the example that needs them.
    Route deeper framework internals back to Core or package-specific docs instead of overloading onboarding examples.
</best_practices>

<usage_guidance>
    Consult Showroom when:
    - a user wants a realistic usage example
    - the agent needs to shape a README or onboarding flow
    - the question is about discoverability, teachability, or progressive scenario design

    Do not treat Showroom as the main source for extension-author API design.
    If the user asks for custom steps, events, artifacts, or deeper architecture, pivot back to Core and the addon packages.
</usage_guidance>

<notable_patterns>
    Important showroom lessons for the agent:
    - WithRetry(...) should be shown with a concrete usage example when retry semantics matter
    - IO-contract scenarios should demonstrate both the happy path and a meaningful failure-diagnosis path
    - the A6 Integrated Azure flow needs explicit phase and configuration explanation rather than assuming background knowledge
    - Azure troubleshooting belongs close to the Azure sample entry point
</notable_patterns>

<decision_rules>
    Recommend Showroom examples when the user needs:
    - a starting point for new tests
    - a README-ready usage example
    - a clearer consumer narrative across Core plus addon packages

    Recommend package docs instead when the user needs:
    - complete API details
    - project-specific configuration adaptation
    - extension-author guidance or runtime internals
</decision_rules>

<anti_patterns>
    Avoid:
    - turning onboarding examples into a second copy of the architecture documentation
    - assuming the reader already knows retry modifiers, Azure config wiring, or IO artifact inspection rules
    - expanding Showroom with framework-author content unless the request is explicitly about extension guidance
</anti_patterns>

<important_type_map>
    Common discovery anchors for the agent:
    - TestFramework-Showroom/README.md: primary onboarding narrative
    - TestFramework.Showroom.Basic: consumer-first local and core usage examples
    - TestFramework.Showroom.Azure: integrated Azure walkthrough and setup expectations

    Discovery heuristics for the agent:
    - if the user asks for an example first, inspect Showroom before inventing one
    - if the user asks why an API feels hard to teach, use Showroom as the benchmark for readability
    - if the user asks for extension-author material, treat the absence of deeper showroom examples as intentional unless they want the showroom scope expanded
</important_type_map>

<sources>
    TestFramework-Showroom/README.md
    TestFramework-Showroom/TestFramework.Showroom.Basic
    TestFramework-Showroom/TestFramework.Showroom.Azure
    1.0-TestFramework.Showroom
</sources>

<grounding_files>
    Most important files for expert grounding:
    - TestFramework-Showroom/README.md
    - TestFramework-Showroom/TestFramework.Showroom.Basic
    - TestFramework-Showroom/TestFramework.Showroom.Azure
    - 1.0-TestFramework.Showroom/RESOLUTION.md
    - 1.0-TestFramework.Showroom/VALIDATION.md
</grounding_files>

<repo_resolution>
    Do not assume or hardcode repository URLs.
    Resolve them when needed from the relevant project files.
</repo_resolution>