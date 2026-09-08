// Ephemeral toast notifications — shared by every extension page (ES module).
// Self-contained: injects its own <style> and container on first use, so a page
// only needs `import { toast } from "./toast.js"`.

const TIMEOUTS = { error: 8000, success: 3500, info: 4500 };

// Pure — trim a message to one readable line for the toast (the full text,
// e.g. an API error's JSON body, stays in the activity log). Tested.
export function summarize(message, max = 140) {
  const firstLine = String(message).split("\n")[0].trim();
  return firstLine.length > max ? `${firstLine.slice(0, max - 1)}…` : firstLine;
}

let container = null;

function ensureContainer() {
  if (container) return container;

  const style = document.createElement("style");
  style.id = "yst-toast-style";
  style.textContent = `
    #yst-toasts {
      position: fixed; z-index: 2147483647;
      right: 16px; bottom: 16px;
      display: flex; flex-direction: column; gap: 8px;
      max-width: min(380px, calc(100vw - 32px));
    }
    .yst-toast {
      background: #202020; color: #f1f1f1;
      border: 1px solid #383838; border-left: 4px solid #888;
      border-radius: 8px; padding: 10px 12px;
      font: 13px/1.4 -apple-system, "Segoe UI", Roboto, sans-serif;
      box-shadow: 0 6px 20px rgba(0,0,0,0.45);
      cursor: pointer; word-break: break-word;
      animation: yst-toast-in 140ms ease-out;
    }
    .yst-toast.error   { border-left-color: #ff4e45; }
    .yst-toast.success { border-left-color: #3fbf5f; }
    .yst-toast.info    { border-left-color: #3ea6ff; }
    .yst-toast.leaving { opacity: 0; transform: translateX(8px); transition: opacity 160ms, transform 160ms; }
    @keyframes yst-toast-in { from { opacity: 0; transform: translateX(8px); } }
  `;
  document.head.appendChild(style);

  container = document.createElement("div");
  container.id = "yst-toasts";
  document.body.appendChild(container);
  return container;
}

export function showToast(message, { type = "info", timeout } = {}) {
  const root = ensureContainer();

  const el = document.createElement("div");
  el.className = `yst-toast ${type}`;
  el.textContent = summarize(message);
  el.title = "Click to dismiss";

  const remove = () => {
    if (!el.isConnected) return;
    el.classList.add("leaving");
    setTimeout(() => el.remove(), 200);
  };
  el.addEventListener("click", remove);
  setTimeout(remove, timeout ?? TIMEOUTS[type] ?? TIMEOUTS.info);

  root.appendChild(el);
}

export const toast = {
  error: (m) => showToast(m, { type: "error" }),
  success: (m) => showToast(m, { type: "success" }),
  info: (m) => showToast(m, { type: "info" }),
};
