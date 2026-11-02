// Shared presentation for extension pages and YouTube's Trusted-Types context.
(function (root) {
  function formatDuration(video) {
    if (video.liveBroadcastContent === 'live') return 'LIVE';
    const seconds = Math.floor(Number(video.durationSeconds));
    if (!Number.isFinite(seconds) || seconds <= 0) return '';
    const tail = String(seconds % 60).padStart(2, '0');
    const minutes = Math.floor(seconds / 60);
    return minutes >= 60
      ? `${Math.floor(minutes / 60)}:${String(minutes % 60).padStart(2, '0')}:${tail}`
      : `${minutes}:${tail}`;
  }

  function formatPublished(value, now = Date.now()) {
    if (!value) return '';
    const elapsed = now - Date.parse(value);
    if (!Number.isFinite(elapsed)) return '';
    if (elapsed < 0) return 'Scheduled';
    const minutes = Math.floor(elapsed / 60000);
    if (minutes < 1) return 'Just now';
    const [count, unit] = minutes < 60 ? [minutes, 'minute']
      : minutes < 1440 ? [Math.floor(minutes / 60), 'hour']
      : minutes < 43200 ? [Math.floor(minutes / 1440), 'day']
      : minutes < 525600 ? [Math.floor(minutes / 43200), 'month']
      : [Math.floor(minutes / 525600), 'year'];
    return `${count} ${unit}${count === 1 ? '' : 's'} ago`;
  }

  function addMetadata(video, { thumb, title, meta, channelTitle }) {
    const duration = formatDuration(video);
    if (duration) {
      const badge = document.createElement('span');
      badge.className = 'duration-badge';
      badge.textContent = duration;
      thumb.appendChild(badge);
    }
    title.className = 'video-title';
    title.href = thumb.href;
    title.target = thumb.target;
    title.rel = 'noopener';
    title.title = video.title;
    const channel = document.createElement(video.channelId ? 'a' : 'span');
    channel.className = 'video-channel';
    channel.textContent = channelTitle || 'Unknown channel';
    if (video.channelId) {
      channel.href = `https://www.youtube.com/channel/${encodeURIComponent(video.channelId)}`;
      channel.target = thumb.target;
      channel.rel = 'noopener';
    }
    const parts = [];
    if (Number.isFinite(video.viewCount)) parts.push(`${video.viewCount.toLocaleString()} views`);
    const published = formatPublished(video.publishedAt);
    if (published) {
      parts.push(published);
      meta.title = new Date(video.publishedAt).toLocaleString();
    }
    meta.append(parts.join(' · '));
    return channel;
  }

  function actionMenu(buttons, className) {
    const menu = document.createElement('details');
    menu.className = className;
    const trigger = document.createElement('summary');
    trigger.textContent = '⋮';
    trigger.setAttribute('aria-label', 'Video actions');
    const items = document.createElement('div');
    items.className = 'video-menu-items';
    for (const button of buttons) {
      button.append(document.createTextNode(button.getAttribute('aria-label')));
      button.addEventListener('click', () => { menu.open = false; });
      items.appendChild(button);
    }
    menu.append(trigger, items);
    return menu;
  }

  // Filtering and sorting happen before paging, over the entire cached feed.
  // Bound synchronous DOM construction instead of building thousands of hidden cards.
  function renderFeedPage(container, videos, buildCard, prefix) {
    const page = document.createElement('div');
    page.className = `${prefix}-feed-page`;
    const more = document.createElement('button');
    more.type = 'button';
    more.className = `${prefix}-show-more`;
    let cursor = 0;
    function appendBatch() {
      const end = Math.min(cursor + 60, videos.length);
      while (cursor < end) page.appendChild(buildCard(videos[cursor++]));
      more.hidden = cursor >= videos.length;
      more.textContent = `Show more (${videos.length - cursor} remaining)`;
    }
    more.addEventListener('click', appendBatch);
    container.replaceChildren(page, more);
    appendBatch();
  }

  const api = { formatDuration, formatPublished, addMetadata, actionMenu, renderFeedPage };
  root.YSTVideoPresentation = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof window !== 'undefined' ? window : globalThis);
