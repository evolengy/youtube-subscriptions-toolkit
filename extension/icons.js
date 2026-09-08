// Tiny inline-SVG icon set for card action buttons. Stroke uses currentColor so
// buttons theme themselves. Shared by the dashboard and the content script
// (createElementNS is fine under YouTube's Trusted-Types CSP — only innerHTML
// string assignment is blocked). Publishes window.YSTIcons.

(function (root) {
  const NS = "http://www.w3.org/2000/svg";

  // name -> array of <path d> strings.
  const PATHS = {
    check: ["M4 12l5 5L20 7"],
    ban: ["M12 3a9 9 0 100 18 9 9 0 000-18z", "M6.3 6.3l11.4 11.4"],
    undo: ["M9 14 4 9l5-5", "M4 9h10.5a5.5 5.5 0 010 11H10"],
    thumb: ["M7 11v9", "M4 22h14a2 2 0 002-1.7l1-6a2 2 0 00-2-2.3h-5V5a2 2 0 00-3.4-1.4L7 11H4z"],
    eye: ["M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7-10-7-10-7z", "M12 15a3 3 0 100-6 3 3 0 000 6z"],
    eyeOff: [
      "M9.9 4.2A9.8 9.8 0 0112 4c6.5 0 10 7 10 7a17 17 0 01-3 3.7",
      "M6.1 6.1A17 17 0 002 11s3.5 7 10 7a9.8 9.8 0 004.1-.9",
      "M9.9 9.9a3 3 0 004.2 4.2",
      "M3 3l18 18",
    ],
  };

  function make(name, { size = 18 } = {}) {
    const svg = document.createElementNS(NS, "svg");
    svg.setAttribute("viewBox", "0 0 24 24");
    svg.setAttribute("width", String(size));
    svg.setAttribute("height", String(size));
    svg.setAttribute("fill", "none");
    svg.setAttribute("stroke", "currentColor");
    svg.setAttribute("stroke-width", "2");
    svg.setAttribute("stroke-linecap", "round");
    svg.setAttribute("stroke-linejoin", "round");
    svg.setAttribute("aria-hidden", "true");
    for (const d of PATHS[name] || []) {
      const path = document.createElementNS(NS, "path");
      path.setAttribute("d", d);
      svg.appendChild(path);
    }
    return svg;
  }

  root.YSTIcons = { make };
  if (typeof module !== "undefined" && module.exports) module.exports = { make, PATHS };
})(typeof window !== "undefined" ? window : globalThis);
