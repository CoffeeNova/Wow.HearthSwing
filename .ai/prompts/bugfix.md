# Prompt: Bugfix — {Bug Title}

> Use this template to report and fix a bug in HearthSwing.

## Bug description

{What happens, what should happen}

## Reproduction steps

1. {Step 1}
2. {Step 2}
3. {Step 3}

## Environment

- .NET version: {version}
- OS: {OS}
- WoW running or closed: {running / closed}
- HearthSwing version: {version}

## Diagnosis

{After investigation: root cause, affected files. Read the relevant skill first:
`.ai/skills/cache-protection/SKILL.md`, `.ai/skills/templates-history/SKILL.md`,
`.ai/skills/process-lifecycle/SKILL.md`, or `.ai/skills/dotnet-developer/SKILL.md`.}

## Fix

{After implementation: what changed, which files. Contract-first — update
`.ai/` docs if the fix changes documented behavior.}

## Verification

- [ ] Build passes: `dotnet build HearthSwing.slnx -c Release`
- [ ] Test added/updated: `dotnet test HearthSwing.slnx -c Release`
- [ ] Manual test steps: {list — including whether WoW must be running/closed}