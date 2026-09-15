(() => {
    const tabs = Array.from(
        document.querySelectorAll(".pp-login__tab")
    );

    function activateTab(tab) {
        tabs.forEach(item => {
            const active = item === tab;

            item.classList.toggle("is-active", active);
            item.setAttribute("aria-selected", String(active));
            item.tabIndex = active ? 0 : -1;

            const panel = document.getElementById(
                item.getAttribute("aria-controls")
            );

            if (panel) {
                panel.hidden = !active;
            }
        });
    }

    tabs.forEach((tab, index) => {
        tab.addEventListener("click", () => activateTab(tab));

        tab.addEventListener("keydown", event => {
            let next;

            if (event.key === "ArrowRight") {
                next = (index + 1) % tabs.length;
            } else if (event.key === "ArrowLeft") {
                next = (index - 1 + tabs.length) % tabs.length;
            } else if (event.key === "Home") {
                next = 0;
            } else if (event.key === "End") {
                next = tabs.length - 1;
            } else {
                return;
            }

            event.preventDefault();
            activateTab(tabs[next]);
            tabs[next].focus();
        });
    });

    const password = document.getElementById("Password");
    const toggle = document.getElementById("toggle-password");

    toggle?.addEventListener("click", () => {
        if (!password) return;

        const visible = password.type === "password";

        password.type = visible ? "text" : "password";
        toggle.textContent = visible ? "Hide" : "Show";
        toggle.setAttribute(
            "aria-label",
            visible ? "Hide password" : "Show password"
        );
        toggle.setAttribute("aria-pressed", String(visible));
    });
})();