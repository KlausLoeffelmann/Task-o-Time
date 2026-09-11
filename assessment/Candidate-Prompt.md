# Task-o-Time: maintainable desktop delivery

Task-o-Time is a Windows WPF application backed by SQL Server and Entity
Framework 6. It grew through a handover between developers, and conventions
were not applied consistently. We need a maintainable C# implementation
without losing the working desktop experience or existing data.

Start by inspecting the solution, the existing tests, and the application's
startup instructions. Treat the current implementation as evidence, not as a
complete specification: verify that visible actions actually perform the
operation they claim to perform, including after data is saved and reloaded.

## Required outcome

1. Migrate production Visual Basic code to C#, including the presentation and
   time-tracking libraries. Preserve public behavior and keep the solution
   buildable. Existing test code need not change language merely for uniformity.
   Do not retain a second VB implementation as the application's fallback.
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
6. Implement the conventional .NET resource localization model: neutral English
   `.resx` resources, culture-specific German and Dutch resources, strongly
   typed accessors backed by `ResourceManager`, and normal culture fallback.
   Connect these resources to actual UI captions, dialogs, validation messages,
   and other presentation text. A custom string dictionary or unused resource
   file is not sufficient. A restart-based culture selection is acceptable.
7. Make the calendar and all Main Data screens readable and consistent in
   dark, light, and high-contrast environments. Cover normal, selected, hovered,
   inactive, focused, and disabled states, not just the initial window.
   Centralize reusable brushes/styles and remove conflicting per-control or
   initialization-time overrides where appropriate.
8. Use **Main Data**, not **Master Data**, in English UI text and application
   naming. Keep persisted database identifiers and compatibility boundaries
   stable where renaming them would change the storage contract.
9. Leave a reproducible migration path, not only a finished translation. This
   is the first of several similar applications; repeating file-by-file work
   on every handover would be too expensive. Include a small checked-in
   transformation utility and representative regression examples for the
   mechanical work you automate. It should be rerunnable, report unsupported
   cases clearly, and allow subsequent applications to reuse the investment
   without repeating the same per-file reasoning. Distinguish mechanical
   conversion from changes that require architectural judgment.

## Boundaries

- **Keep EF6 and the existing SQL Server data model. An EF Core conversion is
  explicitly out of scope.**
- Do not regenerate, drop, or overwrite an existing database to make tests
  pass. Use isolated fixtures or rolled-back transactions for verification.
- Preserve credential handling and the forced initial-password-change flow.
  Never commit passwords or connection secrets.
- Do not change tests to accept incorrect behavior, hide errors behind silent
  fallback, or remove features because their implementation is inconvenient.
- Avoid unrelated framework upgrades. Choose migration tooling and MVVM
  libraries only where they serve the outcome.

## Delivery

Provide working code, resource files, the reusable transformation utility,
focused regression coverage, and concise build/run instructions. Demonstrate
representative workflow and culture/theme checks, including a clean build from
a fresh checkout. Summarize the substantive corrections, explain any remaining
limitations, and separate verified results from assumptions.
