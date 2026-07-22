# План: режим «Шаблоны персонажей» (Character Template Mode)

Документ описывает добавление нового глобального режима работы HearthSwing —
кросс-аккаунтных обезличенных шаблонов персонажей — с сохранением текущего
аккаунтного режима без изменений.

## 1. Цель и суть фичи

Пользователь играет одним классом (напр. варлок) на нескольких аккаунтах, реалмах
и персонажах. Он хочет один **обезличенный шаблон** («Warlock - TBC»), который:

- создаётся из существующего персонажа-донора;
- «отвязывается» от имени персонажа и названия реалма (деперсонализация);
- переносится на любого другого персонажа (другой аккаунт / реалм / имя),
  подставляя имя и реалм цели во всех местах, где они встречаются — в путях папок
  и **внутри содержимого файлов** (макросы, SavedVariables, cache и т.п.).

Итог: единые настройки (кейбинды, макросы, edit-mode/раскладка, настройки аддонов)
синхронизируются между персонажами и мирами.

## 2. Зафиксированные решения

| Тема | Решение |
|---|---|
| UI | Верхний переключатель режимов «Аккаунты \| Шаблоны» в главном окне (старый экран уезжает под вкладку «Аккаунты» без изменений поведения) |
| Деперсонализация | Токенизация при захвате: исходные имя/реалм заменяются на `{{CHAR}}` / `{{REALM}}` |
| Что обезличиваем | Имя персонажа **и** название реалма |
| Где заменяем содержимое | Известный allowlist текстовых файлов + все `*.lua` в SavedVariables на уровне персонажа **и** аккаунта |
| Объём шаблона | Настройки аккаунта (файлы верхнего уровня `Account/<ACC>/` + `SavedVariables/`) **+** папка одного персонажа `<REALM>/<CHAR>/` |
| Хранение | `<ProfilesPath>/.templates/<templateId>/` |
| Версионирование | Да, `.tar.gz` архивы, аналогично аккаунтам |

## 3. Ключевое отличие от текущего кода

Сейчас весь код работает **на уровне аккаунта** и копирует файлы **побайтово**
(`CopyFile`, `SequenceEqual`) — содержимое WoW-файлов никогда не читается и не
переписывается. Новая фича вводит две принципиально новые способности:

1. **Гранулярность на уровне персонажа.**
2. **Подмена имени/реалма внутри содержимого текстовых файлов** (токенизация).

Поэтому режим «Шаблоны» реализуется **отдельной веткой сервисов**, а старые
сервисы (`AccountSnapshotSaveService`, `AccountSwitchService`, `SavedAccountCatalog`,
`ProfileVersionService`, `SwitchingOrchestrator`) **не изменяются**.

## 4. Переиспользование существующего кода

| Существующее | Как переиспользуем |
|---|---|
| `IFileSystem` | Всё файловое I/O нового кода идёт только через него (тестируемость) |
| `IWtfInspector` | Уже отдаёт дерево `WowInstallation → WowAccount → WowRealm → WowCharacter` — источник для выбора персонажа-донора и цели |
| `IAccountSnapshotLayout` | `CollectAccountSettingsRelativePaths` и `CollectCharacterRelativePaths` — готовая классификация «настройки аккаунта» vs «файлы персонажа». Переиспользуем как есть |
| `IArchiveService` (tar.gz) | Версионирование шаблонов |
| Паттерн rollback-копирования | Выносим в новый общий хелпер `IDirectoryReplacer` (старые сервисы не трогаем; они продолжают использовать свои приватные копии) |
| `.`-префикс игнорируется при обходе аккаунтов (`SavedAccountCatalog`) | `.templates/` и `.template-versions/` не попадут в перечисление аккаунтов автоматически |
| `IProcessMonitor` | Блокировка применения шаблона, пока запущен WoW (как при switch) |
| `IDialogService` | Подтверждения и предупреждения (перезапись настроек аккаунта, `/reload`) |
| `ProfileVersionService` | Служит эталоном; для шаблонов создаём параллельный `TemplateVersionService` (без изменения существующего) |

## 5. Хранение на диске

```
<ProfilesPath>/
  .templates/
    <templateId>/
      template.json                         → TemplateMetadata
      Account/                              → токенизированные настройки аккаунта
        <файлы верхнего уровня>             (top-level Account/<ACC>/*)
        SavedVariables/*.lua                (токенизированы)
      Character/
        __REALM__/__CHAR__/                 → токенизированная папка персонажа
          SavedVariables/*.lua
          macros-cache.txt, ... (токенизированы)
  .template-versions/
    <templateId>/<yyyyMMdd_HHmmss>.tar.gz   → версии (как у аккаунтов)
```

- Токены в **именах папок**: `__CHAR__`, `__REALM__` (filesystem-safe).
- Токены в **содержимом файлов**: `{{CHAR}}`, `{{REALM}}`.
- `templateId` генерируется из имени шаблона по тем же правилам санитизации, что и
  `savedAccountId` в `SavedAccountCatalog` (инвалидные символы → `_`, пробелы → `-`,
  коллизии → суффикс `-2`, `-3`, ...).

## 6. Новые модели (`Models/Templates/`)

`TemplateMetadata` (сохраняется в `template.json`):

- `string Id` (required init)
- `string Name` (required init) — «Warlock - TBC»
- `string SourceAccountName` (required init) — провенанс
- `string SourceRealmName` (required init)
- `string SourceCharacterName` (required init)
- `DateTimeOffset CreatedAtUtc`
- `DateTimeOffset? LastUpdatedUtc`
- `int SchemaVersion` — на будущее (миграции формата токенов)

`TemplateSummary` (in-memory проекция, не сохраняется):

- `string Id`, `string Name`, `string RootPath`
- `string SourceAccountName`, `string SourceRealmName`, `string SourceCharacterName`
- `DateTimeOffset CreatedAtUtc`, `DateTimeOffset? LastUpdatedUtc`
- `string DisplayName => Name` (computed)

`TemplateApplyOptions`:

- `bool IncludeAccountSettings` — применять ли настройки уровня аккаунта (по умолчанию **false**, см. риск в §11)
- `bool CreateVersionBeforeApply` — снять версию цели перед применением

Все модели — `public sealed class`, `required` + `init`, файлово-scoped namespace
`HearthSwing.Models.Templates`.

## 7. Новые сервисы (`Services/`)

Все — `public sealed class`, реализуют интерфейс, DI-регистрация singleton в
`App.xaml.cs ConfigureServices()`. Логирование через `event Action<string>? Log`
там, где сервис должен сообщать о ходе работы.

### 7.1 `ITemplateTokenizer` / `TemplateTokenizer`

Ядро деперсонализации. Без файлового I/O — чистые строковые операции.

- `string Tokenize(string content, string charName, string realmName)`
- `string Expand(string content, string charName, string realmName)`
- Порядок замен (важно для минимизации ложных совпадений):
  1. составной ключ `"<char> - <realm>"` → `"{{CHAR}} - {{REALM}}"`;
  2. затем `realmName` → `{{REALM}}`;
  3. затем `charName` → `{{CHAR}}`.
- v1: ordinal (регистрозависимо), т.к. имена папок соответствуют ключам SV.
- Набор пар замен вынести в структуру (список origin-строк), чтобы позже добавить
  варианты реалма (с пробелами / без) без переписывания логики.

### 7.2 `ITemplateFileClassifier` / `TemplateFileClassifier`

Определяет, токенизировать файл или копировать байтами.

- `bool ShouldTokenize(string relativePath)` — true для:
  - любой `*.lua` (SavedVariables на обоих уровнях);
  - allowlist cache-файлов: `macros-cache.txt`, `bindings-cache.wtf`,
    `config-cache.wtf`, `chat-cache.txt`, `chat-frontend-cache.txt`,
    `edit-mode-cache-account.txt`, `edit-mode-cache-character.txt`,
    `tts-cache-account.txt`, `tts-cache-character.txt`,
    `flagged-cache-account.txt`, `layout-local.txt`.
- Явно **исключить** `cache.md5` из токенизации (это контрольная сумма; WoW
  пересчитывает её сам — копируем как есть либо не включаем).
- Константы паттернов держать рядом (как `CachePatterns` в `CacheProtector`).

### 7.3 `IDirectoryReplacer` / `DirectoryReplacer`

Общий хелпер rollback-копирования (новый; старые сервисы не трогаем).

- `void ReplaceDirectory(string sourcePath, string destinationPath)` — копия
  назначения в `.rollback-<guid>`, снять read-only, удалить, скопировать источник;
  при ошибке — откат; `finally` — удалить rollback-папку. Повторяет проверенный
  паттерн из `AccountSnapshotSaveService`.
- `void CopyDirectory(...)`, `void ClearReadOnlyAttributes(...)` — вспомогательные.

### 7.4 `ITemplateCatalog` / `TemplateCatalog`

Аналог `SavedAccountCatalog`, но для `.templates/`.

- `List<TemplateSummary> DiscoverTemplates()` — обход `.templates/*/`, чтение
  `template.json`, сортировка по `Name` (OrdinalIgnoreCase).
- `TemplateSummary? GetById(string templateId)`
- `TemplateSummary Create(string name, ...)` — создать папку + `template.json`.
- `void UpdateLastUpdated(string templateId, DateTimeOffset)`
- `void Rename(string templateId, string newName)`
- `void Delete(string templateId)` — удалить папку шаблона и его `.template-versions/<id>`.
- `StorageRoot = Path.Combine(AppSettings.ProfilesPath, ".templates")`.

### 7.5 `ITemplateCaptureService` / `TemplateCaptureService`

Создание шаблона из живого персонажа.

- `TemplateSummary CreateTemplate(WowCharacter source, string templateName)`:
  1. `Create` записи в каталоге;
  2. собрать настройки аккаунта источника через
     `IAccountSnapshotLayout.CollectAccountSettingsRelativePaths(Account/<srcAcc>)`
     → записать в `<template>/Account/`, токенизируя текстовые файлы;
  3. собрать файлы персонажа через `CollectCharacterRelativePaths(<srcChar>)`
     → записать в `<template>/Character/__REALM__/__CHAR__/`, токенизируя;
  4. `UpdateLastUpdated`.
- Токенизация: для каждого файла — если `ShouldTokenize` → прочитать как UTF-8,
  `Tokenizer.Tokenize(text, source.CharacterName, source.RealmName)`, записать;
  иначе `CopyFile` побайтово.
- `event Action<string>? Log`.

### 7.6 `ITemplateApplyService` / `TemplateApplyService`

Применение шаблона на целевого персонажа.

- `void ApplyTemplate(TemplateSummary template, WowCharacter target, TemplateApplyOptions options)`:
  1. подготовить временную «развёрнутую» копию: пройти `<template>/Character/...`,
     для текстовых файлов `Tokenizer.Expand(text, target.CharacterName, target.RealmName)`,
     имена папок-токенов заменить на реальные `<targetRealm>/<targetChar>`;
  2. `DirectoryReplacer.ReplaceDirectory(expandedChar, WTF/Account/<tgtAcc>/<tgtRealm>/<tgtChar>)`;
  3. если `options.IncludeAccountSettings` — аналогично развернуть `<template>/Account/`
     и применить в `WTF/Account/<tgtAcc>/` (**с предупреждением**, см. §11);
  4. rollback при ошибке на каждом шаге.
- `event Action<string>? Log`.

### 7.7 `ITemplateVersionService` / `TemplateVersionService`

Параллель `ProfileVersionService`, root = `<ProfilesPath>/.template-versions`.

- `Task CreateVersionAsync(string templateId)` — архив папки шаблона в
  `<root>/<templateId>/<timestamp>.tar.gz`, `PruneVersions(...)`.
- `List<ProfileVersion> GetVersions(string templateId)`
- `Task RestoreVersionAsync(ProfileVersion)`
- `void DeleteVersion(ProfileVersion)`
- `void PruneVersions(string templateId, int maxVersions)`
- Переиспользует модель `ProfileVersion` и `IArchiveService`.
- Триггер: снятие версии шаблона перед его перезаписью (пересоздание/обновление).

## 8. ViewModel

Старый `MainViewModel` расширяется **аддитивно** (существующие свойства/команды
не меняются).

Новый режим (согласовано — вкладки в стиле существующих Visibility-биндингов):

- `public enum AppMode { Accounts, Templates }` (в `HearthSwing.ViewModels` или Models).
- `[ObservableProperty] AppMode _activeMode = AppMode.Accounts;`
  с computed `bool IsAccountsMode`/`bool IsTemplatesMode` (или конвертер) для
  Visibility двух корневых панелей.
- `[RelayCommand] void ShowAccountsMode()` / `ShowTemplatesMode()`.

Состояние шаблонов:

- `ObservableCollection<TemplateSummary> Templates`
- `ObservableCollection<ProfileVersion> TemplateVersions`
- выбор донора/цели: переиспользовать дерево из `_wtfInspector.Inspect(GamePath)`
  (`LiveAccounts` уже строится; добавить проекции реалм/персонаж по аналогии с
  `RealmSaveSelectionViewModel` / `CharacterSaveSelectionViewModel`).

Команды:

- `CreateTemplateAsync()` — выбрать персонажа-донора + ввести имя → `CaptureService`.
- `ApplyTemplateAsync(string templateId)` — выбрать целевого персонажа + опции →
  предупреждение (если `IncludeAccountSettings`) → `ApplyService`; подсказать `/reload`.
- `RenameTemplate(string templateId)`, `DeleteTemplate(string templateId)`.
- `ToggleTemplateVersionHistory(string templateId)`,
  `RestoreTemplateVersionAsync(...)`, `DeleteTemplateVersion(...)`.
- Гарды: `IsBusy`, `IsWowRunning` (применение блокируется при запущенном WoW).

Подписка на `Log` новых сервисов — через `AppendLog` (method group), как у
существующих сервисов.

## 9. View (XAML)

- В `MainWindow.xaml` добавить верхний переключатель режимов (segmented control /
  две кнопки-таба в тёмной теме, стили `ProfileBtn`/`LinkBtn`).
- Обернуть **существующий** контент в панель «Аккаунты» (Visibility ← `IsAccountsMode`)
  — перенос разметки без изменения биндингов/поведения.
- Новая панель «Шаблоны» (Visibility ← `IsTemplatesMode`): список шаблонов
  (`ItemsControl` + `WrapPanel`, как профили), кнопки «Создать шаблон» / «Применить»
  / «История версий» / «Удалить», диалоги выбора донора и цели.
- Переиспользовать существующие ресурсы тёмной темы (`CardBg`, `TextPrimary`,
  `BoolToVis`) и паттерн overlay-панелей для выбора персонажа.

## 10. Тесты (`HearthSwing.Tests/`)

Структура зеркалит источник. NUnit + AutoFixture(AutoNSubstitute) + NSubstitute +
Shouldly, AAA, `Freeze<T>()`, без реального I/O.

- `Services/TemplateTokenizerTests` — round-trip tokenize/expand; составной ключ
  `"Name - Realm"`; реалм с пробелом; отсутствие ложной замены подстроки (док. как
  known limitation); идемпотентность.
- `Services/TemplateFileClassifierTests` — `.lua` да; `cache.md5` нет; allowlist.
- `Services/DirectoryReplacerTests` — успех и rollback при ошибке копирования.
- `Services/TemplateCatalogTests` — discover/create/rename/delete, санитизация id,
  коллизии, игнор `.`-папок.
- `Services/TemplateCaptureServiceTests` — токенизация текстовых, побайтовое
  копирование бинарных, структура `Account/` + `Character/__REALM__/__CHAR__/`.
- `Services/TemplateApplyServiceTests` — развёртывание токенов, размещение по пути
  цели, rollback, поведение `IncludeAccountSettings`.
- `Services/TemplateVersionServiceTests` — create/list/restore/prune под
  `.template-versions`.
- `ViewModels/MainViewModelTests` — новые команды, гарды (`IsWowRunning`),
  переключение `AppMode`, отсутствие регрессий старых команд.

## 11. Риски и краевые случаи

1. **Перезапись настроек аккаунта затрагивает всех персонажей аккаунта.**
   `Account/SavedVariables` — общие для всех персонажей на аккаунте. Применение
   настроек аккаунта из шаблона перезапишет данные других персонажей.
   Меры: `IncludeAccountSettings = false` по умолчанию; явное предупреждение через
   `IDialogService`; авто-версия цели перед применением (`CreateVersionBeforeApply`).
2. **Ложные совпадения при замене.** Имя персонажа/реалма может быть подстрокой
   другого слова. Меры: замена только в allowlist-файлах и `*.lua`; порядок замен
   (составной ключ → реалм → имя); документируем как known limitation v1.
3. **Расхождение имени реалма (папка vs ключ SV, пробелы).** Механизм замен —
   список пар origin-строк, расширяемый вариантами реалма без переписывания.
4. **Кодировка.** WoW SV — UTF-8; читать/писать строго UTF-8 (без BOM).
5. **`cache.md5` устаревает** после токенизации — исключить из токенизации; WoW
   пересчитает; рекомендовать `/reload` после применения.
6. **Применение при запущенном WoW** — блокировать (гард `IsWowRunning`), как switch.
7. **Строгое требование:** старые сервисы и их поведение не меняются; новый код —
   отдельная ветка, использующая только `IFileSystem`/`IArchiveService`/layout.

## 12. Порядок реализации (инкрементально)

1. Модели `Models/Templates/` (`TemplateMetadata`, `TemplateSummary`, `TemplateApplyOptions`).
2. `TemplateTokenizer` + тесты (ядро, максимальный риск — валидируем первым).
3. `TemplateFileClassifier` + тесты.
4. `DirectoryReplacer` + тесты.
5. `TemplateCatalog` + тесты.
6. `TemplateCaptureService` + тесты.
7. `TemplateApplyService` + тесты.
8. `TemplateVersionService` + тесты.
9. DI-регистрация в `App.xaml.cs`.
10. `MainViewModel`: `AppMode`, коллекции, команды + тесты.
11. `MainWindow.xaml`: переключатель режимов, панель «Шаблоны», диалоги выбора.
12. Прогон `dotnet build HearthSwing.slnx -c Release` и
    `dotnet test HearthSwing.slnx -c Release`; ручной smoke на реальном WTF.

## 13. Явно вне объёма v1

- Извлечение per-character записей из аккаунтных `*.lua` по ключу `"Name - Realm"`
  (требует парсинга Lua) — отдельная будущая фаза.
- Слияние (merge) настроек аккаунта вместо перезаписи.
- Регистронезависимая/эвристическая замена имён.
- Экспорт/импорт шаблонов между машинами (можно добавить поверх `IArchiveService`).
