/*! pp-msg: reusable popup messages for PeerPulse. Styles are inline, so no CSS build is required.
   ppMsg.success("Saved.")  ppMsg.warning("...")  ppMsg.error("...")  ppMsg.info("...")
   Options: { title, duration (ms, 0 = stays until closed) } */
(() => {
    "use strict";

    if (window.ppMsg) return;

    const MAX_VISIBLE = 4;
    const DURATION = { success: 4000, info: 5000, warning: 7000, error: 9000 };
    const reduceMotion = () => !!window.matchMedia?.("(prefers-reduced-motion: reduce)").matches;

    const THEMES = {
        success: { title: "Success", bg: "#047857", fg: "#ffffff", icon: "rgba(255,255,255,0.22)", bar: "rgba(255,255,255,0.7)", hover: "rgba(255,255,255,0.2)", path: "M5 13l4 4L19 7" },
        warning: { title: "Warning", bg: "#fbbf24", fg: "#111827", icon: "rgba(17,24,39,0.14)", bar: "rgba(17,24,39,0.5)", hover: "rgba(17,24,39,0.14)", path: "M12 9v4m0 4h.01M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z" },
        error: { title: "Error", bg: "#dc2626", fg: "#ffffff", icon: "rgba(255,255,255,0.22)", bar: "rgba(255,255,255,0.7)", hover: "rgba(255,255,255,0.2)", path: "M6 18L18 6M6 6l12 12" },
        info: { title: "Information", bg: "#114fa2", fg: "#ffffff", icon: "rgba(255,255,255,0.22)", bar: "rgba(255,255,255,0.7)", hover: "rgba(255,255,255,0.2)", path: "M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" }
    };

    const css = (node, styles) => Object.assign(node.style, styles);
    const active = new Map();      // key -> { id, close, restart }
    let counter = 0;

    function getContainer() {
        let box = document.getElementById("pp-msg-container");
        if (box) return box;

        box = document.createElement("div");
        box.id = "pp-msg-container";

        css(box, {
            position: "fixed", inset: "auto", top: "16px", right: "16px",
            width: "min(26rem, calc(100vw - 2rem))", margin: "0", padding: "0", border: "0",
            background: "transparent", overflow: "visible", display: "flex",
            flexDirection: "column", gap: "12px", zIndex: "2147483647", pointerEvents: "none"
        });

        if ("popover" in HTMLElement.prototype) box.setAttribute("popover", "manual");
        document.body.appendChild(box);
        return box;
    }

    // While a modal <dialog> is open the page behind it is inert, so the container moves inside it.
    function place(box) {
        const host = document.querySelector("dialog[open]") || document.body;
        if (box.parentElement !== host) host.appendChild(box);

        if (typeof box.showPopover === "function") {
            try {
                if (box.matches(":popover-open")) box.hidePopover();
                box.showPopover();
            } catch { /* z-index still applies */ }
        }
    }

    document.addEventListener("close", event => {
        if (event.target instanceof HTMLDialogElement) {
            const box = document.getElementById("pp-msg-container");
            if (box && box.parentElement !== document.body) place(box);
        }
    }, true);

    function show(options = {}) {
        const type = THEMES[options.type] ? options.type : "info";
        const text = String(options.text ?? "").trim().slice(0, 600);
        if (!text) return null;

        const theme = THEMES[type];
        const title = options.title === undefined ? theme.title : String(options.title).slice(0, 120);
        const duration = options.duration === undefined ? DURATION[type] : Math.max(0, Number(options.duration) || 0);

        const key = `${type}|${title}|${text}`;
        const box = getContainer();

        // The same message is already visible: restart its timer instead of stacking copies.
        const same = active.get(key);
        if (same) { same.restart(); place(box); return same.id; }

        while (active.size >= MAX_VISIBLE) active.values().next().value.close(true);

        const id = ++counter;

        const root = document.createElement("div");
        root.setAttribute("role", type === "error" || type === "warning" ? "alert" : "status");
        css(root, {
            pointerEvents: "auto", background: theme.bg, color: theme.fg, borderRadius: "12px",
            boxShadow: "0 10px 30px rgba(0,0,0,0.28)", overflow: "hidden",
            fontFamily: "inherit", fontSize: "14px", lineHeight: "20px"
        });

        const row = document.createElement("div");
        css(row, { display: "flex", alignItems: "flex-start", gap: "12px", padding: "14px 16px" });

        const icon = document.createElement("span");
        css(icon, {
            width: "32px", height: "32px", borderRadius: "50%", background: theme.icon,
            display: "grid", placeItems: "center", flexShrink: "0"
        });
        icon.innerHTML =
            '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.25" ' +
            'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="' + theme.path + '"/></svg>';

        const body = document.createElement("div");
        css(body, { flex: "1", minWidth: "0" });

        if (title) {
            const heading = document.createElement("p");
            css(heading, { margin: "0", fontWeight: "700" });
            heading.textContent = title;
            body.append(heading);
        }

        const line = document.createElement("p");
        css(line, { margin: title ? "2px 0 0" : "0", fontWeight: "500", wordBreak: "break-word" });
        line.textContent = text;
        body.append(line);

        const closeBtn = document.createElement("button");
        closeBtn.type = "button";
        closeBtn.setAttribute("aria-label", "Dismiss message");
        css(closeBtn, {
            background: "transparent", border: "0", color: "inherit", cursor: "pointer",
            padding: "4px", borderRadius: "6px", opacity: "0.85", flexShrink: "0", margin: "-4px -4px 0 0",
            lineHeight: "0"
        });
        closeBtn.innerHTML =
            '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" ' +
            'stroke-linecap="round" aria-hidden="true"><path d="M6 18L18 6M6 6l12 12"/></svg>';
        closeBtn.addEventListener("mouseenter", () => { closeBtn.style.background = theme.hover; closeBtn.style.opacity = "1"; });
        closeBtn.addEventListener("mouseleave", () => { closeBtn.style.background = "transparent"; closeBtn.style.opacity = "0.85"; });

        row.append(icon, body, closeBtn);
        root.append(row);

        let bar = null;
        if (duration > 0) {
            bar = document.createElement("div");
            css(bar, { height: "4px", width: "100%", background: theme.bar, transformOrigin: "left" });
            root.append(bar);
        }

        let progress = null;
        let closed = false;

        const dismiss = immediate => {
            if (closed) return;
            closed = true;
            progress?.cancel();
            active.delete(key);

            if (immediate || reduceMotion()) { root.remove(); return; }

            root.animate(
                [{ opacity: 1, transform: "translateX(0)" }, { opacity: 0, transform: "translateX(16px)" }],
                { duration: 180, easing: "ease-in", fill: "forwards" })
                .finished.then(() => root.remove()).catch(() => root.remove());
        };

        const start = () => {
            if (!bar) return;
            progress?.cancel();
            progress = bar.animate(
                [{ transform: "scaleX(1)" }, { transform: "scaleX(0)" }],
                { duration, easing: "linear", fill: "forwards" });
            progress.finished.then(() => dismiss(false)).catch(() => { /* cancelled */ });
        };

        closeBtn.addEventListener("click", () => dismiss(false));
        root.addEventListener("mouseenter", () => progress?.pause());
        root.addEventListener("mouseleave", () => { if (!closed) progress?.play(); });
        root.addEventListener("focusin", () => progress?.pause());
        root.addEventListener("focusout", () => { if (!closed) progress?.play(); });

        box.append(root);
        place(box);

        if (!reduceMotion()) {
            root.animate(
                [{ opacity: 0, transform: "translateY(-8px)" }, { opacity: 1, transform: "translateY(0)" }],
                { duration: 200, easing: "ease-out" });
        }

        active.set(key, { id, close: dismiss, restart: () => { if (!closed) start(); } });
        start();

        return id;
    }

    window.ppMsg = Object.freeze({
        show,
        success: (text, options) => show({ ...options, type: "success", text }),
        warning: (text, options) => show({ ...options, type: "warning", text }),
        error: (text, options) => show({ ...options, type: "error", text }),
        info: (text, options) => show({ ...options, type: "info", text }),
        dismiss(id) { for (const item of active.values()) if (item.id === id) item.close(false); },
        clear() { [...active.values()].forEach(item => item.close(true)); }
    });

    // Messages rendered by the server (see _PpMsg.cshtml).
    const flushServerMessages = () => {
        document.querySelectorAll("[data-pp-msg]").forEach(node => {
            show({ type: node.dataset.type || "info", title: node.dataset.title || undefined, text: node.dataset.text || "" });
            node.remove();
        });
    };

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", flushServerMessages);
    else flushServerMessages();
})();