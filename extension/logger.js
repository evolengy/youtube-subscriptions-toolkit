// A small persistent activity log — a ring buffer in chrome.storage.local, so
// it survives the MV3 service worker being torn down (its DevTools console
// doesn't). ES module: used by background.js and logs.html. Surface: logs.html.

const LOG_KEY = "logEntries";
const MAX_ENTRIES = 200;

// Pure — keep the last `cap` entries. Tested in logger.test.mjs.
export function trimLog(entries, cap = MAX_ENTRIES) {
  return entries.length > cap ? entries.slice(entries.length - cap) : entries;
}

async function append(level, msg) {
  const line = String(msg);
  (level === "error" ? console.error : console.log)("[YST]", line);
  try {
    const { [LOG_KEY]: entries } = await chrome.storage.local.get(LOG_KEY);
    const next = trimLog([...(entries || []), { ts: Date.now(), level, msg: line }]);
    await chrome.storage.local.set({ [LOG_KEY]: next });
  } catch (err) {
    console.error("[YST] logger.append failed", err);
  }
}

export const logger = {
  info: (msg) => append("info", msg),
  error: (msg) => append("error", msg),
};

export async function getEntries() {
  const { [LOG_KEY]: entries } = await chrome.storage.local.get(LOG_KEY);
  return entries || [];
}

export async function clearLog() {
  await chrome.storage.local.remove(LOG_KEY);
}

export const LOG_STORAGE_KEY = LOG_KEY;
