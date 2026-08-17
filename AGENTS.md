# HearthSwing — Agent Instructions

This file is the **entry point** for AI agents working in this repository.

Everything an agent needs — project context, architecture, skills, agents,
prompts, and tools — lives in the **`.ai/`** directory. It is the single source
of truth.

## Reading order

1. `.ai/CONTEXT.md` — what the app does, code conventions, key mechanics & gotchas, testing, build & publish
2. `.ai/ARCHITECTURE.md` — MVVM layering, DI, service layer, template/history/cache invariants
3. `.ai/skills/dotnet-developer/SKILL.md` — **before writing any C# code**: senior .NET practices + project conventions
4. `.ai/skills/templates-history/SKILL.md` — **before touching templates, history, or any live `WTF` mutation**: the snapshot-before-overwrite invariant and running-vs-closed WoW paths
5. `.ai/skills/cache-protection/SKILL.md` — **before touching cache files or a live apply**: the four protection layers and the `Unlock -> apply -> Lock -> ForceRestore` sequence
6. `.ai/skills/nunit-testing/SKILL.md` — **before writing or running tests**: Shouldly, NSubstitute + AutoFixture, AAA
7. `.ai/skills/phase-workflow/SKILL.md` — before starting any phase/feature

## Rules

- Follow everything in `.ai/`. It is the source of truth.
- When the design or plan changes, update the files in `.ai/` **first** — they are the contract.
- `README.md` at the repo root is for **human users** — do not treat it as technical documentation.
- All documentation and code in this workspace must be written in English.
- **Contract-first, memory-last**: update `.ai/` before code; record outcomes in `.ai/memories/repo/hearthswing.md` after.
- **History invariant**: every write that overwrites live `WTF` content must complete an `IChangeHistoryService` snapshot of every affected target first. This is non-optional.
- Filesystem and process access go through `IFileSystem` and `IProcessManager` — never call `File.*`/`Directory.*`/`Process.*` statics in service code.
- `MessageBox.Show()` is never called from a ViewModel directly — use the `Action`/`IMessageDialog` abstraction so the ViewModel stays testable.

## Skills (`.ai/skills/`)

| Skill | When to use |
|---|---|
| `dotnet-developer` | Any C# code, .NET/WPF APIs, DI, async/await, refactoring, code review |
| `templates-history` | Template capture/apply, change history, orchestration, any live `WTF` mutation |
| `cache-protection` | `CacheProtector`, cache file handling, live apply sequences |
| `process-lifecycle` | `ProcessMonitor`, `SystemProcessManager`, WoW launch/exit logic |
| `nunit-testing` | Writing/fixing/running tests: Shouldly, NSubstitute + AutoFixture, AAA |
| `phase-workflow` | Starting a phase/feature: contract-first, todo, implement, verify, document, hand back |
| `csharpier-formatting` | Formatting C# with CSharpier (optional tooling, not wired into CI) |

## Agents (`.ai/agents/`)

- `hearthswing-developer` — main agent for any phase/feature/bugfix (startup ritual + rules + output format).
- `dotnet-architect` — architecture review subagent: analyzes design decisions, validates against `ARCHITECTURE.md` and the plan docs, suggests improvements.

## Prompts (`.ai/prompts/`)

- `phase-start.md` — template to begin a phase in a fresh session (context + tasks + DoD + workflow).
- `bugfix.md` — structured bugfix workflow (reproduce → diagnose → fix → verify).

## Tools (`.ai/tools/`)

- `build.ps1` — build the solution (`HearthSwing.slnx`, Debug/Release).
- `test.ps1` — run the test suite (optional NUnit `-Filter`).
- `publish.ps1` — single-file self-contained publish of the WPF app.

## Development plans (`.ai/docs/`)

Plans and implementation prompts that shaped the app:
`wtf-history-redesign-plan.md`, `wtf-history-implementation-prompt.md`,
`templates-ux-improvement-plan.md`, `templates-profiles-implementation-prompt.md`,
`character-template-mode-plan.md`.

## Local development environment

- .NET 10 SDK required (`dotnet --version` → 10.x).
- The WoW client and its `WTF` folder are NOT part of this repo (`content/`
  holds fixture data only). Manual UI verification happens on the user's machine.

## Build & test

```powershell
dotnet build HearthSwing.slnx -c Release     # or .ai/tools/build.ps1
dotnet test HearthSwing.slnx -c Release      # or .ai/tools/test.ps1
dotnet publish HearthSwing/HearthSwing.csproj -c Release   # or .ai/tools/publish.ps1
```

## Why this repo looks the way it does

The `.ai/` library is a lean, adapted port of the AI-documentation pattern
proven in sibling projects (BloomBuddy, luahelper-mcp): a thin `AGENTS.md` entry
point, a `CONTEXT.md`/`ARCHITECTURE.md` contract pair, and skills/agents/
prompts/tools organized so any agent can work here with the same expectations.
Keep the structure consistent when you extend it.
