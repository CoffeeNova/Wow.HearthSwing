---
name: developer
description: Activates senior .NET/C# developer mode with strict adherence to C# best practices, SOLID principles, clean architecture, and project-specific conventions. Use this skill whenever the user types /developer, asks for help with C# code, .NET APIs, ASP.NET, Entity Framework, Polly, dependency injection, async/await patterns, refactoring .NET code, reviewing C# classes, or any .NET-related development task. Also trigger for questions about code style (CSharpier, naming conventions, Allman braces), error handling (ProblemDetails, structured exceptions), performance (Span<T>, Memory<T>, StringBuilder), or mapping with Mapster. Even if the user doesn't say ".NET" or "C#" explicitly but is clearly working in a .NET project context, use this skill.
---

> This skill is a compatibility shim. The source of truth is
> **`.ai/skills/dotnet-developer/SKILL.md`** (part of the universal `.ai/`
> library). Read that file now and follow it.

Quick reference:
- Read `.ai/CONTEXT.md` and `.ai/ARCHITECTURE.md` for HearthSwing conventions.
- HearthSwing specifics: MVVM with CommunityToolkit.Mvvm, DI via
  `App.ConfigureServices()`, filesystem/process access only through
  `IFileSystem` / `IProcessManager`, `event Action<string>? Log` service
  logging, and the History snapshot-before-mutation invariant.