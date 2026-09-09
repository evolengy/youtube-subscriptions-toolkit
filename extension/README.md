# YouTube Subscriptions Toolkit — Chrome extension

A Chrome MV3 extension (vanilla JS, no build step). It adds a **My groups** section to
youtube.com's own left sidebar and a set of management pages opened from a dashboard:

- **Groups** — sort subscriptions into folders with custom emoji icons; the feed and the
  sidebar overlay filter to the selected group.
- **Feed** — filter by type (Video / Short / Live), duration, upload date, and text; sort by
  date / duration / views. "Hide watched" on by default, watched videos get a red progress
  bar, per-group **"N new"** badges.
- **Liked / Not interested** — sync your Liked playlist (read-only) and keep an extension-only
  "not interested" list; both appear as pseudo-groups showing the full list.
- **Feed blocklist** — hide videos by title keyword or mute a channel (without unsubscribing).
- **Channels** — an activity/health table (Active / Quiet / Dormant / Dead) with one-click
  unsubscribe for dead channels.
- **Settings** — hide sections of YouTube's own left menu (Shorts, the channel list, "You",
  "Explore", "More from YouTube", footer).
- **Notifications** — opt a group in and get a desktop toast when a background sync finds new
  uploads in it.
- **Activity log** — the last 200 sync results and errors, in `logs.html`.

Feature status (Russian) is in [STATUS.md](STATUS.md).

---

## Install & connect (one-time, ~5 min)

### 1. Load the extension

1. Clone this repo.
2. Open `chrome://extensions`, turn on **Developer mode** (top-right).
3. **Load unpacked** → select this `extension/` folder.
4. Note the **ID** Chrome shows for the extension — you need it in step 3.

> The extension ID is derived from the folder path (there is no `"key"` in the manifest).
> If you move or re-clone the repo, the ID changes and you must update it in the Cloud
> Console (step 3).

### 2. Create a Google Cloud project

1. [Google Cloud Console](https://console.cloud.google.com/) → create a new project.
2. **APIs & Services → Library** → enable **YouTube Data API v3**.
3. **APIs & Services → OAuth consent screen**:
   - User type **External**, fill in the required name / support email.
   - **Scopes**: you can leave this empty (the extension requests its scope at runtime).
   - **Test users**: add the Google account you'll sign in with. *(Only test users can use a
     project that stays in "Testing" — that's fine for personal use.)*

### 3. Create the OAuth client

1. **APIs & Services → Credentials → Create credentials → OAuth client ID**.
2. Application type: **Chrome Extension**.
3. **Item ID**: paste the extension ID from step 1.4.
4. Create, then copy the **Client ID** (looks like `1234567890-abc….apps.googleusercontent.com`).

### 4. Wire it up

1. Open [`manifest.json`](manifest.json), replace
   `REPLACE_WITH_YOUR_OAUTH_CLIENT_ID.apps.googleusercontent.com` in `oauth2.client_id` with
   your Client ID.
2. Optionally keep that local edit out of `git status`:
   ```
   git update-index --skip-worktree extension/manifest.json
   ```
   (run `--no-skip-worktree` to undo.)
3. `chrome://extensions` → **Reload** the extension.
4. Click the toolbar icon to open the dashboard → **Sign in with Google**.

### If sign-in fails with `bad client id`

The extension ID and the Cloud Console **Item ID** no longer match (you moved the folder, or
re-cloned). Copy the current ID from `chrome://extensions` into the OAuth client's Item ID
field in the Console. No code change needed.

## Tests

Pure-logic modules are covered by Node's built-in test runner (no dependencies):

```
cd extension
node --test
```
