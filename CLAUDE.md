# HearthSwing — Agent Instructions

> This file is a thin compatibility pointer. The single source of truth for
> agents is **`AGENTS.md`** at the repo root, which points into the **`.ai/`**
> directory (project context, architecture, skills, agents, prompts, tools).

Start by reading:

1. `AGENTS.md` — the entry point
2. `.ai/CONTEXT.md` — what the app does, conventions, gotchas
3. `.ai/ARCHITECTURE.md` — MVVM, DI, service layer, invariants
4. The relevant skill under `.ai/skills/` for the task

When the design or plan changes, update `.ai/` **first** — it is the contract.