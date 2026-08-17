---
name: nunit-testing
description: NUnit testing patterns and conventions for the HearthSwing test project. Use before writing, fixing, or running any tests in HearthSwing.Tests — Shouldly assertions, NSubstitute + AutoFixture mocking, Arrange/Act/Assert layout, and the specific service-test conventions of this repo.
---

# Skill: NUnit Testing — Patterns & Conventions

Use when writing, fixing, or running tests in `HearthSwing.Tests/`.

## Test project layout

- `HearthSwing.Tests/` mirrors the source project folders (`Services/`,
  `ViewModels/`).
- Test classes are named `{ClassUnderTest}Tests` (e.g.
  `TemplateRestoreOrchestratorTests`, `ChangeHistoryServiceTests`,
  `CacheProtectorTests`).
- Unit tests are isolated: **no real filesystem, no real processes, no real
  archive I/O**. `IFileSystem`, `IProcessManager`, `IArchiveService`,
  `IWtfInspector`, and the SUT's other dependencies are NSubstitute substitutes.

## Assertions with Shouldly

All assertions use [Shouldly](https://docs.shouldly.org/) (4.3.0) — never
`Assert.That`/`Assert.AreEqual` constraint syntax. One `using Shouldly;` per file.

| NUnit classic | Shouldly |
|---|---|
| `Assert.That(a, Is.EqualTo(b))` | `a.ShouldBe(b)` |
| `Assert.That(a, Is.Not.EqualTo(b))` | `a.ShouldNotBe(b)` |
| `Assert.That(x, Is.Null)` | `x.ShouldBeNull()` |
| `Assert.That(x, Is.Not.Null)` | `x.ShouldNotBeNull()` |
| `Assert.That(x, Is.True/False)` | `x.ShouldBeTrue()/ShouldBeFalse()` |
| `Assert.That(coll, Is.Empty)` | `coll.ShouldBeEmpty()` |
| `Assert.That(coll, Is.Not.Empty)` | `coll.ShouldNotBeEmpty()` |
| `Assert.That(list, Has.Count.EqualTo(n))` | `list.Count.ShouldBe(n)` |
| `Assert.That(text, Does.Contain(s))` | `text.ShouldContain(s, Case.Sensitive)` |
| `Assert.That(text, Does.Not.Contain(s))` | `text.ShouldNotContain(s, Case.Sensitive)` |
| `Assert.That(text, Does.Contain(s).IgnoreCase)` | `text.ShouldContain(s, Case.Insensitive)` |
| `Assert.That(result, Does.Match(pattern))` | `result.ShouldMatch(pattern)` |
| `Assert.That(dict, Does.ContainKey(k))` | `dict.ShouldContainKey(k)` |
| `Assert.That(a, Is.SameAs(b))` | `a.ShouldBeSameAs(b)` |
| `Assert.CatchAsync<T>(async () => ...)` | `await Should.ThrowAsync<T>(async () => ...)` — MUST be awaited |

Notes:
- Shouldly 4.x `ShouldContain`/`ShouldStartWith`/`ShouldEndWith` default to
  case-insensitive — always pass `Case.Sensitive`/`Case.Insensitive` explicitly.
- `ShouldBe` on collections is order-sensitive and element-wise; `ShouldHaveCount`
  is v5-only — use `Count.ShouldBe(n)` on 4.x.
- `Should.ThrowAsync<T>` catches the exact type and derived types (like NUnit's `CatchAsync`).

## Mocking with NSubstitute + AutoFixture

- **NSubstitute 5.3.0** — behavior stubbing and verification.
- **AutoFixture 4.18.1** (+ `AutoFixture.AutoNSubstitute`) — creates substitutes
  automatically for interface dependencies.

Standard HearthSwing pattern: `_fixture.Freeze<T>()` in `[SetUp]`, SUT
constructed in `[SetUp]` with injected dependencies, arrangement in the test
method.

```csharp
using AutoFixture;
using AutoFixture.AutoNSubstitute;
using NSubstitute;

private IFixture _fixture = null!;
private IFileSystem _fileSystem = null!;
private IArchiveService _archive = null!;
private ChangeHistoryService _sut = null!;

[SetUp]
public void SetUp()
{
    _fixture = new Fixture().Customize(new AutoNSubstituteCustomization());
    _fileSystem = _fixture.Freeze<IFileSystem>();
    _archive = _fixture.Freeze<IArchiveService>();

    _fileSystem.DirectoryExists(Arg.Any<string>()).Returns(false);
    _fileSystem.GetFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>()).Returns([]);

    _sut = new ChangeHistoryService(_archive, _fileSystem, /* ... */);
}
```

Configure members with NSubstitute, e.g.:

```csharp
_fileSystem.FileExists(Arg.Any<string>()).Returns(true);
_fileSystem.ReadAllBytesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new byte[] { 1, 2, 3 });
```

Verification (MUST `await` async members):

```csharp
await _fileSystem.DidNotReceive().DeleteFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
```

## AAA pattern

```csharp
[Test]
public async Task RestoreAsync_TargetMissing_Throws()
{
    // Arrange
    _fileSystem.DirectoryExists(Arg.Any<string>()).Returns(false);

    // Act
    var act = async () => await _sut.RestoreAsync(entry);

    // Assert
    await act.ShouldThrowAsync<InvalidOperationException>();
}
```

## Meaningful assertions

**Bad:** `files.ShouldNotBeEmpty();`

**Good:**
```csharp
files.ShouldNotBeEmpty();
files[0].ShouldContain("SavedVariables", Case.Insensitive);
```

## Tests that must exist for template restore

`TemplateRestoreOrchestratorTests` must verify:
1. Required History snapshots complete **before** apply (`Received(1)` on
   `SnapshotAsync` before the apply call).
2. Running-WoW paths **avoid** `IDirectoryReplacer` (did-not-receive).
3. Cache protection is re-established after a live apply (`Lock`/`ForceRestore`
   called after the apply).

## Running tests

```powershell
# Full suite
dotnet test HearthSwing.slnx -c Release

# Single test class
dotnet test HearthSwing.slnx -c Release --filter "FullyQualifiedName~CacheProtectorTests"

# Or use the tooling wrapper
.\.ai\tools\test.ps1 -Filter "FullyQualifiedName~ChangeHistoryServiceTests"
```

- The project has `<Using Include="NUnit.Framework" />` global usings — do not
  add `using NUnit.Framework;` to test files.
- If shared assembly-level setup is ever needed, add a `GlobalFixture` (NUnit
  `[SetUpFixture]`) in the test assembly root.