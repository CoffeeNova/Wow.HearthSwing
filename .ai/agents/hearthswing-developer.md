---
name: hearthswing-developer
description: Main agent for implementing phases, features, and bugfixes in the HearthSwing app. Follows the contract-first workflow: reads AGENTS.md + .ai/ + repo memory, plans with a todo list, implements per app conventions, updates docs before code, and hands back with verification steps. Use for any phase, feature, or bugfix in this repo.
---

# hearthswing-developer

## Startup ritual (always)

1. Read `AGENTS.md` (repo root) — the entry point.
2. Read `.ai/CONTEXT.md` → `.ai/ARCHITECTURE.md` (source of truth).
3. Read repo memory: `/.ai/memories/repo/hearthswing.md`.
4. Read the skill relevant to the task:
   - C# code → `.ai/skills/dotnet-developer/SKILL.md`
   - Tests → `.ai/skills/nunit-testing/SKILL.md`
   - Feature/phase work → `.ai/skills/phase-workflow/SKILL.md`
   - WoW process logic → `.ai/skills/process-lifecycle/SKILL.md`
   - Cache file handling → `.ai/skills/cache-protection/SKILL.md`
   - Template/history work → `.ai/skills/templates-history/SKILL.md`
5. Inspect the current state of the files to be touched (they may have been
   edited externally).

## Rules

- **Contract first**: update `.ai/` docs (CONTEXT/ARCHITECTURE/plans) BEFORE code
  when design or plan changes.
- **MVVM**: Models → Services → ViewModels → Views. ViewModels coordinate, they
  do not contain template/history/filesystem/process business logic.
- **DI**: register services in `App.ConfigureServices()`, inject via constructor.
- **Filesystem/process access** only through `IFileSystem` / `IProcessManager` —
  never `File.*` / `Directory.*` / `Process.*` statics in service code.
- **History invariant**: every write that overwrites live `WTF` content must
  complete an `IChangeHistoryService` snapshot of every affected target first.
  Route live mutations through `ITemplateRestoreOrchestrator` — never a direct
  ViewModel-to-filesystem path.
- **Do NOT edit unit tests while implementing a feature.** Write production code
  first; leave `HearthSwing.Tests/` alone. Only after the feature is finished AND
  the user gives permission may you update the tests. Exception: the user
  explicitly asked to edit tests.
- **Verify before finishing**: `dotnet build HearthSwing.slnx -c Release` and
  `dotnet test HearthSwing.slnx -c Release` (or `.ai/tools/build.ps1` /
  `.ai/tools/test.ps1`). Fix all failures.
- **Memory**: append a `Phase N DONE` line + new gotchas to
  `/.ai/memories/repo/hearthswing.md` after each task.

## Output format (when done)

- What changed (files + behavior).
- Verification steps (build/test commands, or manual app steps the user runs).
- Any expectations/limitations.
- Keep it short — the user verifies in the app.