// Floating emoji picker for the group-icon feature. Reads emojiData.js
// (window.YSTEmojiData), publishes window.YSTEmoji. Used by both the dashboard
// and the content script; styling is emojiPicker.css.

(function (root) {
  const SEGMENTER =
    typeof Intl !== "undefined" && Intl.Segmenter
      ? new Intl.Segmenter(undefined, { granularity: "grapheme" })
      : null;

  // First grapheme cluster of a string — one emoji, even ZWJ sequences like
  // the family emoji. Trims first; "" for empty/nullish input.
  function firstEmoji(str) {
    const s = (str ?? "").trim();
    if (!s) return "";
    if (SEGMENTER) {
      for (const { segment } of SEGMENTER.segment(s)) return segment;
      return "";
    }
    return [...s][0] ?? "";
  }

  let openPanel = null;

  function closePicker() {
    if (!openPanel) return;
    openPanel.remove();
    document.removeEventListener("pointerdown", onDocPointer, true);
    document.removeEventListener("keydown", onKey, true);
    openPanel = null;
  }

  function onDocPointer(event) {
    if (openPanel && !openPanel.contains(event.target)) closePicker();
  }
  function onKey(event) {
    if (event.key === "Escape") closePicker();
  }

  function openEmojiPicker(anchorEl, options = {}) {
    const { onPick, onClear, current } = options;
    closePicker();

    const data = root.YSTEmojiData || [];
    const panel = document.createElement("div");
    panel.className = "yst-emoji-picker";

    const search = document.createElement("input");
    search.type = "search";
    search.placeholder = "Search emoji";
    search.className = "yst-emoji-search";
    panel.appendChild(search);

    if (current && onClear) {
      const clear = document.createElement("button");
      clear.type = "button";
      clear.className = "yst-emoji-clear";
      clear.textContent = "Remove icon";
      clear.addEventListener("click", () => {
        onClear();
        closePicker();
      });
      panel.appendChild(clear);
    }

    const grid = document.createElement("div");
    grid.className = "yst-emoji-grid";
    panel.appendChild(grid);

    const pick = (char) => {
      onPick?.(char);
      closePicker();
    };

    const render = (rawQuery) => {
      grid.replaceChildren();
      const query = rawQuery.trim().toLowerCase();

      for (const category of data) {
        const matches = query
          ? category.emoji.filter(([, name, keywords]) =>
              `${name} ${keywords}`.toLowerCase().includes(query)
            )
          : category.emoji;
        if (matches.length === 0) continue;

        if (!query) {
          const header = document.createElement("div");
          header.className = "yst-emoji-cat";
          header.textContent = category.label;
          grid.appendChild(header);
        }
        for (const [char, name] of matches) {
          const button = document.createElement("button");
          button.type = "button";
          button.className = "yst-emoji-btn";
          button.textContent = char;
          button.title = name;
          button.addEventListener("click", () => pick(char));
          grid.appendChild(button);
        }
      }

      if (!grid.children.length) {
        const none = document.createElement("div");
        none.className = "yst-emoji-none";
        none.textContent = "Nothing found — paste one and press Enter";
        grid.appendChild(none);
      }
    };

    search.addEventListener("input", () => render(search.value));
    search.addEventListener("keydown", (event) => {
      if (event.key !== "Enter") return;
      const custom = firstEmoji(search.value);
      // Treat as a pasted emoji only if it isn't plain latin/digit search text.
      if (custom && !/[a-z0-9]/i.test(custom)) pick(custom);
    });

    document.body.appendChild(panel);
    positionPanel(panel, anchorEl);
    render("");
    search.focus();

    openPanel = panel;
    // Defer so the click that opened the picker doesn't immediately close it.
    setTimeout(() => {
      document.addEventListener("pointerdown", onDocPointer, true);
      document.addEventListener("keydown", onKey, true);
    }, 0);
  }

  function positionPanel(panel, anchorEl) {
    const rect = anchorEl.getBoundingClientRect();
    panel.style.position = "fixed";
    panel.style.top =
      Math.max(8, Math.min(rect.bottom + 4, window.innerHeight - panel.offsetHeight - 8)) + "px";
    panel.style.left =
      Math.max(8, Math.min(rect.left, window.innerWidth - panel.offsetWidth - 8)) + "px";
  }

  const api = { openEmojiPicker, closePicker, firstEmoji };
  if (typeof module !== "undefined" && module.exports) module.exports = api;
  root.YSTEmoji = api;
})(typeof window !== "undefined" ? window : globalThis);
