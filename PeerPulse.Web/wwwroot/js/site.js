(() => {
    "use strict";

    const themeKey = "peerpulse-dark-mode";

    function getSavedTheme() {
        try {
            return localStorage.getItem(themeKey) === "true";
        } catch {
            return false;
        }
    }

    // Apply the theme before the page is displayed.
    document.documentElement.classList.toggle(
        "dark",
        getSavedTheme()
    );

    document.addEventListener("alpine:init", () => {
        Alpine.data("ppTheme", () => ({
            darkMode: document.documentElement.classList.contains("dark"),

            init() {
                this.$watch("darkMode", value => {
                    document.documentElement.classList.toggle(
                        "dark",
                        value
                    );

                    try {
                        localStorage.setItem(
                            themeKey,
                            String(value)
                        );
                    } catch {
                        // Theme switching still works without storage.
                    }
                });
            }
        }));
    });

    function initializeSearchShortcut() {
        const searchInput = document.getElementById("search-input");

        if (!searchInput) {
            return;
        }

        document.addEventListener("keydown", event => {
            if (event.defaultPrevented) {
                return;
            }

            // Search is hidden on smaller screens.
            if (searchInput.getClientRects().length === 0) {
                return;
            }

            const target = event.target;

            const isTyping =
                target instanceof Element &&
                target.closest(
                    "input, textarea, select, [contenteditable]"
                );

            const commandShortcut =
                (event.ctrlKey || event.metaKey) &&
                event.key.toLowerCase() === "k";

            const slashShortcut =
                event.key === "/" &&
                !event.ctrlKey &&
                !event.metaKey &&
                !event.altKey &&
                !isTyping;

            if (!commandShortcut && !slashShortcut) {
                return;
            }

            event.preventDefault();
            searchInput.focus();
        });
    }

    if (document.readyState === "loading") {
        document.addEventListener(
            "DOMContentLoaded",
            initializeSearchShortcut,
            { once: true }
        );
    } else {
        initializeSearchShortcut();
    }
})();