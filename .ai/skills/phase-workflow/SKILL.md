---
name: phase-workflow
description: The end-to-end workflow for implementing a phase or feature of HearthSwing. Use at the start of every phase or feature: contract-first, todo list, implement, verify (build + tests), document, hand back.
---

# Phase workflow

## Golden rule

**.ai/ is the contract.** When the design or plan changes, update
`.ai/CONTEXT.md`, `.ai/ARCHITECTURE.md`, and the plan docs in `.ai/docs/`
FIRST, then code. Never let code drift from the docs.

## The loop (repeat for every phase/feature)

1. **Read the contract**: `AGENTS.md` → `.ai/CONTEXT.md` → `.ai/ARCHITECTURE.md`.
2. **Read repo memory**: `/.ai/memories/repo/hearthswing.md` — it carries
   verified history; don't re-learn what's there.
3. **Check current state**: read the files you'll touch; note any drift from the
   docs (the user or tooling may have edited them).
4. **Create a todo list** for the phase (todo tool) — one item per deliverable,
   mark in-progress/completed as you go.
5. **Update the contract FIRST** if the phase changes behavior (rule 1).
6. **Implement** per the app's conventions (`.ai/CONTEXT.md`):
   - MVVM with CommunityToolkit.Mvvm; Models → Services → ViewModels → Views.
   - Services: `public sealed class` behind an interface, registered in
     `App.ConfigureServices()`, logging via `event Action<string>? Log`.
   - Filesystem/process via `IFileSystem` / `IProcessManager` only.
   - **Live `WTF` mutations go through `ITemplateRestoreOrchestrator` with all
     required History snapshots first** — never a direct ViewModel-to-filesystem path.
7. **Verify**: `dotnet build HearthSwing.slnx -c Release` then
   `dotnet test HearthSwing.slnx -c Release` (or use
   `.ai/tools/build.ps1` / `.ai/tools/test.ps1`). Fix all failures.
8. **Update memory**: append a `Phase N DONE` line to
   `/.ai/memories/repo/hearthswing.md`; add new gotchas to it.
9. **Hand back to the user** with:
   - what changed (files + behavior);
   - the Definition of Done as concrete verification steps;
   - known expectations/limitations.
   Keep it short; the user verifies in the app and reports back.

## Feature conventions (see `.ai/CONTEXT.md` → "Adding new functionality")

1. **Model**: logic-free `sealed class`, record, or enum in `Models/`.
2. **Service**: focused `public sealed class` behind an interface in `Services/`,
   registered in `App.ConfigureServices()`.
3. **ViewModel/View**: `[ObservableProperty]` fields + `[RelayCommand]` methods in
   `MainViewModel`, bound in `MainWindow.xaml` using existing dark-theme styles,
   overlays, confirmation dialogs, and toast patterns.
4. **Tests**: matching coverage in `HearthSwing.Tests/` (NUnit, AutoFixture,
   NSubstitute, Shouldly, AAA) — read the `nunit-testing` skill first.

## Testing etiquette

- **Do NOT touch existing unit tests while implementing a feature** unless they
  directly block you. Write/fix production code first.
- **Only after the feature is finished AND the user gives permission** may you
  update the tests (add coverage for the new behavior, fix tests broken by the
  change). Exception: the user explicitly asked to edit tests.
- The user verifies the WPF app manually (the sandbox cannot click the UI).
- Give exact verification steps and expected output.

## Definition of Done per phase

Always check the phase's Definition of Done in the plan docs under
`.ai/docs/`. State clearly what the user must verify manually.