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
  `token.bin` (DPAPI-encrypted refresh token of the *active* account), `account.json` (last
  resolved `MyChannel` — the fallback identity when a startup `channels.list?mine=true` 403s on
  quota), `log.txt`, `WebView2\` (shared browser profile), and **`accounts\{channelId}\`** each
  holding that account's `settings.json` + `cache.json`. `AccountViewModel` points
  `SubscriptionStore` at the active account's folder on sign-in (`ActivateAccount`) and unbinds
  it on sign-out (`Deactivate` → reads empty, writes dropped). A pre-`accounts\` flat
  `settings.json`/`cache.json` is migrated into the first account's folder once. Signing into a
  *different* account relaunches the app so every view model / `ThemeManager` rebinds. Delete
  `accounts\` + `token.bin` + `account.json` to reset; `log.txt` is the first place to look when
  debugging a live run.
- **Visual testing:** for Claude to drive / screenshot the running app via computer-use, a
  Start-menu shortcut must exist (computer-use resolves apps by Start-menu name, not by running
  process). Create it once, then `request_access(["YouTube Desktop Client"])` works after a
  Claude session restart:
  ```powershell
  $exe = "$PWD\YouTubeDesktopClient\bin\Debug\net10.0-windows\YouTubeDesktopClient.exe"
  $lnk = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\YouTube Desktop Client.lnk"
  $s = (New-Object -ComObject WScript.Shell).CreateShortcut($lnk)
  $s.TargetPath = $exe; $s.WorkingDirectory = (Split-Path $exe); $s.Save()
  ```
  (Run from `desktop-client/`. It just points at the dev build — `rm "$lnk"` to remove.)
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
by a XAML view. WebView2 handles what the public API cannot: video playback (YouTube's official
IFrame player by default, or the unmodified watch page) and the personalized Home feed (rendered
directly). No DOM injection anywhere — that is the whole point of this app versus the extension.

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
- `Auth/AuthService.cs` (`: IAuthService`) — OAuth Authorization Code + PKCE. Spins up a temporary
  loopback `HttpListener` on `http://127.0.0.1:{port}/`, opens the system browser, captures the
  code. Refresh token encrypted at rest via Windows DPAPI (`TokenStore.cs`).
  `GetAccessTokenSilentAsync` returns `null` (never throws) for every "not signed in" outcome — it
  runs inside the sync timer.
- `Account/AccountViewModel.cs` — the single "who's signed in" source. `ResolveAsync` (startup):
  silent token → `IYouTubeAccountApi.GetMyChannelAsync` (`channels.list?mine=true`, or the cached
  `account.json` when that 403s) → `SubscriptionStore.ActivateAccount`. `SignInAsync` /
  `SignOutAsync` drive the toolbar account control (name + avatar + a Sign-out popup — Sign
  in/out is **not** in the tray anymore). Sign-out clears the token + `account.json` and wipes
  the WebView2 profile (deferred via `WebViewProfile` — the folder is held by lingering
  msedgewebview2 processes, so the wipe finishes at next launch). The background sync (which
  writes `cache.json`) only starts once an account is active.

### Background sync + feed expansion (quota is the constraint)

The free quota is 10,000 units/day and `playlistItems.list` (1 unit/channel, not batchable)
dominates. Two mechanisms keep a hundreds-of-subscriptions account under quota:

- `Sync/BackgroundSyncService.cs` — `Timer`-driven, default **3-hour** interval (not the
  extension's 45 min). Fires the first run at `TimeSpan.Zero`. The callback **must never let an
  exception escape** — an unhandled exception on a threadpool thread kills the process. Sends
  stored ETags as `If-None-Match`; a `304` costs zero quota and short-circuits that channel.
  Raises `SyncCompleted` on the threadpool thread — UI subscribers marshal to the dispatcher
  themselves. `ReconcileSubscriptions` diffs the fresh subscription set against the previous
  cache: reports "+N / −N" to `NotificationCenter` and calls
  `SubscriptionStore.PruneChannelsFromGroups` for any channel unsubscribed on the web (skipped on
  the first-ever sync).
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
  each is its own `WebView2`, with the real (truncated) video title, a close button,
  middle-click-to-close and a Close / Close others / Close all context menu. Clicking a video
  reuses the active video tab in place unless `forceNewTab` (from the Feed) or no video tab is
  active. `PruneWebViews` disposes orphaned instances.
- **The player is `Player/PlayerView.xaml`** — the active tab's `WebView2` plus a native
  title / channel / views·date / description strip, with `Views/VideoActionBar.xaml` docked above
  it. Two playback modes (`AppSettings.PlaybackMode`, Settings → Playback, default `Embed`):
  - **`Embed`** — `Player/PlayerPageHost.cs` maps the bundled `Player/player.html` to a virtual
    `https://ytdesktop.local/` origin and points the tab there; the page runs YouTube's **official
    IFrame Player API** (`youtube.com/embed`). No YouTube web sign-in, no API quota, and the JS
    bridge (`postMessage` both ways) gives us `pauseVideo()` — so `MainWindow` pauses the player
    when you switch tabs/destinations. Cost: playback is **not** written to YouTube watch history.
    On an un-embeddable video the page's `onError` → `PlayerView` shows an "Open on YouTube"
    fallback that flips that tab to `FullPage`.
  - **`FullPage`** — the real `youtube.com/watch?v=` page in the tab (writes history when the
    shared profile is signed in); the native metadata strip is hidden.
- **All `WebView2` instances share one `CoreWebView2Environment`** (persistent user-data folder
  under `%AppData%\YouTubeSubscriptionsToolkit\WebView2\`, plus
  `--autoplay-policy=no-user-gesture-required` so the embed player starts on its own), so a
  youtube.com sign-in done once (Home destination, or the Embed fallback) carries across every tab
  and app restarts. A `WebView2` must be navigated *after* `EnsureCoreWebView2Async(env)` or it
  silently binds the default env (see `InitAndNavigateAsync` / `NavigateForModeAsync`).
- **`Player/VideoActionsViewModel.cs`** backs both the action bar (Like / Dislike / Subscribe /
  Comment) and the metadata strip — one shared instance, handed to `VideoActionBar` and
  `PlayerView`. All Data-API on the token the app already holds, so it works even when the embed
  player shows nothing personalised. The `snippet,statistics` video call the action bar already
  makes now also carries description / publishedAt / viewCount (no extra quota). Calls wait for
  the server then flip local state (no optimistic UI).
- **`Diagnostics/NotificationCenter.cs`** (static, like `Logger`) is the one sink for user-facing
  problem messages — API quota/permission errors, failed background syncs. The toolbar bell turns
  red while there are unread items and opens a popup listing them. **Nothing else renders these
  strings inline** — route new error messages here, not to a status label.
- "Mark watched" is **local-only bookkeeping** — there is no API to mark a video watched on the
  real account. Only `FullPage` playback (signed in) writes real YouTube watch history; the
  default `Embed` player does not.
- `csproj` deliberately removes the implicit `System.Windows.Forms` / `System.Drawing` global
  usings: `UseWPF` + `UseWindowsForms` together add them and they collide with WPF's
  `Application`/`Timer`/`UserControl`. Only `Tray/*` (NotifyIcon) uses WinForms, and it qualifies
  those types explicitly.
- **Theming reaches WPF only.** WPF popups/menus/tooltips are themed by styles in
  `Themes/Controls.xaml` (`ToolTip` included — without it every `ToolTip="..."` is system-white in
  dark). The tray `ContextMenuStrip` is WinForms, so `Tray/DarkMenuRenderer.cs` gives it a
  `ProfessionalColorTable` + text-colour override, re-applied on `ThemeManager.ThemeChanged`.

## extension architecture

`background.js` (MV3 service worker) owns a `chrome.alarms` refresh loop (45 min) and a
message router; `youtubeApi.js` wraps the Data API using `chrome.identity.getAuthToken` (one
`https://www.googleapis.com/auth/youtube` token for everything, no refresh mechanism);
`storage.js` wraps `chrome.storage`. `dashboard.html/js` is the management UI;
`content-groups.js` injects a "My groups" section into YouTube's own sidebar and
`content-location.js` adds a country badge on watch pages — those two content scripts are the
fragile parts the desktop client exists to avoid. Cross-account subscription migration was
implemented and then deliberately removed — see `extension/STATUS.md` before reconsidering it.
