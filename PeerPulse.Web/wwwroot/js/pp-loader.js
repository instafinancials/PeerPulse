/*! pp-loader: reusable loaders for PeerPulse.
   ppLoader.show("Loading…") / ppLoader.hide()      full-page overlay (calls can be nested)
   await ppLoader.wrap(promiseOrFunction, "Saving…")  show, run, always hide
   ppLoader.button(button, true, "Saving…")          spinner inside a button; false restores it */
(() => {
    "use strict";

    if (window.ppLoader) return;

    const SHOW_DELAY = 200;      // ms before the overlay appears
    const MIN_VISIBLE = 350;     // ms the overlay stays once shown, to avoid flicker

    const css = (node, styles) => Object.assign(node.style, styles);

    let count = 0;
    let overlay = null;
    let label = null;
    let showTimer = null;
    let hideTimer = null;
    let shownAt = 0;

    function spinnerSvg(size) {
        const holder = document.createElement("span");
        holder.innerHTML =
            `<svg width="${size}" height="${size}" viewBox="0 0 24 24" fill="none" aria-hidden="true">` +
            '<circle cx="12" cy="12" r="10" stroke="currentColor" stroke-opacity="0.25" stroke-width="4"/>' +
            '<path d="M4 12a8 8 0 018-8" stroke="currentColor" stroke-width="4" stroke-linecap="round"/></svg>';

        const svg = holder.firstChild;
        svg.animate([{ transform: "rotate(0deg)" }, { transform: "rotate(360deg)" }],
            { duration: 800, iterations: Infinity });
        return svg;
    }

    const isDark = () =>
        document.documentElement.classList.contains("dark") || document.body.classList.contains("dark");

    function build() {
        overlay = document.createElement("div");
        overlay.setAttribute("role", "status");
        overlay.setAttribute("aria-live", "polite");

        css(overlay, {
            position: "fixed", inset: "0", display: "none", alignItems: "center", justifyContent: "center",
            background: "rgba(15, 23, 42, 0.45)", zIndex: "2147483000"
        });

        const card = document.createElement("div");
        css(card, {
            display: "flex", alignItems: "center", gap: "14px", padding: "20px 28px", borderRadius: "14px",
            boxShadow: "0 20px 40px rgba(0,0,0,0.3)", fontFamily: "inherit", fontSize: "14px", fontWeight: "600"
        });

        const spin = spinnerSvg(28);
        css(spin, { color: "#114fa2", flexShrink: "0" });

        label = document.createElement("span");
        card.append(spin, label);
        overlay.append(card);
        overlay.dataset.card = "1";
        document.body.appendChild(overlay);

        overlay._card = card;
    }

    function paint() {
        const dark = isDark();
        css(overlay._card, { background: dark ? "#1f2937" : "#ffffff", color: dark ? "#f3f4f6" : "#111827" });
    }

    function show(text = "Loading…") {
        count++;
        if (!overlay) build();

        label.textContent = text;
        clearTimeout(hideTimer);

        if (overlay.style.display === "none" && !showTimer) {
            showTimer = setTimeout(() => {
                showTimer = null;
                if (count > 0) {
                    paint();
                    overlay.style.display = "flex";
                    shownAt = Date.now();
                }
            }, SHOW_DELAY);
        }
    }

    function hide() {
        count = Math.max(0, count - 1);
        if (count > 0) return;

        clearTimeout(showTimer);
        showTimer = null;

        if (!overlay || overlay.style.display === "none") return;

        const wait = Math.max(0, MIN_VISIBLE - (Date.now() - shownAt));
        hideTimer = setTimeout(() => { overlay.style.display = "none"; }, wait);
    }

    async function wrap(task, text) {
        show(text);
        try {
            return await (typeof task === "function" ? task() : task);
        } finally {
            hide();
        }
    }

    const saved = new WeakMap();

    function button(btn, on, text) {
        if (!btn) return;

        if (on) {
            if (saved.has(btn)) return;
            saved.set(btn, [...btn.childNodes]);

            const spin = spinnerSvg(16);
            css(spin, { display: "inline-block", verticalAlign: "middle", marginRight: "8px", flexShrink: "0" });

            const textNode = document.createElement("span");
            textNode.textContent = text || "Please wait…";

            btn.replaceChildren(spin, textNode);
            btn.setAttribute("aria-busy", "true");
        } else {
            const nodes = saved.get(btn);
            if (!nodes) return;

            btn.replaceChildren(...nodes);
            btn.removeAttribute("aria-busy");
            saved.delete(btn);
        }
    }

    window.ppLoader = Object.freeze({ show, hide, wrap, button });
})();