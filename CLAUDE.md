# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository shape

Two **independent** products that solve the same problem (PocketTube-style subscription
management on top of the YouTube Data API v3), plus the design docs that drove the second one:

| Path | What it is | Language |
|---|---|---|
| `extension/` | The original Chrome MV3 extension. Still maintained, ships on its own. | Vanilla JS (ES modules), no build step |
| `desktop-client/` | A standalone Windows desktop app that re-implements the extension's functionality natively and adds embedded playback. | C# / .NET 10 / WPF |
| `docs/superpowers/` | The spec + task-by-task plan the desktop client was built from (Superpowers SDD workflow). Read the spec before changing desktop-client architecture. | Markdown |

The desktop client is **not** a replacement for the extension — it was built specifically to
stop depending on injecting into YouTube's DOM (the extension's main fragility). Changes to one
do not imply changes to the other. `extension/STATUS.md` is written in Russian; code, comments,
and commit messages are in English (Conventional Commits: `feat:` / `fix:` / `chore:` / `perf:` / `docs:`).

## Commands

### desktop-client (primary active codebase)

Run from `desktop-client/`:

```bash
dotnet build YouTubeDesktopClient.sln
```

```bash
dotnet test
```

```bash
dotnet run --project YouTubeDesktopClient
```

Single test / filtered:

```bash
dotnet test --filter "FullyQualifiedName~VideoClassifierTests"
```

```bash
dotnet test --filter "DisplayName~ParsesSingleFullPage"
```

- **.NET 10 SDK is required** (`net10.0-windows`); .NET 8/9 will not build it. The plan is
  explicit that 10 is a hard constraint.
- Runtime data lives in `%AppData%\YouTubeSubscriptionsToolkit\`:
  `settings.json`, `cache.json`, `token.bin` (DPAPI-encrypted refresh token), `log.txt`.
  Delete these to reset app state; `log.txt` is the first place to look when debugging a live run.
- Tests use xUnit with a **hand-written `FakeHttpMessageHandler`** (in
  `YouTubeDesktopClient.Tests/YouTubeApiClientTests.cs`) and hand-written fake
  `IYouTubeApiClient` / `IFeedExpansionService` classes (one per test file, `file`-scoped) —
  there is no mocking library, keep it that way. `NotificationCenter` is static, so tests that
  provoke errors call `NotificationCenter.Clear()` first.

### extension

No build. Load `extension/` as an unpacked extension in Chrome. `manifest.json` contains a real
OAuth `client_id`; the desktop client's `App.xaml.cs` contains a real client id **and secret** —
both are committed intentionally (Google installed-app credentials are "not treated as a secret"),
so don't treat their presence as a leak to fix.

## desktop-client architecture

### The core split: pure logic vs. WPF

Everything testable is a plain class with **no UI dependency**, consumed by a view model, rendered
by a XAML view. WebView2 hosts unmodified youtube.com pages for anything the public API cannot
provide (playback, the personalized Home feed). No DOM injection anywhere — that is the whole point
of this app versus the extension.

- `Api/YouTubeApiClient.cs` — thin YouTube Data API v3 wrapper. Non-2xx on read paths becomes
  `YouTubeApiException` (carries status + body) rather than blowing up JSON parsing. Adds ETag
  support the extension's `youtubeApi.js` lacks. `FetchRecentUploadIdsAsync` threads a `pageToken`
  for backward history walks; the player-action endpoints (`GetVideoActionStateAsync`,
  `RateVideoAsync`, `SubscribeAsync`, `PostCommentAsync`) are write calls on the same
  `.../auth/youtube` token (POST bodies via the `JsonBody` helper).
- `Storage/SubscriptionStore.cs` — the two JSON files. `settings.json` = user-curated data
  (groups, watched ids, **and the `App` section**: theme / feed density / auto-expand, via
  `GetAppSettings` / `SaveAppSettings`) — the file a future sync backend would target. `cache.json` =
  re-fetchable data (subscriptions cache, videos cache, last-synced, per-channel ETags + deep-history
  cursors `UploadsNextPageToken` / `HistoryComplete`). All new record fields have defaults so an
  older file still deserializes. All reads/writes go through one reentrant `lock` because the
  background sync thread writes while the UI thread reads. Writes are temp-file-plus-atomic-rename;
  a corrupt file is logged and treated as empty (never throws on launch).
- `Feed/VideoClassifier.cs` — ISO-8601 duration parsing + the Shorts/Live/Video heuristic
  (live/upcoming first, then a 60s Shorts threshold). Ported verbatim from the extension's
  already-approved logic; keep the heuristic identical unless the spec changes.
- `Auth/AuthService.cs` — OAuth Authorization Code + PKCE. Spins up a temporary loopback
  `HttpListener` on `http://127.0.0.1:{port}/`, opens the system browser, captures the code.
  Refresh token encrypted at rest via Windows DPAPI (`TokenStore.cs`). `GetAccessTokenSilentAsync`
  returns `null` (never throws) for every "not signed in" outcome — it runs inside the sync timer.

### Background sync + feed expansion (quota is the constraint)

The free quota is 10,000 units/day and `playlistItems.list` (1 unit/channel, not batchable)
dominates. Two mechanisms keep a hundreds-of-subscriptions account under quota:

- `Sync/BackgroundSyncService.cs` — `Timer`-driven, default **3-hour** interval (not the
  extension's 45 min). Fires the first run at `TimeSpan.Zero`. The callback **must never let an
  exception escape** — an unhandled exception on a threadpool thread kills the process. Sends
  stored ETags as `If-None-Match`; a `304` costs zero quota and short-circuits that channel.
  Raises `SyncCompleted` on the threadpool thread — UI subscribers marshal to the dispatcher themselves.
- `Sync/FeedExpansionService.cs` — pulls *older* history pages on demand as the user scrolls
  past the cached window. Bounded by `MaxCachedPerChannel` (500) and `MaxParallelism` (4). A 403
  whose body names `quotaExceeded`/`dailyLimitExceeded`/`rateLimitExceeded` sets `QuotaExhausted`
  and stops all expansion for the day; a plain 403 just skips that one channel.

**Cursor invariant:** `BackgroundSyncService` refreshes the newest N videos and merges them over
the cache (`Storage/VideoMerge.Dedup`); `FeedExpansionService` advances `UploadsNextPageToken`
backward through history. A sync must **not** rewind that cursor — it only seeds it when there
has never been one. Breaking this makes deep scroll re-fetch pages forever.

### The feed (MVVM + virtualization)

`Feed/FeedViewModel.cs` is stateful: an `ObservableCollection<VideoCardViewModel> Items` that
grows by `PageSize` (60) per scroll batch from the local cache, and once that is exhausted asks
`FeedExpansionService` for more history (gated by `AppSettings.AutoExpandFeed` and
`QuotaExhausted`). `GetVisibleItems` stays a pure function. `Views/FeedPanel.xaml` is a `ListBox`
+ WpfToolkit `VirtualizingWrapPanel` (NuGet `VirtualizingWrapPanel`) — an adaptive card grid
that reflows to width and recycles containers. This replaced an imperative card builder that
had to cap itself at 150 items. Two panel gotchas are handled in `FeedPanel.xaml.cs`:
`MouseWheelDelta` is raised (the default is tiny for a card grid), and the real inner
`ScrollViewer` is pinned to `HorizontalScrollBarVisibility=Disabled` on `Loaded` **and**
`SizeChanged` (the panel reports a sub-pixel horizontal extent at some widths, and the XAML
attached property alone doesn't hold against its `IScrollInfo`).

### Theming

- `Themes/Light.xaml` / `Dark.xaml` — mirrored semantic colour tokens (`Brush.Canvas`,
  `Brush.Surface`, `Brush.Elevated`, `Brush.BorderSubtle`, `Brush.Text*`, ...). Dark is
  near-flat: `Surface == Canvas`, so regions read by their content, not tone.
- `Themes/Controls.xaml` — every implicit control style, **fully retemplated** (ComboBox,
  CheckBox, RadioButton, TextBox, ScrollBar, ContextMenu, TabControl, ListBox, ...). Setting only
  `Background`/`Foreground` on the stock Aero templates leaves light system chrome showing through
  in dark mode — retemplate or it looks broken. All colours are `DynamicResource`.
- `Themes/ThemeManager.cs` — Light/Dark/System. `App.xaml` merges a theme dictionary **alongside**
  `Controls.xaml` at the app level (not nested inside it — nesting lets the default shadow the
  swapped-in one). `ThemeManager` swaps that entry in `Application.Resources` at runtime, resolves
  `System` from `HKCU\...\Themes\Personalize`, follows `SystemEvents.UserPreferenceChanged`, and
  writes the Windows DWM accent (`0xAABBGGRR`) into `Application.Resources` so it survives the swap.
- **Gotcha:** an implicit `<Style TargetType="Window">` does **not** apply to the `MainWindow`
  subclass, so `MainWindow.xaml` sets `Background` explicitly on the `<Window>` and root `<Grid>`.
- `MainWindow` P/Invokes `DwmSetWindowAttribute(DWMWA_USE_IMMERSIVE_DARK_MODE)` on
  `OnSourceInitialized` and every `ThemeChanged` — the native title bar + system menu follow the
  app theme.
- `Settings/AppSettingsViewModel.cs` is the single runtime source of truth for preferences: every
  setter persists to `settings.json` and pushes the change (ThemeManager for theme, an event for
  density). `Views/SettingsPanel.xaml` is the destination; the toolbar toggle drives the same VM.

### UI shell (MainWindow)

- **Sidebar destinations** (Feed / Home / Channels / Settings) are *not* tabs — a `ListBox` in the
  sidebar; `MainWindow` swaps `ContentHost.Content` directly.
- **Video tabs** are the only real tabs (`Tabs/TabsViewModel.cs`) and live in the top strip:
  each is its own `WebView2` showing a full `youtube.com/watch?v=` page, with the real (truncated)
  video title, a close button, middle-click-to-close and a Close / Close others / Close all
  context menu. Clicking a video reuses the active video tab in place unless `forceNewTab` (from
  the Feed) or no video tab is active. `PruneWebViews` disposes orphaned instances.
- **All `WebView2` instances share one `CoreWebView2Environment`** with a persistent user-data
  folder under `%AppData%\YouTubeSubscriptionsToolkit\WebView2\`, so a youtube.com sign-in done
  once (via the Home destination) carries across every player tab and app restarts. A `WebView2`
  must be navigated *after* `EnsureCoreWebView2Async(env)` or it silently binds the default env
  (see `InitAndNavigateAsync`).
- **`Views/VideoActionBar.xaml`** sits above the player WebView2: Like / Dislike / Subscribe /
  Comment, driven by `Player/VideoActionsViewModel.cs` over the Data API on the token the app
  already holds — works even when the embedded page shows "Sign in". Calls wait for the server
  then flip local state (no optimistic UI). Watch history is deliberately absent — no API writes it.
- **`Diagnostics/NotificationCenter.cs`** (static, like `Logger`) is the one sink for user-facing
  problem messages — API quota/permission errors, failed background syncs. The toolbar bell turns
  red while there are unread items and opens a popup listing them. **Nothing else renders these
  strings inline** — route new error messages here, not to a status label.
- "Mark watched" is **local-only bookkeeping** — there is no API to mark a video watched on the
  real account. Actually playing a video in a tab writes real YouTube history for free.
- `csproj` deliberately removes the implicit `System.Windows.Forms` / `System.Drawing` global
  usings: `UseWPF` + `UseWindowsForms` together add them and they collide with WPF's
  `Application`/`Timer`/`UserControl`. Only `Tray/TrayIconService.cs` (NotifyIcon) uses WinForms,
  and it qualifies those types explicitly.

## extension architecture

`background.js` (MV3 service worker) owns a `chrome.alarms` refresh loop (45 min) and a
message router; `youtubeApi.js` wraps the Data API using `chrome.identity.getAuthToken` (one
`https://www.googleapis.com/auth/youtube` token for everything, no refresh mechanism);
`storage.js` wraps `chrome.storage`. `dashboard.html/js` is the management UI;
`content-groups.js` injects a "My groups" section into YouTube's own sidebar and
`content-location.js` adds a country badge on watch pages — those two content scripts are the
fragile parts the desktop client exists to avoid. Cross-account subscription migration was
implemented and then deliberately removed — see `extension/STATUS.md` before reconsidering it.
