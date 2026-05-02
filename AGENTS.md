# AGENTS.md

This file provides guidance to Codex (Codex.ai/code) when working with code in this repository.

## Build

Open `yanshuai.sln` in **Visual Studio 2019+** and build via the IDE. There is no CLI build command.

- Target SDK: `10.0.19041.0`; minimum `10.0.10240.0` (Lumia 950)
- Platforms: ARM (primary), x86, x64
- Bundle always built: `x86|x64|arm`
- No NuGet restore needed beyond `Microsoft.NETCore.UniversalWindowsPlatform 6.2.9`

There are no automated tests in this project.

## Architecture

UWP app (namespace `yanshuai`, Chinese UI) acting as an AI chat client. All source files are in the project root.

### Data layer

- **`Models.cs`** — all `[DataContract]` model classes: `AppData`, `Conversation`, `ConversationMessage`, `CharacterCard`, `ApiProfile`, `WorldBookEntry`, `UserProfile`, `ConvAppearance`, `BranchPoint`, `ConversationBranch`
- **`DataManager.cs`** — static class; loads/saves the single `AppData` instance to `yanshuaiAppData.json` in `ApplicationData.Current.LocalFolder` via `DataContractJsonSerializer`. A `SemaphoreSlim` prevents concurrent save races. Call `DataManager.LoadAsync()` at startup and `DataManager.SaveAsync()` after any mutation.
- **`AppSettings.cs`** — thin wrapper around `ApplicationData.Current.LocalSettings` (key-value). Stores: dark mode, language, jailbreak, default API profile ID, OOBE state, enter-key behavior, startup mode, web search settings.
- **`AppState.cs`** — in-memory cross-page state: `ActiveConversation`, a per-conversation background task registry (for parallel streaming across multiple conversations).

### Page structure

| Page | Purpose |
|---|---|
| `StartingPage` | App entry; routes to OOBE or main |
| `OobePage1–5` | First-run wizard |
| `MainPage` | Primary chat UI — sends messages, streams AI responses |
| `ConversationsListPage` | Browse/manage all conversations |
| `ConvSettingsPage` | Per-conversation settings |
| `ConvAppearancePage` | Bubble colors, background image |
| `ApiProfilesPage` | Manage API profiles (URL, key, model, provider) |
| `CharacterCardsPage` | Browse character cards |
| `CharaWizardPage1–4` | Multi-step character card creation |
| `WorldBookPage` | World book entries |
| `UserProfilesPage` / `UserProfilePage` | User persona management |
| `ImportExportPage` | ZIP backup/restore; SillyTavern import/export |
| `SettingsPage` | Global settings |
| `InfoPage` | About page |

### Key patterns

**Theme:** Always call `AppSettings.ApplyTheme(RootGrid, this)` in `OnNavigatedTo` for both element theme and bottom app bar.

**UWP async dialogs:** `await dialog.ShowAsync().AsTask()` — UWP `IAsyncOperation` must be converted with `.AsTask()`.

**XAML z-order:** Elements declared later in the same `Grid` render on top. The `LoadingOverlay` must be declared after `ScrollViewer`.

**Serialization:** Use `DataContractJsonSerializer` throughout. Helper generics `ToJson<T>` / `FromJson<T>` are defined in `ImportExportPage.xaml.cs` and can be reused.

**FontIcon foreground reset:** Use `Application.Current.Resources["ApplicationForegroundThemeBrush"]`, not `null`.

**Conversation branching:** `Conversation.Messages` is a live reference into `AllBranches[ActiveBranchIndex].Messages`. Always call `conv.InitBranches()` after deserialization and `conv.SyncActiveBranch()` before saving.

**API profile resolution** (`DataManager.GetProfileForConversation`): priority order — conversation's own `ApiProfileId` → `AppSettings.DefaultApiProfileId` → `AppData.DefaultApiProfileId` → legacy selected → first available.

**Cross-thread UI access:** All UI element access from background threads (`Task.Run`) must be wrapped in `await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => { ... })`. Capture needed values (e.g. `bubble.Content`) inside the dispatcher callback into local variables before using them outside. `ScrollToBottom()` and any method touching XAML controls are also subject to this rule.

**ValueTuple not supported:** The project targets a C# language version that does not support value tuples `(T1, T2)`. Use parallel `List<T>` collections or a named class instead.

**WorldBookEntry scope:** `CharacterIds` (List<string>) replaces the old `CharacterId` (string). Empty list = global (all conversations). `[OnDeserialized]` migrates old single-ID data automatically. In `MainPage.BuildSystemPrompt`, entries are filtered by `_conv.CharacterCardId` before keyword matching.

**WorldBookPage grouping:** Uses a code-behind `CollectionViewSource` field (`_cvs`), not a XAML resource, to avoid XAML resource binding issues. `_cvs.Source = BuildGroups()` then `EntriesList.ItemsSource = _cvs.View`. `EntryGroup : List<WorldBookEntry>` with a `GroupName` property drives the `GroupStyle` header. Empty angle groups are omitted from `BuildGroups`.

**CollectionViewSource in UWP:** Do not declare `CollectionViewSource` as a XAML `Page.Resources` item and then assign `.Source` from code-behind via `x:Name` — this does not work reliably. Create it as a `readonly` field in code-behind instead.

### Streaming

`MainPage` streams AI responses over HTTP using `HttpCompletionOption.ResponseHeadersRead`. During streaming, `ChatBubble.IsStreaming = true` shows a plain `TextBlock`; on completion it switches to the markdown-rendered `RichTextBlock`.

### Import/Export

`ImportExportPage` handles:
- **ZIP backup** (`ExportAllAsZip` / `ImportFromZip`) — archives `yanshuaiAppData.json` plus `search_settings.json`
- **SillyTavern V2** character card PNG/JSON import and JSONL chat import

**WorldBookPage tree grouping:** `EntryGroup` has `IsExpanded` bool. `_flatList` (`ObservableCollection<object>`) mixes `EntryGroup` (headers) and `WorldBookEntry` (items). `WorldBookTemplateSelector : DataTemplateSelector` picks the template. Header taps toggle `IsExpanded` and insert/remove entries from `_flatList` directly. `EntriesList_SelectionChanged` deselects any `EntryGroup` that gets selected. Multi-select mode ignores group headers in `SelectedItems`.

**Multi-select mode pattern** (ConversationsListPage, CharacterCardsPage, WorldBookPage, ApiProfilesPage): `_selectMode` bool field; `RightTapped`/`Holding` on item → `EnterSelectMode()` + `SelectItemFromSender()`; `EnterSelectMode` flips `SelectionMode` to `Multiple`, hides normal buttons, shows ToggleAll/DeleteSelect/Cancel; `ExitSelectMode` reverses. Separate `SelectionChanged` handler for multi-select (unwire normal handler, wire multi handler).

**PasswordBox reveal:** Use `PasswordRevealMode = PasswordRevealMode.Peek` for built-in eye button (long-press to show).

**Image compression:** Do NOT set `encoder.BitmapTransform.ScaledWidth/Height` after `SetPixelData` — pixels are already scaled by `GetPixelDataAsync(transform)`, redundant BitmapTransform causes vertical tearing on portrait images.

**Mobile keyboard popup after delete:** Do not call `ConvList.Focus(FocusState.Programmatic)` after deleting a conversation — on Windows 10 Mobile, focusing a ListView triggers the soft keyboard.

**Character card file picker:** Use an inline `FileOpenPicker` with `SuggestedStartLocation = DocumentsLibrary` and `ViewMode = List`, not `ImportExportPage.PickFiles`, because the shared helper with `.png` filter causes Mobile to open the photo gallery instead.

### Stubs

`ChatMessage.cs`, `ChatSession.cs`, and `SessionManager.cs` are empty stubs kept for build compatibility after those classes were replaced by `ConversationMessage` / `Conversation` / `DataManager`.
