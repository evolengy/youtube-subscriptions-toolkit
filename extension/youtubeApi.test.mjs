// Run: node --test youtubeApi.test.mjs
import test from "node:test";
import assert from "node:assert/strict";
import { fetchVideosDetails, __setSleepForTests } from "./youtubeApi.js";

__setSleepForTests(() => Promise.resolve()); // no real backoff waits in tests

function jsonResponse(body, status = 200) {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: () => Promise.resolve(body),
    text: () => Promise.resolve(JSON.stringify(body)),
  };
}

const oneVideo = {
  items: [
    {
      id: "v1",
      snippet: { channelId: "c1", title: "V", publishedAt: "2026-01-01T00:00:00Z", thumbnails: {}, liveBroadcastContent: "none" },
      contentDetails: { duration: "PT1M" },
      statistics: { viewCount: "10" },
    },
  ],
};

test("apiFetch retries a transient 500 and then succeeds", async () => {
  let calls = 0;
  globalThis.fetch = () => {
    calls++;
    return Promise.resolve(calls < 3 ? jsonResponse({ error: "boom" }, 500) : jsonResponse(oneVideo));
  };
  const out = await fetchVideosDetails("tok", ["v1"]);
  assert.equal(calls, 3);
  assert.equal(out[0].videoId, "v1");
});

test("apiFetch gives up after the retry limit and throws", async () => {
  let calls = 0;
  globalThis.fetch = () => {
    calls++;
    return Promise.resolve(jsonResponse({ error: "still boom" }, 503));
  };
  await assert.rejects(() => fetchVideosDetails("tok", ["v1"]), /503/);
  assert.equal(calls, 3);
});

test("apiFetch does NOT retry a 403 (quota / permission)", async () => {
  let calls = 0;
  globalThis.fetch = () => {
    calls++;
    return Promise.resolve(jsonResponse({ error: "quotaExceeded" }, 403));
  };
  await assert.rejects(() => fetchVideosDetails("tok", ["v1"]), /403/);
  assert.equal(calls, 1);
});

test("apiFetch retries a thrown network error", async () => {
  let calls = 0;
  globalThis.fetch = () => {
    calls++;
    if (calls < 2) return Promise.reject(new TypeError("Failed to fetch"));
    return Promise.resolve(jsonResponse(oneVideo));
  };
  const out = await fetchVideosDetails("tok", ["v1"]);
  assert.equal(calls, 2);
  assert.equal(out.length, 1);
});
