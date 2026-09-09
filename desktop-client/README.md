# YouTube Subscriptions Toolkit — desktop client

A standalone Windows app (**.NET 10 / WPF**) that re-implements the extension's subscription
management natively and adds an embedded player. It never injects into YouTube's DOM — WebView2
only renders YouTube's official IFrame player (or, optionally, the real watch page) and the
personalized Home feed. Everything else is the Data API on a token the app holds.

Highlights over the extension: an embedded player with a Like / Dislike / Subscribe / Save
action bar and threaded comments, multi-account support, light/dark theming, a tray icon, and
ETag-conditional syncing to stay under quota.

The current architecture reference is the root [`CLAUDE.md`](../CLAUDE.md) ("desktop-client
architecture"); [`docs/superpowers/`](../docs/superpowers/) has the original spec + plan.

---

## Build & run

Requires the **.NET 10 SDK** (`net10.0-windows` — .NET 8/9 will not build it).

```
cd desktop-client
dotnet build YouTubeDesktopClient.sln
dotnet run --project YouTubeDesktopClient
dotnet test
```

## Connect to YouTube (one-time, ~5 min)

### 1. First launch

Run the app once. With no credentials configured it drops a template at

```
%AppData%\YouTubeSubscriptionsToolkit\credentials.json
```

opens that folder in Explorer, and exits. Fill the file in (steps below), then relaunch.

### 2. Create a Google Cloud project

1. [Google Cloud Console](https://console.cloud.google.com/) → create a new project.
2. **APIs & Services → Library** → enable **YouTube Data API v3**.
3. **APIs & Services → OAuth consent screen**:
   - User type **External**, fill in name / support email.
   - **Scopes**: add `https://www.googleapis.com/auth/youtube.force-ssl` *(a superset of the
     plain `youtube` scope — required for posting comments; subscriptions / playlists /
     ratings still work)*.
   - **Test users**: add the Google account you'll sign in with.

### 3. Create the OAuth client

1. **APIs & Services → Credentials → Create credentials → OAuth client ID**.
2. Application type: **Desktop app**.
3. Create, then copy both the **Client ID** and the **Client secret**.

   > Google's own docs note the client secret for a Desktop app "is not treated as a
   > secret" — the app uses Authorization Code + PKCE on a loopback redirect. It's still
   > per-user config, which is why it isn't in the repo.

### 4. Fill in `credentials.json`

```json
{
  "clientId": "1234567890-abc….apps.googleusercontent.com",
  "clientSecret": "GOCSPX-…"
}
```

Save, relaunch the app. It opens your browser for the OAuth consent, then signs in.

> Because the project stays in **"Testing"**, Google shows an *"unverified app"* warning on the
> consent screen — click **Advanced → Go to … (unsafe)**. That's expected for your own project.

## Where runtime data lives

`%AppData%\YouTubeSubscriptionsToolkit\` — `credentials.json` (yours, git-ignored),
`token.bin` (DPAPI-encrypted refresh token), `account.json`, `log.txt`, `WebView2\`, and
`accounts\{channelId}\` (per-account `settings.json` + `cache.json`). Delete `accounts\` +
`token.bin` + `account.json` to reset; `log.txt` is the first place to look when something
misbehaves.
