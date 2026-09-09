# YouTube Subscriptions Toolkit

[![extension](https://github.com/evolengy/youtube-subscriptions-toolkit/actions/workflows/extension.yml/badge.svg)](https://github.com/evolengy/youtube-subscriptions-toolkit/actions/workflows/extension.yml)
[![desktop-client](https://github.com/evolengy/youtube-subscriptions-toolkit/actions/workflows/desktop-client.yml/badge.svg)](https://github.com/evolengy/youtube-subscriptions-toolkit/actions/workflows/desktop-client.yml)

PocketTube-style subscription management on top of the official **YouTube Data API v3** —
group your subscriptions into folders, filter and sort the resulting feed, spot dead
channels, mute noise, and get notified about new uploads in the groups you care about.

Two independent implementations of the same idea live here:

| Folder | What it is | Setup |
|---|---|---|
| [`extension/`](extension/) | A Chrome MV3 extension. Adds a "My groups" section to youtube.com's own sidebar plus a set of management pages. No build step. | [extension/README.md](extension/README.md) |
| [`desktop-client/`](desktop-client/) | A standalone Windows app (.NET 10 / WPF) that re-implements the extension natively and adds an embedded player, so it never touches YouTube's DOM. | [desktop-client/README.md](desktop-client/README.md) |

They are **not** a client/server pair — pick whichever you prefer. `docs/` holds the design
spec the desktop client was originally built from.

## You must bring your own Google Cloud project

Both apps talk to the YouTube Data API with **your** OAuth client, not a shared one:

- The free API quota is **10,000 units/day**, counted per project. A shared client would run
  dry immediately.
- The OAuth consent screen for a personal project stays in **"Testing"** mode, which only lets
  Google accounts you explicitly add as *Test users* sign in — so a shared client id would be
  unusable by anyone else anyway.

Creating the project takes a few minutes and is free. Each app's README walks through it.
The repo ships placeholders (`REPLACE_WITH_YOUR_OAUTH_CLIENT_ID` / `_SECRET`); nothing works
until you replace them.

## License

MIT — see [LICENSE](LICENSE).
