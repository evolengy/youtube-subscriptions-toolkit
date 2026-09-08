# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository shape

Two **independent** products that solve the same problem (PocketTube-style subscription
management on top of the YouTube Data API v3), plus the design docs that drove the second one:

| Path | What it is | Language |
|---|---|---|
| `extension/` | The original Chrome MV3 extension. Still maintained, ships on its own. | Vanilla JS (ES modules), no build step |
| `desktop-client/` | A standalone Windows desktop app that re-implements the extension's functionality natively and adds embedded playback. | C# / .NET 10 / WPF |
| `docs/superpowers/` | The spec + task-by-task plan the desktop client was **originally** built from (Superpowers SDD workflow). The app has evolved well past it since — **this file (CLAUDE.md) is the current architecture reference**; the spec has an "Evolution since this spec" section listing what changed. | Markdown |

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
  `YouTubeDesktopClient.Tests/YouTubeApiClientTests.cs`, drives the real `YouTubeApiClient`) and
  hand-written fake interface classes — one per test file, `file`-scoped, no mocking library, keep
  it that way. The API surface is split into `IYouTubeApiClient` / `IYouTubeAccountApi` /
  `IYouTubePlaylistApi` / `IYouTubeCommentApi` (+ `IAuthService`, `IFeedExpansionService`)
  precisely so each fake only stubs the handful of methods its view model touches. `NotificationCenter`
  is static, so tests that provoke errors call `NotificationCenter.Clear()` first. Current count is
  ~160; every phase adds a test file next to its view model.

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

- `Api/YouTubeApiClient.cs` — thin YouTube Data API v3 wrapper implementing `IYouTubeApiClient`
  (sync), `IYouTubeAccountApi`, `IYouTubePlaylistApi`, `IYouTubeCommentApi` (each split off so a
  view model's test fake only stubs what it uses). Non-2xx on read paths becomes
  `YouTubeApiException` (carries status + body) via `ReadJsonRootAsync`; writes go through
  `EnsureOkAsync`; `ApiErrorText.Describe` turns either into one `NotificationCenter` sentence
  (quota / scope / generic). ETag support the extension's `youtubeApi.js` lacks.
- **OAuth scope is `.../auth/youtube.force-ssl`** (`Auth/AuthService.cs`), NOT the plain
  `.../auth/youtube` — comment reads/writes (`commentThreads.*`, `comments.*`) 403 with
  `ACCESS_TOKEN_SCOPE_INSUFFICIENT` on the plain scope. `force-ssl` is a superset, so
  subscriptions / playlists / ratings still work. **Changing it invalidates old consent — the
  user must sign out and back in once.**
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

The free quota is 10,000 units/day. **Reads (`*.list`) cost 1 unit, writes
(`insert`/`update`/`delete`/`rate`) cost 50, `search.list` costs 100** — so `playlistItems.list`
(1 unit/channel, not batchable) dominates the sync, and user-triggered writes (rate a video, add
to a playlist, post a comment) should each be a deliberate action, never bulk. Two mechanisms keep
a hundreds-of-subscriptions account under quota:

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
`QuotaExhausted`). `GetVisibleItems` stays a pure function. The card grid itself is
`Views/VideoCardGrid.xaml` — a `ListBox` + WpfToolkit `VirtualizingWrapPanel` (NuGet
`VirtualizingWrapPanel`) that reflows to width and recycles containers, shared by `FeedPanel` and
`PlaylistsPanel`. It owns the two gotchas: `MouseWheelDelta` is raised (the default is tiny for a
card grid), and the real inner `ScrollViewer` is pinned to `HorizontalScrollBarVisibility=Disabled`
on `Loaded` **and** `SizeChanged` (sub-pixel horizontal extent at some widths, and the XAML
attached property alone doesn't hold against `IScrollInfo`). Each consumer supplies its own
`ItemTemplate` and handles `NearEndReached`; `SetDensity` rewrites the `Feed.CardWidth/…` resources
(`Feed/FeedLayout.cs`).

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
  **Exception: the "Start with Windows" checkbox** is *not* an `AppSettingsViewModel` /
  `settings.json` value — it reads and writes the `HKCU\...\Run` key directly through
  `Tray/StartupRegistration.cs` (keyed by `StartupRegistration.DefaultValueName`), the single
  source of truth shared with the tray menu's identical item (which re-reads it on `Opening`).

### UI shell (MainWindow)

- **Sidebar destinations** (Feed / Home / Playlists / Channels / Settings) are *not* tabs — a
  `ListBox` in the sidebar; `MainWindow` swaps `ContentHost.Content` directly.
- **Playlists** — `Playlists/PlaylistsViewModel.cs` (shared by `Views/PlaylistsPanel.xaml` and the
  toolbar/action-bar "Save to playlist" picker) fetches the user's playlists once per session and
  mutates the list in place on create / rename / delete / add. `Api/IYouTubePlaylistApi` (on
  `YouTubeApiClient`): reads 1 unit, writes **50**. A `playlistItems.list` right after a delete is
  eventually-consistent, so the panel drops the removed card locally instead of re-fetching.
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
  Save) and the metadata strip — one shared instance, handed to `VideoActionBar` and `PlayerView`.
  All Data-API on the token the app already holds, so it works even when the embed player shows
  nothing personalised. The `snippet,statistics` video call the action bar already makes also
  carries description / publishedAt / viewCount (no extra quota). Calls wait for the server then
  flip local state (no optimistic UI). **Save** raises `SaveRequested` → `MainWindow` opens the
  themed playlist picker (existing playlists + "Manage playlists…").
- **`Player/CommentsView.xaml`** sits under the metadata strip in embed mode (hidden in FullPage):
  `Player/CommentsViewModel` (`IYouTubeCommentApi`) lazy-loads `commentThreads.list` on first
  reveal, paginates, loads a thread's full replies on the "view all" toggle, and posts comments
  / replies (prepending a local copy — `commentThreads.list` lags a write). The compose box lives
  here now, not in the action bar.
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
- **App icon** — `Assets/app.ico` (YouTube-style red pill + white play triangle, generated;
  small frames BMP so GDI reads them, the 256 frame PNG). Wired three ways: `<ApplicationIcon>`
  in `csproj` (exe → taskbar / Explorer), `Icon="/Assets/app.ico"` on `MainWindow` (title bar /
  Alt-Tab, needs the `<Resource>` include), and `TrayIconService` pulls it off the running exe
  with `Icon.ExtractAssociatedIcon(Environment.ProcessPath)` — no second copy of the file.
- **Theming reaches WPF only.** WPF popups/menus/tooltips are themed by styles in
  `Themes/Controls.xaml` (`ToolTip` included — without it every `ToolTip="..."` is system-white in
  dark). The tray `ContextMenuStrip` is WinForms, so `Tray/DarkMenuRenderer.cs` gives it a
  `ProfessionalColorTable` + text-colour override, re-applied on `ThemeManager.ThemeChanged`.

## extension architecture

`background.js` (MV3 service worker) owns a `chrome.alarms` refresh loop (45 min) and a
message router; its sync also fires per-group "new video" desktop notifications (opt-in
via `groups[id].notify`, set on `settings.html`; diff logic in the ESM module
`groupNotify.js`, `node --test groupNotify.test.mjs`); `youtubeApi.js` wraps the Data API using `chrome.identity.getAuthToken` (one
`https://www.googleapis.com/auth/youtube` token for everything, no refresh mechanism).
`apiFetch` retries transient 5xx/429 (not 4xx) with exponential backoff (`node --test
youtubeApi.test.mjs`), and `refreshAll`'s per-channel loop is `try`-wrapped so one
channel's failure keeps its previous cached videos instead of aborting the whole sync.
`storage.js` wraps `chrome.storage`. `logger.js` (ESM) is a 200-entry ring buffer in
`chrome.storage.local` — `background.js` records each sync's outcome and its errors
there (the MV3 worker's own console is ephemeral); surface is `logs.html`. `toast.js`
(ESM, self-injecting styles) is the shared ephemeral-notification widget — page action
errors (`Refresh now`, sign-in, unsubscribe) surface as an auto-dismissing toast now,
not as sticky text in the toolbar. The UI is
five extension pages: `dashboard.html/js` (groups list, feed) plus `channels.html`
(channel health table), `groups.html` (assign channels ↔ groups), `settings.html` (hide
sections of YouTube's own left guide; feed blocklist; per-group notification opt-in) and
`logs.html` (the activity log), the latter four opened from the dashboard via
`chrome.tabs.create` (the `settings.html` and `logs.html` buttons are always shown — they
need no auth). The single content script `content-groups.js` (matched to all of
`youtube.com/*`, so it survives SPA navigation) injects a "My groups" section + grouped-feed
overlay into YouTube's sidebar **and** the channel-country badge on watch pages — it is the
fragile part the desktop client exists to avoid.

Pure, `node --test`-covered logic modules (`window.YST*` globals, loaded by plain `<script>`
before the page's ES module; content-script ones also listed in the manifest `js` array):
`feedFilter.js` (feed filter/sort + `hydrateVideoList` for the liked / not-interested
pseudo-groups, shared dashboard + overlay), `groupCounts.js` (`countNewPerGroup` — the
"N new since last opened" group badges, shared dashboard + overlay), `guideDeclutter.js`
(`buildGuideCss` — CSS to hide chosen sections of YouTube's own left guide, driven by
the `guideHidden` sync key; surface is `settings.html`; `settings.html` also edits the
`feedBlocklist` sync key — keyword + muted-channel feed filter applied in `feedFilter.js`),
`channelHealth.js`
(activity classification, `channels.html`), `groupEditor.js` (`groups.html`),
`emojiPicker.js` + `emojiData.js` (group-icon picker, used by `groups.html` and the
content script), `icons.js` (`window.YSTIcons.make` — inline-SVG card-action icons,
dashboard + content script). All group create/rename/delete/icon/assignment lives in
`groups.html`; the dashboard's group list is a read-only feed filter. "Not interested"
is an extension-only per-video hide list (id list in `sync` + metadata map in `local`),
filtered from the feed by default — YouTube has no API for its own equivalent. The
dashboard/sidebar group list also carries two pseudo-groups (👍 Liked, ⊘ Not interested)
that render the full stored lists, and per-group "N new" badges keyed on a
`groupLastVisited` sync map.

YouTube-DOM realities worth knowing before touching the content script are in
`extension/STATUS.md` → "Совместимость с DOM YouTube". `extension/STATUS.md` is otherwise the
feature-status source.
