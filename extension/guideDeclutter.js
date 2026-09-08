// Builds the CSS that hides chosen sections of YouTube's own left guide.
// Injected as one <style> element by content-groups.js from the `guideHidden`
// setting; rebuilt on change. CSS-only on purpose — YouTube's guide is a
// Polymer dom-repeat that fights DOM edits (and froze the tab once), but it
// never touches a <style> we add to <head>, and `display:none` applies to
// whatever it re-renders.
//
// Selectors anchor on stable hrefs, not visible text: the section titles are
// localized ("Вы" / "Навигатор" / "Другие возможности"), the hrefs aren't.
// `:has()` lifts the match from an entry up to its whole section container.
// A selector that matches nothing (a section absent on this page / rollout) is
// harmless, so one fixed toggle list covers every layout.
//
// Pure: no DOM, no chrome.* — run under `node --test guideDeclutter.test.js`.

(function (root) {
  // YouTube Music / Live "topic channel" ids are global constants.
  const YT_MUSIC_CHANNEL = "UC-9-kyTW8ZkZNDHQJ6FgpwQ";
  const YT_LIVE_CHANNEL = "UC4R8DWoMoI7CAwX8_LjQHig";

  const SELECTORS = {
    // Top section: the Shorts entry (no href; "Shorts" is a brand name, not
    // translated, so the title match is stable).
    shorts: 'ytd-guide-entry-renderer:has(a[title="Shorts"])',

    // The flat channel list under "Subscriptions" — every entry in that section
    // except the "Subscriptions" link itself (so the link stays).
    subChannels:
      'ytd-guide-section-renderer:has(a[href="/feed/subscriptions"]) ' +
      'ytd-guide-entry-renderer:not(:has(a[href="/feed/subscriptions"]))',

    // "You": History, Playlists, Watch later, Liked, Your videos, Downloads.
    you: 'ytd-guide-section-renderer:has(a[href="/feed/history"])',

    // "Explore" ("Навигатор"): Music, Movies, Live.
    explore:
      'ytd-guide-section-renderer:has(' +
      `a[href="/channel/${YT_MUSIC_CHANNEL}"], ` +
      'a[href^="/feed/storefront"], ' +
      `a[href="/channel/${YT_LIVE_CHANNEL}"])`,

    // "More from YouTube": YouTube Music (app), YouTube Kids, Premium.
    moreFromYoutube:
      'ytd-guide-section-renderer:has(' +
      'a[href^="https://music.youtube.com"], ' +
      'a[href^="https://www.youtubekids.com"])',

    // Footer: the About / Press / Terms links, and the "Report history" entry.
    footer:
      'ytd-guide-renderer #footer, ' +
      'ytd-guide-section-renderer:has(a[href="/reporthistory"])',
  };

  function buildGuideCss(settings) {
    const active = [];
    for (const [key, selector] of Object.entries(SELECTORS)) {
      if (settings && settings[key]) active.push(selector);
    }
    if (!active.length) return "";
    return `${active.join(",\n")} {\n  display: none !important;\n}\n`;
  }

  const api = { buildGuideCss, SELECTORS };

  if (typeof module !== "undefined" && module.exports) module.exports = api; // node test
  root.YSTDeclutter = api; // browser (content script + settings page)
})(typeof window !== "undefined" ? window : globalThis);
