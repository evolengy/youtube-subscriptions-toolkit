// Shows the subscribed channel's country next to its name on watch pages.
// YouTube is a single-page app, so we re-run on its internal navigation
// event instead of relying on a fresh page load per video.

const BADGE_CLASS = "yst-location-badge";

function getChannelId() {
  const meta = document.querySelector('meta[itemprop="channelId"]');
  return meta?.content ?? null;
}

function findOwnerNameContainer() {
  // The channel name link lives inside ytd-video-owner-renderer; anchoring
  // the badge there keeps it away from the (more volatile) description box.
  return document.querySelector("ytd-video-owner-renderer #channel-name");
}

async function requestCountry(channelId) {
  const response = await chrome.runtime.sendMessage({ type: "GET_CHANNEL_COUNTRY", channelId });
  return response?.country ?? null;
}

async function renderBadge() {
  const channelId = getChannelId();
  const container = findOwnerNameContainer();
  if (!channelId || !container) return;

  document.querySelectorAll(`.${BADGE_CLASS}`).forEach((el) => el.remove());

  const country = await requestCountry(channelId);
  const badge = document.createElement("span");
  badge.className = BADGE_CLASS;
  badge.textContent = country ?? "Location unknown";
  container.appendChild(badge);
}

function scheduleRender() {
  // The owner element isn't in the DOM yet on first paint of a fresh
  // navigation; poll briefly instead of adding a full MutationObserver.
  let attempts = 0;
  const timer = setInterval(() => {
    attempts += 1;
    if (findOwnerNameContainer() || attempts > 20) {
      clearInterval(timer);
      renderBadge().catch(console.error);
    }
  }, 250);
}

scheduleRender();
document.addEventListener("yt-navigate-finish", scheduleRender);
