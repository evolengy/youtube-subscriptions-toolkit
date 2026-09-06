# Desktop YouTube client — design

> **This is the original build spec (2026-09-05). The app has evolved past it — see
> "Evolution since this spec" below, and use the repo's `CLAUDE.md` as the current
> architecture reference.** The rest of this document is preserved as the point-in-time
> SDD artifact it was.

## Evolution since this spec

The desktop client was built from this spec and then extended over several redesign rounds
(tracked in commit history and the author's working notes). Major departures:

- **Playback is the official IFrame player by default, not the embedded watch page.**
  `Player/player.html` (bundled, mapped to a virtual `https://ytdesktop.local/` origin) runs
  YouTube's IFrame Player API — no YouTube web login, no API quota, a JS bridge for
  pause-on-tab-switch and an "un-embeddable → open on YouTube" fallback. `AppSettings.PlaybackMode`
  keeps the full watch page as an opt-in (`FullPage`, writes watch history).
- **Native metadata strip + native comments** under the player (embed mode): title / channel /
  views·date / description, then a threaded comments list (`Player/CommentsView`,
  `commentThreads.list` / `comments.list` / `comments.insert`). The compose box moved here from the
  action bar.
- **Native player action bar** (`Views/VideoActionBar`) — Like / Dislike / Subscribe / Save,
  driven by `Player/VideoActionsViewModel` over the Data API, independent of the WebView session.
- **Playlists** — a sidebar destination + `Playlists/PlaylistsViewModel` + `IYouTubePlaylistApi`:
  browse / create / rename / delete, add/remove videos, "Save to playlist" from feed cards.
- **Account-scoped storage.** Instead of one flat `settings.json` / `cache.json`, each account's
  data lives under `%AppData%\...\accounts\{channelId}\`. `Account/AccountViewModel` +
  `IYouTubeAccountApi` resolve the signed-in channel and point `SubscriptionStore` at its folder;
  a toolbar account control (name + avatar + Sign out) replaces the tray's sign-in/out items.
- **OAuth scope is `.../auth/youtube.force-ssl`**, not the plain `.../auth/youtube` — comments
  require it.
- **Subscription reconciliation** — `BackgroundSyncService` diffs each sync against the cache,
  reports "+N / −N" and prunes unsubscribed channels out of groups.
- **UI shell** — Feed / Home / Playlists / Channels / Settings are sidebar destinations (a
  `ListBox`), not tabs; the only real tabs are open video documents. Full theme-token system
  (`Themes/*`), retemplated controls, themed tooltips + a dark WinForms tray menu, DWM dark title
  bar. The feed is a virtualized `Views/VideoCardGrid` (WpfToolkit `VirtualizingWrapPanel`), shared
  with the playlists panel.

## Context

The existing project is a Chrome MV3 extension (`browser-yotube-functions`) that adds PocketTube-style subscription groups, a filtered/sorted feed, dead-channel detection, and a channel-location badge on top of youtube.com, plus a sidebar injected into YouTube's own DOM. That injection is the extension's main source of fragility: YouTube's frequent redesigns can silently break the sidebar or the location badge at any time (see [STATUS.md](../../../STATUS.md) for the extension's current state).

This spec covers a **separate, standalone Windows desktop application** — not a replacement for the extension, which keeps working independently. The goal is a personal YouTube client ("a fork of the YouTube app, with the extension's functionality") that owns its entire UI natively, using the YouTube Data API v3 for data and an embedded browser control only for the parts that must render YouTube's own page (video playback).

## Goals

- Port the extension's working functionality: subscription groups, feed filter/sort (Video/Short/Live, date/duration/views), watched tracking, dead-channel detection + unsubscribe, channel country display.
- Full video playback (comments, likes, related videos, description) via the real youtube.com watch page, embedded — not a stripped-down player.
- A "Home" tab showing YouTube's real, personalized homepage — there is no API for recommendations, so this is the actual site rendered as-is.
- Runs in the background/tray, refreshing subscriptions on a schedule, independent of the extension.
- Stay within the YouTube Data API's free 10,000 units/day quota even for accounts with hundreds of subscriptions.
- Local-only storage for now, structured so a sync backend can be added later without a rewrite.

## Non-goals

- No cross-device sync in this version (explicitly deferred; storage is designed to make adding it later straightforward, not to implement it now).

## Architecture overview

- **.NET 10 (LTS), WPF (C#).** Chosen over Electron (heavier: bundles its own Chromium) and over Tauri/WinUI (WPF has the most mature tooling and least packaging friction for a personal, non-Store app), after comparing performance — WPF and Tauri end up similar on Windows since both ultimately render web content via the same system WebView2 (Chromium) component; Electron pays a fixed Chromium tax regardless of what it displays. .NET 10 rather than .NET 8: both are LTS releases, but 10 is the current one and gives a longer support runway for a project starting now.
- **Microsoft.Web.WebView2** hosts the actual YouTube page for playback. All other UI (groups list, feed grid, channel management) is native WPF — no DOM injection into any YouTube page is needed anywhere in this app, which is the main improvement over the extension's approach.
- **Storage**: two JSON files under `%AppData%\YouTubeSubscriptionsToolkit\`:
  - `settings.json` — user-curated data: groups, watched video ids. This is the file a future sync backend would target.
  - `cache.json` — re-fetchable data: subscriptions cache, videos cache, last-synced timestamp, per-channel ETags (see quota strategy below).
- **Background service**: a timer-driven sync loop runs for the lifetime of the process, independent of whether the main window is visible or minimized to tray.

## Components

Mirrors the extension's module split, ported from JS to C#:

| Extension file | Desktop equivalent | Notes |
|---|---|---|
| `youtubeApi.js` | `YouTubeApiClient.cs` | Same methods (subscriptions, channels, playlistItems, videos, unsubscribe), plus ETag support (new) |
| `storage.js` | `SubscriptionStore.cs` | Same schema, JSON-file-backed instead of `chrome.storage` |
| `background.js` (alarm + message handling) | `BackgroundSyncService.cs` | Timer-driven instead of `chrome.alarms`; no message-passing needed since everything is in-process |
| `dashboard.js` (feed filter/sort/classify) | `FeedViewModel.cs` | Same `parseIsoDuration` / `classifyVideoType` logic ported as-is (already-approved heuristic: live/upcoming first, then a 60s Shorts threshold) |
| `content-groups.js` (sidebar) | `GroupsPanel.xaml` / `GroupsViewModel.cs` | No DOM injection — this is just a native list bound to `SubscriptionStore` |
| `content-location.js` (country badge) | Rendered directly on each feed card | Country is already in the channel cache; no page injection needed |
| — (new) | `AuthService.cs` | OAuth Authorization Code + PKCE flow, see below |
| — (new) | `PlayerTabsViewModel.cs` | Manages the open video tabs (see UI section) |

## Authentication

- A **Desktop app** OAuth client (separate from the extension's Chrome Extension and Web application clients, same Google Cloud project).
- Flow: Authorization Code + PKCE. The app starts a temporary local `HttpListener` on `http://127.0.0.1:{port}/`, opens the system default browser to Google's consent screen, and captures the redirected authorization code.
- The code is exchanged for an `access_token` + `refresh_token`. The `refresh_token` is encrypted at rest with Windows DPAPI (`ProtectedData.Protect`, current-user scope) and stored in `%AppData%`.
- The background service uses the `refresh_token` to silently mint new `access_token`s — no repeated interactive login, unlike the extension's one-off `launchWebAuthFlow` (which had no refresh mechanism at all).

## API quota strategy

`playlistItems.list` (1 unit per channel, not batchable) dominates cost. At 45-minute refresh intervals (as the extension uses), 300 subscribed channels alone would consume roughly 13,000 units/day — over the 10,000/day free limit. Two changes address this:

1. **Longer default refresh interval**: 2–3 hours instead of 45 minutes. A desktop client checked periodically doesn't need the same freshness as a browser extension open on the subscriptions feed.
2. **ETag-conditional requests**: store the ETag returned with each channel's `playlistItems.list` response in `cache.json`. On the next cycle, send it as `If-None-Match`; a `304 Not Modified` response (no new uploads) costs zero quota. For accounts where most channels post infrequently, this sharply cuts real-world consumption below the worst-case table below.

Worst-case (no ETag hits) unit cost per full refresh cycle:

| Subscriptions | `playlistItems.list` | `videos.list` (batched by 50) | Total/cycle |
|---|---|---|---|
| 100 | 100 | ~30 | ~135 |
| 300 | 300 | ~90 | ~410 |
| 500 | 500 | ~150 | ~680 |

At a 3-hour interval (8 cycles/day) even the 500-subscription worst case (~5,440/day) stays under quota; ETag hits reduce it further in practice.

## UI

Single main window, no separate dashboard/popup split (unlike the extension, which needed both):

- **Left panel** — native group list: create/rename/delete groups, assign channels via checkboxes. Replaces both the extension's injected sidebar and its dashboard group-management UI.
- **Center area**, tab-based:
  - A pinned, non-closable **"Feed" tab** — video grid (thumbnail, title, duration/type/views, channel country, "mark watched" button), with the same Type filter and Sort control the extension has.
  - A pinned, non-closable **"Home" tab** — the real `https://www.youtube.com/` loaded in `WebView2`, unmodified. YouTube's personalized recommendation algorithm has no public API at all, so the only way to show it is to render YouTube's own homepage directly, exactly like a video tab.
  - Clicking a video **navigates the current tab's `WebView2`** to `https://www.youtube.com/watch?v={id}` if the current tab is already a video tab (YouTube-like in-place navigation), or opens a **new tab** if triggered from the Feed tab or via an explicit "open in new tab" action. Each video tab is a full, unmodified youtube.com watch page — comments, related videos, likes, subscribe button all included, since the whole point of the desktop app is to stop fighting YouTube's own markup.
  - Every open video tab is its own `WebView2` instance sharing one user data folder (so the login session is shared); each is roughly as resource-heavy as a browser tab showing the same page — expected and unavoidable, since it's genuinely Chromium underneath either way.
- **Channel management panel** — dead-channel list with unsubscribe, same as the extension.

### What's real vs. local-only

Worth being explicit about, since it's easy to assume more integration than the public API actually allows:

- **"Mark watched" in the Feed tab is local-only bookkeeping** (same as the extension's `watchedVideoIds`) — there is no public API to mark a video watched on the real account, so clicking it only affects this app's own filtering/dimming.
- **Actually playing a video in a video tab writes to the real YouTube watch history automatically**, at no extra implementation cost — it's the genuine youtube.com page and player, so YouTube's own history tracking applies exactly as it would in a browser.

## Background & tray

- Closing the window minimizes to the system tray (`NotifyIcon`) instead of exiting the process.
- Tray context menu: Open, Refresh now, Exit.
- The sync timer runs continuously regardless of window visibility.
- Optional, toggleable "Start with Windows" setting (startup shortcut or `Run` registry key).

## Error handling

Carries over the lesson learned while testing the extension: never silently swallow an API or auth failure. Background sync failures are logged and retried on the next cycle rather than crashing the tray-resident process; interactive actions (sign-in, unsubscribe) surface the real error message in the UI rather than failing silently.

## Testing

Manual verification for the OAuth flow, tray/background behavior, and feed rendering (this is a personal app, not a published product). Worth unit-testing in isolation: ISO-8601 duration parsing, the Shorts/Live/Video classification heuristic, and JSON store round-tripping — these are pure functions ported directly from already-working, already-tested-by-hand extension code.
