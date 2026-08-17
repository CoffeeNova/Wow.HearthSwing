---
name: templates-history
description: The template and history domain invariants for HearthSwing — templates as the only transfer mechanism, the snapshot-before-overwrite invariant, apply scopes, running-vs-closed WoW paths, and history storage layout. Use before touching any template capture/apply, history, or orchestration code.
---

# Skill: Templates & History — Domain Invariants

Use when working on template capture/apply, change history, or orchestration.

## Core invariants (non-optional)

1. **Templates are the only transfer mechanism.** Do not reintroduce saved
   accounts, profile switching, or a profile filter. The UI has exactly two
   top-level modes: `Templates` and `History`.
2. **Snapshot before every overwrite.** Every write that overwrites live `WTF`
   content MUST complete an `IChangeHistoryService` snapshot of every affected
   target first. This is enforced by `ITemplateRestoreOrchestrator` — never add
   a ViewModel-to-filesystem path that bypasses it.
3. **Character restore with account-scoped data** needs SEPARATE snapshots of
   the character subtree AND the account subtree — shared account data affects
   every character on that account.
4. **Closed game ⇒ rollback-aware directory replacement; running game ⇒ in-place
   writes.** While WoW is running, never use `IDirectoryReplacer` or folder swap.
5. **History restore is offline-only.** Resolve the live target, snapshot it
   again, restore with `IDirectoryReplacer`. The UI must require WoW closed.

## Storage layout

```
<ProfilesPath>/
├── .templates/<id>/        # template metadata + content
└── .history/<target-key>/  # tar.gz archives + index.json records
```

- `TemplateCatalog` manages `.templates/<id>/`.
- `ChangeHistoryService` manages `.history/<target-key>/` (bounded;
  `MaxHistoryEntriesPerTarget` defaults to 20, configurable in `AppSettings`).

## Apply scopes

- `TemplateApplyScope.Full` — transfers all applicable files.
- `TemplateApplyScope.CacheOnly` — transfers the cache-backed subset only
  (the `Tokenizable` cache files — see the `cache-protection` skill).

## Character templates

Character templates hold a tokenized character tree (donor character/realm
values replaced with target values where supported, via `ITemplateTokenizer`)
plus an optional `Shared/` account-scoped payload. Applying a character template
can opt into applying the shared account settings.

## The orchestration contract

`TemplateRestoreOrchestrator` is the single entry point for any live `WTF`
mutation from a template:

1. Resolve every affected target.
2. Snapshot each target through `IChangeHistoryService` (BEFORE any write).
3. Apply via `TemplateApplyService`:
   - WoW closed → `IDirectoryReplacer.ReplaceDirectory()` (rollback-aware).
   - WoW running → `Unlock -> apply in place -> Lock -> ForceRestore` + prompt
     for `/reload`.
4. Restore history → offline, snapshot-first, `IDirectoryReplacer`.

## Gotchas

- Clear read-only attributes before overwriting live files or deleting staging
  folders (cache-protected files are read-only by design).
- History restore snapshots the current target again so the restore is itself
  recoverable.
- Tests must verify the invariant: `TemplateRestoreOrchestratorTests` checks
  that required snapshots complete before apply, that running-WoW paths avoid
  directory replacement, and that cache protection is re-established after a
  live apply.

## Related docs

- `.ai/ARCHITECTURE.md` — section 3 (Templates & History data flow).
- `.ai/docs/wtf-history-redesign-plan.md`, `.ai/docs/templates-ux-improvement-plan.md`,
  `.ai/docs/character-template-mode-plan.md` — the plans that shaped these invariants.