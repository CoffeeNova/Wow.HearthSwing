---
name: csharpier-formatting
description: Code formatting with CSharpier for the HearthSwing solution — installation, usage, and how it maps to this repo's style rules. Use before committing or when a code review flags formatting.
---

# Skill: CSharpier Code Formatting

Use when formatting C# code in this project. HearthSwing's style rules are
documented in `.ai/CONTEXT.md`; CSharpier is the assumed formatter and should
produce output that conforms to them. It is **not** wired into CI or the build —
run it manually before committing.

## Installation

```powershell
dotnet tool install -g csharpier
```

## Usage

```powershell
# Format all files in a project
csharpier format HearthSwing

# Check if files are formatted (exit code 1 if any file needs formatting)
csharpier check HearthSwing

# Format a single file
csharpier format HearthSwing/Services/CacheProtector.cs
```

## How CSharpier's defaults map to repo rules

CSharpier uses default settings — no `.csharpierrc` file needed:

- Indentation: 4 spaces (matches repo convention).
- Braces: K&R/same line for blocks — but this repo uses file-scoped namespaces
  (`namespace X.Y;`) and Allman braces per `.ai/CONTEXT.md`. CSharpier follows
  the prevailing style in each file; when a file already uses Allman braces,
  keep it consistent and run `csharpier` only where it agrees with the repo style.
- Line endings: LF.
- Trailing commas: when multi-line (matches repo collection-expression style).

## Key behaviors

- CSharpier is opinionated — it does NOT have configuration options for brace
  style, indent size, etc.
- It formats the entire file, not just changed lines.
- The `check` command exits with code 1 if any file is not formatted.

## CI integration (optional)

Not currently part of the HearthSwing workflow. If added to a GitHub Actions
workflow:

```yaml
- name: Check formatting
  run: |
    dotnet tool install -g csharpier
    csharpier check HearthSwing
```

## Style rules the repo enforces beyond CSharpier

Always check `.ai/CONTEXT.md` for the authoritative rules:

- File-scoped namespaces, no `#region`.
- `_camelCase` private fields; `PascalCase` types/constants/XAML keys.
- Collection expressions (`[]`) over `Array.Empty<T>()` / `new List<T>()`.
- Method groups over lambda wrappers; no `async` on never-awaiting methods.
- `string.Empty` instead of `""`.