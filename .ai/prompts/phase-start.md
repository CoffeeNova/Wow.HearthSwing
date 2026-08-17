# Prompt: Start Phase {N} — {Phase Name}

> Use this template to begin a new phase in a fresh session.

## Context

- **Phase:** {N} — {Phase Name}
- **Plan:** `.ai/docs/{plan}.md` (section "Phase {N}")
- **Architecture:** `.ai/ARCHITECTURE.md` (relevant sections)
- **Conventions:** `.ai/CONTEXT.md`
- **Repo memory:** `.ai/memories/repo/hearthswing.md`
- **Previous phase:** {Phase N-1} completed — {brief summary}

## Tasks

{List the steps from the plan}

## Definition of Done

{List the DoD items from the plan}

## Workflow

1. Read the plan and architecture.
2. Read the relevant skill(s):
   - C# code → `.ai/skills/dotnet-developer/SKILL.md`
   - Tests → `.ai/skills/nunit-testing/SKILL.md`
   - Feature work → `.ai/skills/phase-workflow/SKILL.md`
3. Create a todo list.
4. Implement step by step, contract-first (update `.ai/` before code when
   behavior changes).
5. Build after each step: `dotnet build HearthSwing.slnx -c Release`
   (or `.ai/tools/build.ps1`).
6. Test before completion: `dotnet test HearthSwing.slnx -c Release`
   (or `.ai/tools/test.ps1`).
7. Update repo memory and write a brief report (what changed, verification
   steps, expectations/limitations).

Begin by reading the contract files and showing a short plan, then implement.