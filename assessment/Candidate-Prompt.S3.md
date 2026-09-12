# Task-o-Time: maintainable desktop delivery (S3)

Task-o-Time is a Windows Presentation Foundation (WPF) application backed by SQL Server and Entity
Framework 6. It grew through a handover between developers, and conventions
were not applied consistently. We need a maintainable C# implementation
without losing the working desktop experience or existing data.

Start by inspecting the solution, the existing tests, and the application's
startup instructions. Treat the current implementation as evidence, not as a
complete specification: verify that visible actions actually perform the
operation they claim to perform, including after data is saved and reloaded.

## Required outcome

Your starting point is **S3: SDK-style .NET 10 with production C#**. Language, project-style and framework migration are already complete. Complete the quality and workflow requirements below without repeating those migrations. No migration utility or VB converter is requested.

1. Keep the existing SDK-style C# .NET 10 solution buildable and preserve its
   supported target frameworks. No language or project migration is required.
   Existing VB test projects may remain VB; all test targets must stay compatible
   with the solution. Do not introduce a second production VB implementation.
2. Keep the original main window, calendar, time list, task panel, login,
   settings, and maintenance screens usable. Trace manual booking, editing,
   interruption/resumption, checkout, task completion, selection, and
   persistence across their real boundaries. Correct inconsistencies you find;
   do not replace real SQL services with sample data or omit awkward features.
3. Establish a coherent WPF presentation boundary. ViewModels should expose
   observable state and commands rather than require concrete controls.
   Views may retain genuinely visual responsibilities. Reuse existing sound
   abstractions instead of enforcing a wholesale rewrite or a particular
   framework.
4. Preserve the production time collection's identity, notification contracts,
   booking-order behavior, neighboring entries, and duration recalculation.
   A copied list or an ordinary observable collection is not an equivalent
   replacement merely because the UI still displays rows.
5. Use English for source comments and XML documentation. Preserve useful
   explanations of invariants and rewrite confusing handover notes accurately;
   deleting documentation is not a translation. Resolve or clarify TODOs whose
   assumptions are invalid.
6. Developers were specifically asked to provide an LLM/agent-friendly
   localization system based on `Microsoft.Extensions.Localization`. Use
   neutral/default English `.resx` resources plus German, Dutch, and Spanish
   resource versions with matching keys, real translated content, and normal
   culture fallback. Prove feasibility on exactly these UI surfaces: **Login
   Experience**, **Main time-collection UI**, **add/edit booking dialog**, and
   **Project Main Data dialog**. Resource keys must actually supply displayed
   strings on every named surface; merely adding files or unused keys does not
   count. Add a runtime language selector in **Options** and connect it to the
   application's localization/culture behavior without requiring a restart.
7. Make the calendar and all Main Data screens readable and consistent in
   dark, light, and high-contrast environments. Cover normal, selected, hovered,
   inactive, focused, and disabled states, not just the initial window.
   Centralize reusable brushes/styles and remove conflicting per-control or
   initialization-time overrides where appropriate.
8. Use **Main Data**, not **Master Data**, in English UI text and application
   naming. Keep persisted database identifiers and compatibility boundaries
   stable where renaming them would change the storage contract.
9. Similar applications will follow this handover. Consider whether repeatable
   quality changes can be made reproducible so later projects need less repeated
   analysis and effort. Explain the trade-off and leave evidence that the
   approach works. This is a design consideration, not a requirement to build
   a migration utility. Distinguish mechanical changes from architectural judgment.

## Boundaries

- **Keep EF6 and the existing SQL Server data model. An EF Core conversion is
  explicitly out of scope.**
- Preserve intentional evaluator custody: do not edit, copy into the product,
  disable, or special-case private assessor/evaluator artifacts or diagnostics.
- Do not regenerate, drop, or overwrite an existing database to make tests
  pass. Use isolated fixtures or rolled-back transactions for verification.
- Preserve credential handling and the forced initial-password-change flow.
  Never commit passwords or connection secrets.
- Do not change tests to accept incorrect behavior, hide errors behind silent
  fallback, or remove features because their implementation is inconvenient.
- Avoid unrelated framework upgrades. Choose migration tooling and MVVM
  libraries only where they serve the outcome.

## Delivery

Provide working code and resource files,
focused regression coverage, and concise build/run instructions. Demonstrate
representative workflow and runtime culture/theme checks, including all four
localization surfaces and a clean build from
a fresh checkout. Summarize the substantive corrections, explain any remaining
limitations, and separate verified results from assumptions.
