# HearthSwing — repo memory

Working record for AI agents. Source of truth is `.ai/`; this file mirrors
verified outcomes, decisions, and phase status.

## Status (2026-08)

- **v1.0.** WPF desktop app (.NET 10, `win-x64`) for capturing, applying, and
  recovering WoW Classic Anniversary `WTF` settings.
- Two top-level UI modes only: **Templates** and **History**. Templates are the
  only transfer mechanism — saved accounts / profile switching / profile filters
  were removed and must not be reintroduced.
- Core flows implemented and covered by service tests: template capture/apply
  (closed-game rollback-aware replacement; running-WoW in-place + `/reload`),
  bounded tar.gz change history, cache protection (read-only + backup +
  watcher + timestamp), direct WoW launch, settings auto-detect.
- **Invariant**: every live `WTF` overwrite completes an
  `IChangeHistoryService` snapshot of every affected target first — enforced by
  `TemplateRestoreOrchestrator`.

## Decisions

- Templates are the only transfer mechanism; History records recoverable
  snapshots before templates change live content.
- Directory replacement (`IDirectoryReplacer`) only when WoW is closed; running
  WoW gets targeted in-place writes + cache force-restore + `/reload` prompt.
- History restore is offline-only (UI requires WoW closed).
- Cache protection is layered (read-only lock, in-memory backup,
  `FileSystemWatcher` recovery, timestamp touch); `CacheFilePatterns` is the
  single source of truth for protected files.
- `SwitchingOrchestrator` scope = cache + launch only.
- All filesystem/process/archive I/O behind interfaces (`IFileSystem`,
  `IProcessManager`, `IArchiveService`, `IWtfInspector`) for testability.

## Phases

- v1.0 core — DONE (templates, history, cache protection, launch, settings).
- AI-library rework (2026-08) — DONE: universal `AGENTS.md` entry point; all AI
  docs moved into `.ai/` (CONTEXT, ARCHITECTURE, skills, agents, prompts, tools,
  docs, memories); `docs/` → `.ai/docs/`; `CLAUDE.md`/`.github/copilot-instructions.md`
  are now thin pointers; `.claude/skills/developer` points to
  `.ai/skills/dotnet-developer/SKILL.md`. Adapted skills/tools from luahelper-mcp
  (nunit-testing, phase-workflow, process-lifecycle, csharpier-formatting,
  dotnet-architect agent, build/test/publish tools).

## To verify / known limitations

- Manual UI verification happens on the user's machine (real WoW client not in
  this repo; `content/` holds fixture data only).
