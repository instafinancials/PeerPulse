(() => {
    "use strict";

    const root = document.querySelector("[data-peer-page]");
    if (!root) return;

    const page = root.dataset.peerPage;
    const $ = id => root.querySelector(`#${id}`);

    const el = (tag, className, value) => {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (value != null) node.textContent = value;
        return node;
    };

    const esc = value => String(value ?? "").replace(/[&<>"']/g, c =>
    ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));

    const fmtDate = value => value
        ? new Date(value).toLocaleDateString("en-GB", { day: "2-digit", month: "short", year: "numeric" })
        : "—";

    const fmtMoney = value => {
        if (value == null) return "—";
        const n = Number(value);
        return n >= 100000 ? `₹${(n / 10000000).toFixed(2)} Cr` : `₹${n.toLocaleString("en-IN")}`;
    };

    const safeUrl = value => {
        if (!value) return null;
        try {
            const url = new URL(/^https?:\/\//i.test(value) ? value : `https://${value}`);
            return /^https?:$/.test(url.protocol) ? url.href : null;
        } catch { return null; }
    };

    // ------------------------------------------------------------------ service addresses
    // Normally every address comes from a data-* attribute written by the view. If one is missing,
    // it is built from the current page address (/PeerDiscovery/Manage -> /PeerDiscovery/Workspace).
    const ACTIONS = {
        base: "Workspace", search: "Search", add: "AddPeer", updatePeer: "UpdatePeer",
        deactivatePeer: "DeactivatePeer", reactivatePeer: "ReactivatePeer", createGroup: "CreateGroup",
        updateGroup: "UpdateGroup", deleteGroup: "DeleteGroup", manage: "Manage"
    };

    const warnedKeys = new Set();

    function endpoint(key) {
        const configured = root.dataset[key];
        if (configured) return configured;

        const action = ACTIONS[key];
        const match = location.pathname.replace(/\/+$/, "").match(/^(.*\/PeerDiscovery)\/[^/]+$/i);
        if (!action || !match) return null;

        if (!warnedKeys.has(key)) {
            warnedKeys.add(key);
            console.warn(`PeerPulse: data-${key.replace(/[A-Z]/g, c => "-" + c.toLowerCase())} is missing in the view; using ${match[1]}/${action}.`);
        }

        return `${match[1]}/${action}`;
    }

    // ------------------------------------------------------------------ state
    let workspace = null;
    let searchId = null;
    let retrySearch = null;
    let lastSearch = null;
    let currentRecord = null;
    let currentExisting = null;
    let editingGroup = null;
    let pendingPeer = null;
    let confirmAction = null;

    const selected = new Set();
    const selectedPeerIds = new Set();
    let peerView = "table";
    let sortAscending = true;

    // ------------------------------------------------------------------ messages (pp-msg)
    const KIND_BY_STATUS = {
        Invalid: "warning", GroupRequired: "warning", SearchRequired: "warning",
        LimitReached: "warning", PeerLimitReached: "warning", NoSubscription: "warning",
        UnknownPlan: "warning", Conflict: "warning", NotFound: "warning"
    };

    function notify(text, kind = "info", options = {}) {
        if (!text) return;
        if (window.ppMsg) window.ppMsg.show({ ...options, type: kind, text });
        else console.warn("pp-msg.js is not loaded:", kind, text);
    }

    function failure(text, kind = "error", silent = false, traceId = null) {
        const error = new Error(text);
        error.kind = kind;
        error.silent = silent;
        error.traceId = traceId;
        return error;
    }

    // Session expired: show the popup, then send the user to the login page (pp-session.js).
    function sessionExpired(loginUrl, returnUrlParameter) {
        if (window.ppSession) {
            window.ppSession.expired(loginUrl, returnUrlParameter);
            return failure("Your session has expired.", "warning", true);
        }
        return failure("Your session has expired. Reload the page and sign in again.", "warning");
    }

    function describeHttp(response) {
        let path = "";
        try { path = new URL(response.url).pathname; } catch { /* ignore */ }

        if (response.status === 403)
            return "You do not have access to this feature.";
        if (response.status === 400)
            return "The request was rejected because the security token is missing or expired. Reload the page and try again.";
        if (response.status === 404)
            return `The service address ${path} was not found. Please contact support.`;
        if (response.status >= 500)
            return `The server could not complete the request (error ${response.status}). Please try again.`;
        return `Unexpected server response (${response.status}). Reload the page and try again.`;
    }

    // ------------------------------------------------------------------ server calls
    async function api(url, values) {
        if (!url) {
            console.error("PeerPulse: a service address is missing on this page.", { page, dataset: { ...root.dataset } });
            throw failure("This page could not reach its service. Reload the page; if it keeps happening, contact support.");
        }

        const options = {
            credentials: "same-origin",
            headers: { Accept: "application/json" },
            cache: "no-store"
        };

        if (values) {
            const form = new FormData();

            form.append("__RequestVerificationToken",
                root.querySelector('input[name="__RequestVerificationToken"]')?.value || "");

            Object.entries(values).forEach(([key, value]) => {
                if (Array.isArray(value)) value.forEach(item => form.append(key, String(item)));
                else if (value != null) form.append(key, String(value));
            });

            options.method = "POST";
            options.body = form;
        }

        let response;
        try {
            response = await fetch(url, options);
        } catch {
            throw failure("Cannot reach the server. Check your connection and try again.");
        }

        // Session expired: the server answers 401 (see Program.cs), or an older redirect lands on a login page.
        if (response.status === 401) {
            let info = {};
            try { info = await response.json(); } catch { /* not JSON */ }
            throw sessionExpired(info.loginUrl, info.returnUrlParameter);
        }

        if (response.redirected) {
            let landed = null;
            try { landed = new URL(response.url); } catch { /* ignore */ }
            if (landed && /login|signin/i.test(landed.pathname)) throw sessionExpired(landed.pathname, "ReturnUrl");
        }

        if (!(response.headers.get("content-type") || "").includes("application/json")) {
            console.error("PeerPulse: unexpected response", response.status, response.url);
            throw failure(describeHttp(response), response.status === 403 ? "warning" : "error");
        }

        let result;
        try {
            result = await response.json();
        } catch {
            throw failure("The server sent a response this page could not read. Please try again.");
        }

        if (result && result.status === undefined && result.Status !== undefined) {
            console.error("PeerPulse: JSON property names are PascalCase. Check AddJsonOptions in Program.cs.", result);
            throw failure("The server response format is not what this page expects. Please contact support.");
        }

        if (!response.ok || result.status !== "Ok") {
            if (Array.isArray(result.groups) && Array.isArray(result.peers)) applyWorkspace(result);
            if (result.quota) updateQuota(result.quota);

            throw failure(
                result.message || "The request was not accepted. Reload the page and try again.",
                KIND_BY_STATUS[result.status] || "error",
                false,
                result.traceId || null);
        }

        return result;
    }

    // Disables the button, shows a spinner in it, runs the task, and reports any error as a popup.
    async function withButton(button, task, busyText = "Please wait…") {
        if (button?.disabled) return;

        // The search button animates its own spinner (it contains [data-spinner]).
        const ownLoader = !!button?.querySelector("[data-spinner]");

        if (button) button.disabled = true;
        if (button && !ownLoader) window.ppLoader?.button(button, true, busyText);

        try {
            await task();
        } catch (error) {
            if (!error.silent) {
                const reference = error.traceId ? ` (Ref: ${error.traceId})` : "";
                notify((error.message || "Something went wrong. Reload the page and try again.") + reference,
                    error.kind || "error");
            }
        } finally {
            if (button && !ownLoader) window.ppLoader?.button(button, false);
            if (button) button.disabled = button.id === "pp-search-button" && !$("pp-identifier").value.trim();
        }
    }

    // ------------------------------------------------------------------ workspace data
    function applyWorkspace(data) {
        // console.log("Workspace response:", data);

        // console.table((data.peers ?? []).map(peer => ({
        //     peerId: peer.peerId,
        //     name: peer.name,
        //     createdAtUtc: peer.createdAtUtc,
        //     CreatedAtUtc: peer.CreatedAtUtc,
        //     displayedDate: peerDate(peer)
        // })));
        data.groups = Array.isArray(data.groups) ? data.groups : [];
        data.peers = (Array.isArray(data.peers) ? data.peers : [])
            .map(peer => ({ ...peer, groups: Array.isArray(peer.groups) ? peer.groups : [] }));

        workspace = data;
        root.querySelectorAll("[data-total-peers]").forEach(n => {
            n.textContent = data.peers.length;
        });

        root.querySelectorAll("[data-peer-count]").forEach(node => {
            node.textContent = `${data.peerCount ?? 0}/${data.peerLimit ?? 0}`;
        });

        if (page === "manage") {
            $("pp-total").textContent = data.peerCount ?? 0;
            $("pp-balance").textContent = Math.max(0, (data.peerLimit ?? 0) - (data.peerCount ?? 0));
            $("pp-subscription-limit").textContent = data.peerLimit ?? 0;
            root.querySelectorAll("[data-peer-cap]").forEach(n => {
                n.textContent = `of ${data.peerLimit ?? 0}`;
            });
            $("pp-group-total").textContent = data.groups.length;

            const filter = $("pp-group-filter");
            const previous = filter.value;

            const all = el("option", "", "All groups");
            all.value = "";
            filter.replaceChildren(all);

            data.groups.forEach(group => {
                const option = el("option", "", group.name);
                option.value = group.groupId;
                filter.append(option);
            });

            filter.value = previous;
            renderPeers();
        } else if (page === "groups") {
            renderGroups();
        } else if (page === "add") {
            renderInlineGroups();
        }

        if ($("pp-select-dialog")?.open) renderGroupOptions();
    }

    function updateQuota(quota) {
        if (page !== "add" || !quota) return;

        $("pp-used").textContent = quota.used;
        $("pp-limit").textContent = quota.dailyLimit;
        $("pp-remaining").textContent = quota.remaining;
        $("pp-progress").max = Math.max(quota.dailyLimit, 1);
        $("pp-progress").value = quota.used;
        $("pp-progress").style.width = `${Math.min(100, Math.max(0, Number(quota.used) / Math.max(1, Number(quota.dailyLimit)) * 100))}%`;
    }

    async function loadWorkspace() {
        showSkeleton();

        try {
            const data = await api(endpoint("base"));
            applyWorkspace(data);

            if (page === "manage") {
                const groupId = new URLSearchParams(location.search).get("groupId");

                if (groupId) {
                    $("pp-group-filter").value = groupId;
                    $("pp-status-filter").value = "all";
                    renderPeers();
                }
            }
        } catch (error) {
            if (!error.silent) {
                notify(error.message + (error.traceId ? ` (Ref: ${error.traceId})` : ""), error.kind || "error");
            }
            showLoadError("The list could not be loaded.");
        }
    }

    // ------------------------------------------------------------------ shared group checkbox
    const GROUP_ON = "flex cursor-pointer select-none items-center gap-2.5 rounded-lg border border-[#114fa2] bg-blue-50/80 p-2.5 ring-1 ring-[#114fa2]/30 dark:border-blue-500 dark:bg-blue-950/40";
    const GROUP_OFF = "flex cursor-pointer select-none items-center gap-2.5 rounded-lg border border-gray-200 bg-white p-2.5 hover:bg-gray-50 dark:border-gray-700 dark:bg-gray-800 dark:hover:bg-gray-700";

    function groupOption(group, onChange) {
        const on = selected.has(group.groupId);
        const label = el("label", on ? GROUP_ON : GROUP_OFF);

        const checkbox = el("input", "h-4 w-4 rounded border-gray-300");
        checkbox.type = "checkbox";
        checkbox.checked = on;
        checkbox.addEventListener("change", () => {
            if (checkbox.checked) selected.add(group.groupId);
            else selected.delete(group.groupId);
            onChange();
        });

        const text = el("div", "min-w-0");
        text.append(
            el("span", "block truncate text-xs font-semibold text-gray-800 dark:text-gray-200", group.name),
            el("span", "block truncate text-[10px] text-gray-400 dark:text-gray-500", group.vertical || "General")
        );

        label.append(checkbox, text);
        return label;
    }

    // ------------------------------------------------------------------ loaders
    function showSkeleton() {
        if (page === "manage") {
            const body = $("pp-peer-rows");
            body.replaceChildren();

            for (let i = 0; i < 5; i++) {
                const row = el("tr", "animate-pulse");
                for (let c = 0; c < 8; c++) {
                    const cell = el("td");
                    cell.append(el("div", "h-4 rounded bg-gray-200 dark:bg-gray-700"));
                    row.append(cell);
                }
                body.append(row);
            }

            const empty = $("pp-peer-empty");
            empty.hidden = false;
            empty.replaceChildren(document.createTextNode("Loading peers…"));
            empty.classList.remove("hidden");
        } else if (page === "groups") {
            const grid = $("pp-group-grid");
            grid.replaceChildren();

            for (let i = 0; i < 3; i++) {
                grid.append(el("div", "h-44 animate-pulse rounded-2xl border border-gray-200 bg-gray-100 dark:border-gray-800 dark:bg-gray-800"));
            }
        }
    }

    function showLoadError(text) {
        if (page === "add") return;

        const retry = el("button", "ml-2 rounded-lg bg-brand-primary-blue px-4 py-2 text-xs font-semibold text-white hover:bg-brand-primary-blue/90", "Try again");
        retry.type = "button";
        retry.addEventListener("click", loadWorkspace);

        if (page === "manage") {
            $("pp-peer-rows").replaceChildren();
            const empty = $("pp-peer-empty");
            empty.hidden = false;
            empty.classList.remove("hidden");
            empty.replaceChildren(document.createTextNode(text), retry);
        } else {
            const box = el("div", "col-span-full rounded-2xl border-2 border-dashed border-gray-300 p-8 text-center text-sm text-gray-600 dark:border-gray-700 dark:text-gray-300", text);
            box.append(retry);
            $("pp-group-grid").replaceChildren(box);
        }
    }

    const EMPTY_TITLE = "Awaiting Business Identifier Input";
    const EMPTY_TEXT = "Search by PAN, CIN, or LLPIN to display company or LLP information for review.";

    function emptyCard(title, text) {
        $("pp-results").innerHTML = `
          <div class="rounded-2xl border-2 border-dashed border-gray-300 border-t-2 border-t-[#114fa2] bg-gray-50/50 p-8 text-center dark:border-gray-700 dark:border-t-[#114fa2] dark:bg-gray-800/30">
            <h4 class="text-sm font-semibold text-gray-700 dark:text-gray-200">${esc(title)}</h4>
            <p class="mx-auto mt-1 max-w-sm text-xs text-gray-500 dark:text-gray-400">${esc(text)}</p>
          </div>`;
    }

    function setSearching(on) {
        const button = $("pp-search-button");
        if (!button) return;

        button.setAttribute("aria-busy", String(on));
        button.querySelector("[data-icon]")?.classList.toggle("hidden", on);
        button.querySelector("[data-spinner]")?.classList.toggle("hidden", !on);

        const label = button.querySelector("[data-label]");
        if (label) label.textContent = on ? "Verifying & Fetching Registry Data..." : "Verify & Search Peer Database";
        button.disabled = on || !$("pp-identifier").value.trim();
    }

    function showLoader() {
        $("pp-results").innerHTML = `
          <div role="status" aria-live="polite"
               class="animate-pulse rounded-2xl border border-gray-200 border-t-2 border-t-[#114fa2] bg-white p-6 shadow-xs dark:border-gray-800 dark:border-t-[#114fa2] dark:bg-gray-800/90">
            <div class="flex items-center gap-2.5">
              <span class="h-7 w-7 rounded-full bg-gray-200 dark:bg-gray-700"></span>
              <span class="h-4 w-40 rounded bg-gray-200 dark:bg-gray-700"></span>
            </div>
            <div class="mt-5 h-6 w-3/4 rounded bg-gray-200 dark:bg-gray-700"></div>
            <div class="mt-3 grid grid-cols-2 gap-3">
              <div class="h-12 rounded-lg bg-gray-200 dark:bg-gray-700"></div>
              <div class="h-12 rounded-lg bg-gray-200 dark:bg-gray-700"></div>
            </div>
            <div class="mt-3 grid grid-cols-3 gap-3">
              <div class="h-12 rounded-lg bg-gray-200 dark:bg-gray-700"></div>
              <div class="h-12 rounded-lg bg-gray-200 dark:bg-gray-700"></div>
              <div class="h-12 rounded-lg bg-gray-200 dark:bg-gray-700"></div>
            </div>
            <div class="mt-4 h-16 rounded-xl bg-gray-200 dark:bg-gray-700"></div>
            <p class="mt-4 text-center text-xs text-gray-500 dark:text-gray-400">Searching the registry. This can take a few seconds…</p>
          </div>`;
    }

    // ------------------------------------------------------------------ add page: results
    function renderResults(records) {
        currentRecord = null;
        currentExisting = null;
        selected.clear();

        if (!records.length) {
            emptyCard("No matching business found", "Check the identifier and try again.");
            return;
        }

        if (records.length === 1) {
            showDetail(records[0]);
            return;
        }

        // A PAN can match several entities (company and LLP): let the user pick one.
        const target = $("pp-results");
        target.replaceChildren();

        const box = el("div", "rounded-2xl border border-gray-200 border-t-2 border-t-[#0f9b48] bg-white p-5 shadow-xs dark:border-gray-800 dark:border-t-[#0f9b48] dark:bg-gray-800/90");
        box.append(el("h3", "text-base font-semibold text-gray-900 dark:text-white",
            `${records.length} matches found. Select one to review.`));

        records.forEach(record => {
            const button = el("button", "mt-3 block w-full rounded-lg border border-gray-200 p-3 text-left hover:bg-gray-50 dark:border-gray-700 dark:hover:bg-gray-700");
            button.type = "button";
            button.append(
                el("span", "block text-sm font-semibold text-gray-900 dark:text-white", record.name),
                el("span", "block font-mono text-xs text-gray-500 dark:text-gray-400",
                    `${record.entityType} · ${record.registrationNumber}`)
            );
            button.addEventListener("click", () => showDetail(record));
            box.append(button);
        });

        target.append(box);
    }

    function showDetail(record) {
        const existing = (workspace?.peers || []).find(peer =>
            peer.entityType === record.entityType && peer.entityId === record.entityId) || null;

        currentRecord = record;
        currentExisting = existing;

        selected.clear();
        existing?.groups.forEach(group => selected.add(group.groupId));

        const locked = !!existing && !existing.isActive;
        const isLlp = record.entityType === "LLP";
        const site = safeUrl(record.website);
        const replaced = !!lastSearch && lastSearch.type !== "PAN" &&
            lastSearch.identifier !== record.registrationNumber;

        const idBox = isLlp
            ? "rounded-lg border border-amber-200/80 bg-amber-50/60 p-2.5 dark:border-amber-900/50 dark:bg-amber-950/20"
            : "rounded-lg border border-purple-200/80 bg-purple-50/60 p-2.5 dark:border-purple-900/50 dark:bg-purple-950/20";
        const idLabel = isLlp
            ? "text-[10px] font-bold uppercase tracking-wider text-amber-700 dark:text-amber-400"
            : "text-[10px] font-bold uppercase tracking-wider text-purple-700 dark:text-purple-400";
        const idValue = isLlp
            ? "mt-0.5 truncate font-mono text-xs font-semibold text-amber-950 dark:text-amber-100"
            : "mt-0.5 truncate font-mono text-xs font-semibold text-purple-950 dark:text-purple-100";

        const stat = (label, value) => `
            <div class="rounded-lg border border-gray-200/80 bg-white p-2.5 dark:border-gray-700 dark:bg-gray-800">
              <span class="block text-[10px] font-bold uppercase text-gray-400">${label}</span>
              <span class="block truncate text-xs font-semibold text-gray-800 dark:text-gray-200">${esc(value ?? "—")}</span>
            </div>`;

        const notices = [];

        if (replaced) {
            notices.push(`
              <div class="mt-4 rounded-lg border border-amber-200 bg-amber-50 p-3 text-xs text-amber-800 dark:border-amber-900 dark:bg-amber-950/40 dark:text-amber-300">
                ${esc(lastSearch.identifier)} is a previous ${isLlp ? "LLPIN" : "CIN"}. Showing the current record:
                <strong>${esc(record.registrationNumber)}</strong>.
              </div>`);
        }

        if (existing && existing.isActive) {
            notices.push(`
              <div class="mt-4 flex items-center justify-between gap-3 rounded-lg border border-blue-200 bg-blue-50/90 p-3 text-xs text-blue-800 dark:border-blue-900 dark:bg-blue-950/40 dark:text-blue-300">
                <span>This company is <strong>already in your Peer List</strong>. You can update its groups or notes below.</span>
                <a href="${esc(endpoint("manage") || "#")}" class="shrink-0 font-semibold underline">View list</a>
              </div>`);
        }

        if (locked) {
            notices.push(`
              <div class="mt-4 rounded-lg border border-amber-200 bg-amber-50 p-3 text-xs text-amber-800 dark:border-amber-900 dark:bg-amber-950/40 dark:text-amber-300">
                This peer is <strong>inactive</strong>. Reactivate it from Manage Peers before changing it.
              </div>`);
        }

        $("pp-results").innerHTML = `
        <div class="rounded-2xl border border-gray-200 border-t-2 border-t-[#0f9b48] bg-white shadow-xs dark:border-gray-800 dark:border-t-[#0f9b48] dark:bg-gray-800/90">

          <div class="border-b border-gray-100 p-5 sm:p-6 dark:border-gray-700">
            <div class="flex flex-wrap items-center justify-between gap-3">
              <div class="flex items-center gap-2.5">
                <span class="flex h-7 w-7 items-center justify-center rounded-full text-xs font-bold text-white" style="background-color:#059669" >2</span>
                <h3 class="text-base font-semibold text-gray-900 dark:text-white">Review &amp; Add Peer</h3>
              </div>
              <span class="inline-flex items-center rounded-full border border-emerald-200 bg-emerald-50 px-3 py-1 text-xs font-semibold text-emerald-700 dark:border-emerald-800 dark:bg-emerald-950/50 dark:text-emerald-300">
                ${esc(record.status || "Database match")}
              </span>
            </div>
            ${notices.join("")}
          </div>

          <div class="border-b border-gray-100 bg-gray-50/50 p-5 sm:p-6 dark:border-gray-700 dark:bg-gray-900/30">
            <div class="flex items-start justify-between gap-4">
              <div class="min-w-0">
                <div class="flex items-center gap-2 text-xs">
                  <span class="font-semibold uppercase tracking-wider text-brand-primary-blue dark:text-blue-400">${esc(isLlp ? "LLP" : "Company")}</span>
                  <span class="h-1 w-1 rounded-full bg-gray-300 dark:bg-gray-600"></span>
                  <span class="text-gray-500 dark:text-gray-400">${esc(record.roc || "RoC unavailable")}</span>
                </div>
                <h2 class="mt-1 text-xl font-bold text-gray-900 dark:text-white">${esc(record.name)}</h2>
              </div>
              <span class="flex h-12 w-12 shrink-0 items-center justify-center rounded-xl bg-brand-primary-blue text-lg font-bold text-white shadow-sm">${esc((record.name || "P").charAt(0))}</span>
            </div>

            <div class="mt-4 grid grid-cols-1 gap-2.5 sm:grid-cols-2">
              <div class="${idBox}">
                <span class="${idLabel}">${isLlp ? "LLPIN" : "CIN"}</span>
                <div class="${idValue}" title="${esc(record.registrationNumber)}">${esc(record.registrationNumber)}</div>
              </div>
              <div class="rounded-lg border border-blue-200/80 bg-blue-50/60 p-2.5 dark:border-blue-900/50 dark:bg-blue-950/20">
                <span class="text-[10px] font-bold uppercase tracking-wider text-blue-700 dark:text-blue-400">PAN</span>
                <div class="mt-0.5 truncate font-mono text-xs font-semibold text-blue-950 dark:text-blue-100">${esc(record.pan || "—")}</div>
              </div>
            </div>

            <div class="mt-3 grid grid-cols-1 gap-2.5 sm:grid-cols-3">
              ${stat("Incorporated", fmtDate(record.incorporatedOn))}
              ${stat("State", record.state)}
              ${stat(isLlp ? "Contribution" : "Paid-up Capital", fmtMoney(record.capital))}
            </div>

            ${isLlp ? `
            <div class="mt-3 grid grid-cols-2 gap-2.5">
              ${stat("Partners", record.partners)}
              ${stat("Designated Partners", record.designatedPartners)}
            </div>` : ""}

            <div class="mt-3 rounded-xl border border-gray-200/80 bg-white p-3 dark:border-gray-700 dark:bg-gray-800">
              <span class="text-[10px] font-bold uppercase tracking-wider text-gray-400 dark:text-gray-500">Business Activity</span>
              <p class="mt-1 text-xs font-medium leading-relaxed text-gray-800 dark:text-gray-200">${esc(record.activity || "Not available")}</p>
            </div>

            <div class="mt-3 flex items-center justify-between gap-2 rounded-xl border border-gray-200/80 bg-white p-3 dark:border-gray-700 dark:bg-gray-800">
              <span class="text-[10px] font-bold uppercase tracking-wider text-gray-400 dark:text-gray-500">Website</span>
              ${site
                ? `<a href="${esc(site)}" target="_blank" rel="noopener noreferrer" class="text-xs font-semibold text-brand-primary-blue hover:underline dark:text-blue-400">${esc(site.replace(/^https?:\/\//, ""))}</a>`
                : `<span class="text-xs text-gray-500 dark:text-gray-400">Not available</span>`}
            </div>
          </div>

          <div class="space-y-4 p-5 sm:p-6">
            <div>
              <div class="mb-2 flex items-center justify-between">
                <div class="flex items-center gap-2">
                  <label class="block text-sm font-medium text-gray-700 dark:text-gray-300">Assign to Peer Groups <span class="text-red-500">*</span></label>
                  <span id="pp-selected-count" class="inline-flex items-center rounded-full bg-blue-100/80 px-2 py-0.5 text-[10px] font-bold text-blue-800 dark:bg-blue-900/50 dark:text-blue-300">0 Selected</span>
                </div>
                <button type="button" id="pp-inline-new-group" class="text-xs font-semibold text-brand-primary-blue hover:underline dark:text-blue-400">+ Create New Group</button>
              </div>
              <div id="pp-inline-groups" class="mt-2 grid grid-cols-1 gap-2 sm:grid-cols-2"></div>
              <p class="mt-1.5 text-[11px] text-gray-500 dark:text-gray-400">A peer must belong to at least one group. It can belong to several.</p>
            </div>

            <div>
              <label for="pp-inline-notes" class="mb-1.5 block text-sm font-medium text-gray-700 dark:text-gray-300">User Notes &amp; Context (Optional)</label>
              <textarea id="pp-inline-notes" rows="3" maxlength="1000"
                placeholder="e.g., Key competitor in South India expansion."
                class="w-full rounded-lg border border-gray-300 bg-transparent px-4 py-2.5 text-sm text-gray-800 placeholder-gray-400 outline-none transition focus:border-[#114fa2] focus:ring-2 focus:ring-[#114fa2]/20 dark:border-gray-700 dark:text-white dark:placeholder-gray-500"></textarea>
            </div>

            <div class="flex flex-col gap-3 pt-2 sm:flex-row">
              <button type="button" id="pp-add-peer"
                class="flex flex-1 items-center justify-center gap-2 rounded-lg bg-[#0f9b48] px-5 py-3 text-sm font-semibold text-white shadow-sm transition hover:bg-emerald-600 disabled:cursor-not-allowed disabled:opacity-50">
                ${existing ? "Update Peer in My List" : "Add to My Peer List"}
              </button>
              <button type="button" id="pp-cancel-result"
                class="rounded-lg border border-gray-300 bg-white px-4 py-3 text-sm font-medium text-gray-700 hover:bg-gray-50 dark:border-gray-700 dark:bg-gray-800 dark:text-gray-300">
                Cancel
              </button>
            </div>
          </div>
        </div>`;

        $("pp-inline-notes").value = existing?.notes || "";

        $("pp-inline-new-group").addEventListener("click", () => openGroup());

        $("pp-cancel-result").addEventListener("click", () => {
            currentRecord = null;
            currentExisting = null;
            selected.clear();
            emptyCard(EMPTY_TITLE, EMPTY_TEXT);
        });

        $("pp-add-peer").addEventListener("click", event => {
            if (!selected.size) {
                notify("Select at least one group before adding this peer.", "warning");
                return;
            }

            withButton(event.currentTarget, async () => {
                const editing = !!currentExisting;

                const data = await api(
                    editing ? endpoint("updatePeer") : endpoint("add"),
                    {
                        PeerId: editing ? currentExisting.peerId : null,
                        SearchId: editing ? null : searchId,
                        EntityType: currentRecord.entityType,
                        EntityId: currentRecord.entityId,
                        GroupIds: [...selected],
                        Notes: $("pp-inline-notes").value
                    });

                applyWorkspace(data);
                notify(data.message, "success");
                showDetail(currentRecord);      // now shows "already in your list"
            }, editing => "Saving…");
        });

        renderInlineGroups();
    }

    function renderInlineGroups() {
        const container = $("pp-inline-groups");
        if (!container) return;

        if (!workspace) {
            container.replaceChildren(el("p", "text-xs text-gray-500 dark:text-gray-400",
                "Groups could not be loaded. Reload the page before adding a peer."));
            const add = $("pp-add-peer");
            if (add) add.disabled = true;
            return;
        }

        container.replaceChildren();

        for (const id of [...selected]) {
            if (!workspace.groups.some(group => group.groupId === id)) selected.delete(id);
        }

        if (!workspace.groups.length) {
            container.append(el("p", "text-xs text-gray-500 dark:text-gray-400",
                "No groups exist yet. Create a group first, then select it."));
        }

        workspace.groups.forEach(group => container.append(groupOption(group, renderInlineGroups)));

        const locked = !!currentExisting && !currentExisting.isActive;

        $("pp-selected-count").textContent = `${selected.size} Selected`;
        $("pp-add-peer").disabled = selected.size === 0 || locked;
    }

    // ------------------------------------------------------------------ edit-peer dialog (Manage page)
    function openSelection(peer) {
        if (!workspace) {
            notify("Groups could not be loaded. Reload the page before saving a peer.", "error");
            return;
        }

        pendingPeer = peer;
        selected.clear();
        peer.groups.forEach(group => selected.add(group.groupId));

        $("pp-select-title").textContent = "Edit Peer Information";
        $("pp-select-business").textContent = peer.name;
        $("pp-peer-notes").value = peer.notes || "";

        renderGroupOptions();
        $("pp-select-dialog").showModal();
    }

    function renderGroupOptions() {
        const options = $("pp-group-options");
        options.replaceChildren();

        if (!workspace.groups.length) {
            options.append(el("p", "text-xs text-gray-500 dark:text-gray-400",
                "No groups exist. Create a group first, then select it."));
        }

        for (const id of [...selected]) {
            if (!workspace.groups.some(group => group.groupId === id)) selected.delete(id);
        }

        workspace.groups.forEach(group => options.append(groupOption(group, renderGroupOptions)));
    }

    // ------------------------------------------------------------------ group dialog and confirm dialog
    function openGroup(group = null) {
        editingGroup = group;

        $("pp-group-title").textContent = group ? "Edit Peer Group" : "Create New Peer Group";
        $("pp-group-name").value = group?.name || "";
        $("pp-group-vertical").value = group?.vertical || "";
        $("pp-group-description").value = group?.description || "";

        $("pp-group-dialog").showModal();
    }

    function askConfirm(text, task) {
        confirmAction = task;
        $("pp-confirm-text").textContent = text;
        $("pp-confirm-dialog").showModal();
    }

    // ------------------------------------------------------------------ manage page
    const BADGE = {
        blue: "inline-flex items-center rounded-full bg-blue-100 px-2 py-0.5 text-[11px] font-semibold text-blue-800 dark:bg-blue-900/50 dark:text-blue-200",
        green: "inline-flex items-center rounded-full bg-emerald-100 px-2 py-0.5 text-[11px] font-semibold text-emerald-800 dark:bg-emerald-900/50 dark:text-emerald-200",
        gray: "inline-flex items-center rounded-full bg-gray-100 px-2 py-0.5 text-[11px] font-semibold text-gray-700 dark:bg-gray-700 dark:text-gray-200"
    };

    const LINK_BLUE = "text-xs font-semibold text-brand-primary-blue hover:underline dark:text-blue-400";
    const LINK_RED = "text-xs font-semibold text-red-600 hover:underline dark:text-red-400";
    const LINK_GREEN = "text-xs font-semibold text-emerald-700 hover:underline dark:text-emerald-400";
    function peerDate(peer) {
        return peer.createdAtUtc ?? null;
    }
    function visiblePeers() {
        const term = ($("pp-filter")?.value || "").toLowerCase();
        const groupId = Number($("pp-group-filter")?.value || 0);
        const activity = $("pp-status-filter")?.value || "active";

        const result = (workspace?.peers || []).filter(peer => {
            const matchesActivity =
                activity === "all" || (activity === "active" ? peer.isActive : !peer.isActive);

            const matchesGroup = !groupId || peer.groups.some(group => group.groupId === groupId);

            const matchesText = !term || [
                peer.name, peer.registrationNumber, peer.pan, peer.notes,
                ...peer.groups.map(group => group.name)
            ].some(value => (value || "").toLowerCase().includes(term));

            return matchesActivity && matchesGroup && matchesText;
        });
        const sort = $("pp-sort")?.value || "dateAdded";
        const dateValue = p => peerDate(p) ?? "";
        const sortValue = p => ({
            legalName: p.name || "",
            groupName: p.groups.map(g => g.name).join(", "),
            state: p.state || "",
            dateAdded: dateValue(p)
        })[sort];
        return result.sort((a, b) => {
            const left = sortValue(a), right = sortValue(b);
            const cmp = sort === "dateAdded"
                ? (Date.parse(left) || 0) - (Date.parse(right) || 0)
                : String(left).localeCompare(String(right), undefined, { sensitivity: "base" });
            return sortAscending ? cmp : -cmp;
        });
    }

    const GROUP_BADGE = "inline-flex items-center rounded-md bg-blue-50 px-2 py-0.5 text-[10px] font-medium text-blue-700 border border-blue-200 dark:bg-blue-900/30 dark:text-blue-300 dark:border-blue-800";
    const ACTION_BUTTON = "rounded-lg border border-gray-300 px-2.5 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50 dark:border-gray-600 dark:text-gray-200";

    function peerDate(peer) {
        return peer.createdAt || peer.dateAdded || peer.addedOn || peer.createdOn || null;
    }

    function showPeerDetail(peer) {
        const content = $("pp-detail-content");
        content.replaceChildren();
        content.append(el("h4", "text-base font-bold text-gray-900 dark:text-white", peer.name));
        for (const [label, value] of [
            ["PAN", peer.pan], ["CIN / LLPIN", peer.registrationNumber],
            ["Entity type", peer.entityType], ["State", peer.state],
            ["Activity", peer.isActive ? "Active" : "Inactive"],
            ["Groups", peer.groups.map(g => g.name).join(", ")],
            ["Notes", peer.notes], ["Date added", fmtDate(peerDate(peer))]
        ]) {
            const line = el("div", "mt-3 rounded-lg border border-gray-200 p-3 dark:border-gray-700");
            line.append(el("span", "block text-[10px] font-semibold uppercase text-gray-500 dark:text-gray-400", label),
                el("p", "mt-1 break-words text-xs text-gray-800 dark:text-gray-200", value || "—"));
            content.append(line);
        }
        $("pp-detail-dialog").showModal();
    }

    function peerActions(peer) {
        const actions = el("div", "flex items-center justify-end gap-1.5");
        const view = el("button", ACTION_BUTTON, "View");
        const edit = el("button", ACTION_BUTTON, "Edit");
        const toggle = el("button", peer.isActive
            ? "rounded-lg border border-red-200 px-2.5 py-1.5 text-xs font-medium text-red-600 hover:bg-red-50 dark:border-red-900/60 dark:text-red-400"
            : "rounded-lg border border-emerald-200 px-2.5 py-1.5 text-xs font-medium text-emerald-700 hover:bg-emerald-50 dark:border-emerald-800 dark:text-emerald-400",
            peer.isActive ? "Deactivate" : "Reactivate");
        for (const b of [view, edit, toggle]) b.type = "button";
        view.addEventListener("click", () => showPeerDetail(peer));
        edit.addEventListener("click", () => openSelection(peer));
        toggle.addEventListener("click", () => askConfirm(
            `${peer.isActive ? "Deactivate" : "Reactivate"} ${peer.name}? Notes and group assignments are kept.`,
            async () => {
                const data = await api(peer.isActive ? endpoint("deactivatePeer") : endpoint("reactivatePeer"), { PeerId: peer.peerId });
                applyWorkspace(data);
                notify(data.message, "success");
            }));
        actions.append(view, edit, toggle);
        return actions;
    }

    function updatePeerSelection(peers = visiblePeers()) {
        $("pp-selection-count").textContent = `${selectedPeerIds.size} selected`;
        const all = $("pp-select-all");
        const count = peers.filter(p => selectedPeerIds.has(p.peerId)).length;
        all.checked = peers.length > 0 && count === peers.length;
        all.indeterminate = count > 0 && count < peers.length;
    }

    function identifierCell(value, className) {
        const td = el("td", className);
        const row = el("div", "flex items-center gap-1.5 group");
        row.append(el("span", "truncate max-w-[150px]", value || "—"));
        if (value) {
            const copy = el("button", "text-gray-400 hover:text-brand-primary-blue p-0.5", "⧉");
            copy.type = "button";
            copy.title = "Copy identifier";
            copy.setAttribute("aria-label", "Copy identifier");
            copy.addEventListener("click", async () => {
                try { await navigator.clipboard.writeText(value); notify("Copied", "success"); }
                catch { notify("Could not copy. Select and copy the identifier manually.", "warning"); }
            });
            row.append(copy);
        }
        td.append(row);
        return td;
    }

    function renderPeers() {
        const body = $("pp-peer-rows");
        const grid = $("pp-peer-grid");
        body.replaceChildren();
        grid.replaceChildren();
        const peers = visiblePeers();
        // Discard selections that no longer match the current visible list.
        const visibleIds = new Set(peers.map(p => p.peerId));
        for (const id of selectedPeerIds) if (!visibleIds.has(id)) selectedPeerIds.delete(id);
        $("pp-peer-empty").textContent = peers.length ? "" : "No peers match your filters.";
        $("pp-peer-empty").hidden = peers.length > 0;
        $("pp-peer-empty").classList.toggle("hidden", peers.length > 0);
        $("pp-table-view").hidden = peerView !== "table";
        grid.hidden = peerView !== "grid";
        for (const button of root.querySelectorAll("[data-view]")) {
            const active = button.dataset.view === peerView;
            button.setAttribute("aria-pressed", String(active));
            button.className = "rounded-md p-1.5 transition " + (active
                ? "bg-white text-gray-900 shadow-xs dark:bg-gray-800 dark:text-white"
                : "text-gray-500 hover:text-gray-700 dark:text-gray-400");
        }
        peers.forEach(peer => {
            const row = el("tr", "transition hover:bg-blue-50/30 dark:hover:bg-gray-700/30");
            const select = el("td", "p-4");
            const checkbox = el("input", "h-4 w-4 rounded border-gray-300 text-brand-primary-blue focus:ring-brand-primary-blue/20 dark:border-gray-700 dark:bg-gray-800");
            checkbox.type = "checkbox";
            checkbox.checked = selectedPeerIds.has(peer.peerId);
            checkbox.setAttribute("aria-label", `Select ${peer.name}`);
            checkbox.addEventListener("change", () => {
                if (checkbox.checked) selectedPeerIds.add(peer.peerId); else selectedPeerIds.delete(peer.peerId);
                updatePeerSelection(peers);
            });
            select.append(checkbox);
            const name = el("td", "py-3 px-4");
            const nameRow = el("div", "flex items-center gap-3");
            nameRow.append(el("div", "flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-brand-primary-blue/10 text-brand-primary-blue dark:bg-blue-900/40 dark:text-blue-300 font-bold text-sm", (peer.name || "P").charAt(0)));
            const nameText = el("div");
            const nameButton = el("button", "text-left font-semibold text-gray-900 hover:text-brand-primary-blue dark:text-white dark:hover:text-blue-400", peer.name);
            nameButton.type = "button";
            nameButton.addEventListener("click", () => showPeerDetail(peer));
            nameText.append(nameButton, el("div", "text-[11px] text-gray-500 dark:text-gray-400 mt-0.5", `${peer.entityType} · ${peer.state || "State unavailable"}`));
            if (!peer.isActive) nameText.append(el("span", BADGE.gray, "Inactive"));
            nameRow.append(nameText); name.append(nameRow);
            const groups = el("td", "py-3 px-4");
            const badges = el("div", "flex flex-wrap gap-1 max-w-[190px]");
            peer.groups.forEach(g => badges.append(el("span", GROUP_BADGE, g.name)));
            groups.append(badges);
            const notes = el("td", "py-3 px-4 min-w-[280px]");
            if (peer.notes) {
                const box = el("div", "rounded-lg bg-gray-50/90 p-2.5 border border-gray-200/80 dark:bg-gray-900/50 dark:border-gray-700");
                box.append(el("p", "text-xs text-gray-700 dark:text-gray-200 leading-relaxed break-words line-clamp-2", peer.notes));
                const editNote = el("button", "mt-1 text-[10px] font-semibold text-brand-primary-blue hover:underline dark:text-blue-400", "Edit note");
                editNote.type = "button"; editNote.addEventListener("click", () => openSelection(peer));
                box.append(editNote); notes.append(box);
            } else {
                const addNote = el("button", "text-[11px] text-gray-400 hover:text-brand-primary-blue border border-dashed border-gray-300 dark:border-gray-700 rounded-lg px-2.5 py-1.5", "+ Add note…");
                addNote.type = "button"; addNote.addEventListener("click", () => openSelection(peer)); notes.append(addNote);
            }
            const rawCreatedAt = peer.createdAtUtc;
            const parsedCreatedAt = rawCreatedAt? new Date(rawCreatedAt): null;

            const dateText =parsedCreatedAt && !Number.isNaN(parsedCreatedAt.getTime())? parsedCreatedAt.toLocaleDateString("en-GB", {day: "2-digit",month: "short",year: "numeric"}): "—";

            const date = el("td","py-3 px-4 text-gray-500 dark:text-gray-400 font-mono text-[11px]",dateText);

        
            const actions = el("td", "py-3 px-4"); actions.append(peerActions(peer));
            row.append(select, name,
                identifierCell(peer.pan, "py-3 px-4 font-mono font-medium text-gray-800 dark:text-gray-200"),
                identifierCell(peer.registrationNumber, "py-3 px-4 font-mono text-[11px] font-medium text-gray-700 dark:text-gray-300"),
                groups, notes, date, actions);
            body.append(row);
            const card = el("article", "rounded-2xl border border-gray-200 border-t-2 border-t-[#114fa2] bg-white p-5 shadow-xs dark:border-gray-800 dark:bg-gray-800 flex flex-col justify-between");
            card.append(el("h3", "font-bold text-base text-gray-900 dark:text-white", peer.name),
                el("p", "mt-1 text-[11px] text-gray-500 dark:text-gray-400", `${peer.entityType} · ${peer.state || "—"} · ${peer.isActive ? "Active" : "Inactive"}`),
                el("p", "mt-3 font-mono text-xs text-gray-700 dark:text-gray-300", `PAN: ${peer.pan || "—"}`),
                el("p", "mt-1 font-mono text-[11px] text-gray-600 dark:text-gray-400", peer.registrationNumber || "—"));
            const cardBadges = el("div", "mt-3 flex flex-wrap gap-1");
            peer.groups.forEach(g => cardBadges.append(el("span", GROUP_BADGE, g.name)));
            card.append(cardBadges, el("p", "mt-3 text-xs text-gray-600 dark:text-gray-300 break-words line-clamp-2", peer.notes || "No notes added."));
            const footer = el("div","mt-4 flex flex-wrap items-center justify-between gap-3 border-t border-gray-100 pt-3 dark:border-gray-700");
            const createdAt = peer.createdAtUtc? new Date(peer.createdAtUtc): null;
            const addedDate =createdAt && !Number.isNaN(createdAt.getTime())? createdAt.toLocaleDateString("en-GB", {day: "2-digit",month: "short",year: "numeric"}): "—";
            footer.append(el("span", "text-[11px] text-gray-500 dark:text-gray-400",`${addedDate}`),peerActions(peer));
            card.append(footer);
            grid.append(card);
        });
        if (!peers.length) grid.append(el("p", "text-sm text-gray-500 dark:text-gray-400", "No peers match your filters."));
        updatePeerSelection(peers);
    }

    function renderGroups() {
        const grid = $("pp-group-grid");
        grid.replaceChildren();
        const term = ($("pp-filter")?.value || "").toLowerCase();
        const groups = workspace.groups.filter(group => !term || [group.name, group.vertical, group.description]
            .some(v => String(v || "").toLowerCase().includes(term)));
        if ($("pp-visible-groups")) $("pp-visible-groups").textContent = groups.length;
        groups.forEach(group => {
            const assigned = workspace.peers.filter(p => p.groups.some(g => g.groupId === group.groupId));
            const card = el("article", "rounded-2xl border border-gray-200 border-t-2 border-t-[#7c3aed] bg-white p-5 shadow-xs transition hover:shadow-md dark:border-gray-800 dark:border-t-[#7c3aed] dark:bg-gray-800 flex flex-col justify-between");
            const top = el("div");
            const head = el("div", "flex items-start justify-between gap-3");
            const brand = el("div", "flex items-center gap-3");
            brand.append(el("div", "flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-brand-primary-blue text-white shadow-sm font-bold text-base", (group.name || "G").charAt(0)));
            const title = el("div");
            title.append(el("h3", "font-bold text-base text-gray-900 dark:text-white line-clamp-1", group.name),
                el("span", "inline-flex items-center rounded bg-gray-100 px-2 py-0.5 text-[10px] font-semibold text-gray-600 dark:bg-gray-700 dark:text-gray-300 mt-0.5", group.vertical || "General Vertical"));
            brand.append(title); head.append(brand, el("span", "rounded-full bg-blue-50 px-2.5 py-1 text-xs font-bold text-brand-primary-blue border border-blue-200 dark:bg-blue-900/40 dark:text-blue-300 dark:border-blue-800", `${assigned.length} Peers`));
            top.append(head, el("p", "mt-3.5 text-xs text-gray-600 dark:text-gray-300 leading-relaxed min-h-[36px] line-clamp-2", group.description || "No description provided."));
            const box = el("div", "mt-4 rounded-xl bg-gray-50/70 p-3 border border-gray-100 dark:bg-gray-900/40 dark:border-gray-700");
            const label = el("div", "flex items-center justify-between text-[11px] font-medium text-gray-500 dark:text-gray-400 mb-2");
            const listLink = el("a", "text-brand-primary-blue hover:underline font-semibold dark:text-blue-400", "View List");
            listLink.href = `${endpoint("manage")}?groupId=${encodeURIComponent(group.groupId)}`;
            label.append(el("span", "", "Assigned Companies"), listLink); box.append(label);
            const previews = el("div", "flex flex-wrap gap-1.5");
            assigned.slice(0, 3).forEach(p => {
                const chip = el("span", "inline-flex items-center gap-1 rounded bg-white px-2 py-1 text-[11px] font-medium text-gray-800 shadow-2xs border border-gray-200 dark:bg-gray-800 dark:text-gray-200 dark:border-gray-700");
                chip.append(el("span", p.isActive ? "h-1.5 w-1.5 rounded-full bg-emerald-500" : "h-1.5 w-1.5 rounded-full bg-gray-400"),
                    el("span", "truncate max-w-[120px]", `${p.name}${p.isActive ? "" : " (inactive)"}`)); previews.append(chip);
            });
            if (assigned.length > 3) previews.append(el("span", "inline-flex items-center rounded bg-gray-200 px-2 py-1 text-[11px] font-bold text-gray-700 dark:bg-gray-700 dark:text-gray-300", `+${assigned.length - 3} more`));
            if (!assigned.length) previews.append(el("span", "italic text-gray-400 text-[11px]", "No peers assigned yet."));
            box.append(previews); top.append(box); card.append(top);
            const footer = el("div", "mt-5 pt-3 border-t border-gray-100 dark:border-gray-700 flex items-center justify-between text-xs");
            footer.append(el("span", "text-[11px] text-gray-400 font-mono", group.createdAt ? `Created ${fmtDate(group.createdAt)}` : ""));
            const actions = el("div", "flex items-center gap-1.5");
            const view = el("a", "rounded-lg bg-blue-50 px-2.5 py-1.5 text-xs font-semibold text-brand-primary-blue hover:bg-blue-100 dark:bg-blue-900/40 dark:text-blue-300", "View Peers"); view.href = listLink.href;
            const edit = el("button", ACTION_BUTTON, "Edit"); edit.type = "button"; edit.addEventListener("click", () => openGroup(group));
            const remove = el("button", "rounded-lg border border-red-200 px-2.5 py-1.5 text-xs font-medium text-red-600 hover:bg-red-50 dark:border-red-900/60 dark:text-red-400", "Delete"); remove.type = "button";
            remove.addEventListener("click", () => askConfirm(`Delete group ${group.name}? Reassign all linked peers, including inactive ones, first.`, async () => {
                const data = await api(endpoint("deleteGroup"), { GroupId: group.groupId }); applyWorkspace(data); notify(data.message, "success");
            }));
            actions.append(view, edit, remove); footer.append(actions); card.append(footer); grid.append(card);
        });
        const quick = el("button", "flex flex-col items-center justify-center rounded-2xl border-2 border-dashed border-gray-300 border-t-2 border-t-[#7c3aed] p-8 text-center transition hover:border-brand-primary-blue hover:bg-blue-50/30 dark:border-gray-700 dark:border-t-[#7c3aed] dark:hover:border-blue-500 dark:hover:bg-gray-800/50");
        quick.type = "button";
        quick.append(el("span", "flex h-12 w-12 items-center justify-center rounded-full bg-brand-primary-blue/10 text-brand-primary-blue dark:bg-blue-900/30 dark:text-blue-300 mb-3 text-2xl", "+"),
            el("h4", "font-bold text-sm text-gray-800 dark:text-gray-200", "+ Add Business Vertical / Group"),
            el("p", "mt-1 text-xs text-gray-500 dark:text-gray-400 max-w-xs", "Create a specialized cohort for a new business line, market geography, or tier classification."));
        quick.addEventListener("click", () => openGroup()); grid.append(quick);
    }

    // ------------------------------------------------------------------ dialog wiring
    root.querySelectorAll("[data-close]").forEach(button => {
        button.addEventListener("click", () => $(button.dataset.close).close());
    });

    $("pp-create-from-selection")?.addEventListener("click", () => openGroup());
    $("pp-new-group")?.addEventListener("click", () => openGroup());

    $("pp-group-form")?.addEventListener("submit", event => {
        event.preventDefault();

        withButton(event.submitter || event.target.querySelector('[type="submit"]'), async () => {
            const data = await api(
                editingGroup ? endpoint("updateGroup") : endpoint("createGroup"),
                {
                    GroupId: editingGroup?.groupId,
                    Name: $("pp-group-name").value.trim(),
                    Vertical: $("pp-group-vertical").value,
                    Description: $("pp-group-description").value
                });

            if (!editingGroup && data.changedGroupId) selected.add(data.changedGroupId);

            applyWorkspace(data);
            $("pp-group-dialog").close();
            notify(data.message, "success");
        }, "Saving…");
    });

    $("pp-selection-form")?.addEventListener("submit", event => {
        event.preventDefault();

        if (!selected.size) {
            notify("Select at least one group. If no group exists, create one first.", "warning");
            return;
        }

        withButton(event.submitter || event.target.querySelector('[type="submit"]'), async () => {
            const data = await api(endpoint("updatePeer"), {
                PeerId: pendingPeer.peerId,
                EntityType: pendingPeer.entityType,
                EntityId: pendingPeer.entityId,
                GroupIds: [...selected],
                Notes: $("pp-peer-notes").value
            });

            applyWorkspace(data);
            $("pp-select-dialog").close();
            notify(data.message, "success");
        }, "Saving…");
    });

    $("pp-confirm-action")?.addEventListener("click", event => {
        withButton(event.currentTarget, async () => {
            try {
                if (confirmAction) await confirmAction();
            } finally {
                $("pp-confirm-dialog").close();    // closes on success and on failure; the popup explains a failure
            }
        });
    });

    // ------------------------------------------------------------------ add page: search form
    if (page === "add") {
        const syncSearchInput = () => {
            const value = $("pp-identifier").value;
            $("pp-search-button").disabled = !value.trim() || $("pp-search-button").getAttribute("aria-busy") === "true";
            if ($("pp-character-count")) {
                $("pp-character-count").textContent = value.length;
                $("pp-character-count").parentElement.hidden = value.length === 0;
            }
            if ($("pp-clear-identifier")) $("pp-clear-identifier").hidden = value.length === 0;
            if ($("pp-validation")) { $("pp-validation").hidden = true; $("pp-validation").textContent = ""; }
        };
        root.querySelectorAll("[data-type]").forEach(button => {
            button.addEventListener("click", () => {
                root.querySelectorAll("[data-type]").forEach(node => {
                    node.setAttribute("aria-pressed", String(node === button));
                    node.className = `${node.dataset.baseClass} ${node === button ? node.dataset.classActive : node.dataset.classInactive}${node.disabled ? " opacity-50 cursor-not-allowed" : ""}`;
                });

                if (button.disabled) return;
                root.querySelector('input[name="IdentifierType"]').value = button.dataset.type;

                $("pp-identifier").placeholder = {
                    PAN: "e.g. AAACI4818J",
                    CIN: "e.g. L85110KA1981PLC013115",
                    LLPIN: "e.g. AAA-0364"
                }[button.dataset.type];

                retrySearch = null;
            });
        });

        $("pp-identifier").addEventListener("input", () => {
            $("pp-identifier").value = $("pp-identifier").value.toUpperCase();
            retrySearch = null;
            syncSearchInput();
        });

        $("pp-clear-identifier")?.addEventListener("click", () => {
            $("pp-identifier").value = "";
            retrySearch = null;
            syncSearchInput();
            emptyCard(EMPTY_TITLE, EMPTY_TEXT);
        });
        root.querySelectorAll("[data-sample]").forEach(button => {
            button.addEventListener("click", () => {
                if (button.disabled) return;
                root.querySelector(`[data-type="${button.dataset.sampleType}"]`)?.click();
                $("pp-identifier").value = button.dataset.sample;
                syncSearchInput();
                // Samples fill the field; a paid/quota-consuming search requires Verify.
                $("pp-identifier").focus();
            });
        });
        updateQuota({
            used: Number($("pp-used").textContent) || 0,
            dailyLimit: Number($("pp-limit").textContent) || 0,
            remaining: Number($("pp-remaining").textContent) || 0
        });
        syncSearchInput();
        $("pp-search-form").addEventListener("submit", event => {
            event.preventDefault();

            withButton($("pp-search-button"), async () => {
                const type = root.querySelector('input[name="IdentifierType"]').value;
                const identifier = $("pp-identifier").value.trim().toUpperCase();

                const patterns = {
                    PAN: /^[A-Z]{5}[0-9]{4}[A-Z]$/,
                    CIN: /^[LU][0-9]{5}[A-Z]{2}[0-9]{4}[A-Z]{3}[0-9]{6}$/,
                    LLPIN: /^[A-Z]{3}-[0-9]{4}$/
                };

                if (!patterns[type] || !patterns[type].test(identifier)) {
                    if ($("pp-validation")) {
                        $("pp-validation").hidden = false;
                        $("pp-validation").textContent = `Enter a valid ${type}. Invalid input does not use a search.`;
                    }
                    notify(`Enter a valid ${type}. Invalid input does not use a search.`, "warning");
                    return;
                }

                // The same SearchId is reused on a manual retry, so a retry is never charged twice.
                if (!retrySearch || retrySearch.type !== type || retrySearch.identifier !== identifier) {
                    retrySearch = { type, identifier, id: crypto.randomUUID() };
                }

                showLoader();
                setSearching(true);

                try {
                    const data = await api(endpoint("search"), {
                        IdentifierType: type,
                        Identifier: identifier,
                        SearchId: retrySearch.id
                    });

                    searchId = data.searchId;
                    lastSearch = { type, identifier };
                    updateQuota(data.quota);
                    renderResults(data.records || []);
                    notify(data.message, (data.records || []).length ? "success" : "warning");
                    retrySearch = null;
                } catch (error) {
                    emptyCard(EMPTY_TITLE, EMPTY_TEXT);
                    throw error;                     // withButton shows the message
                } finally {
                    setSearching(false);
                }
            });
        });
    }

    $("pp-sort")?.addEventListener("change", () => { if (workspace) renderPeers(); });
    $("pp-sort-direction")?.addEventListener("click", event => {
        sortAscending = !sortAscending;
        event.currentTarget.textContent = sortAscending ? "↑" : "↓";
        if (workspace) renderPeers();
    });
    root.querySelectorAll("[data-view]").forEach(button => button.addEventListener("click", () => {
        peerView = button.dataset.view;
        if (workspace) renderPeers();
    }));
    $("pp-select-all")?.addEventListener("change", event => {
        if (!workspace) return;
        visiblePeers().forEach(peer => {
            if (event.currentTarget.checked) selectedPeerIds.add(peer.peerId); else selectedPeerIds.delete(peer.peerId);
        });
        renderPeers();
    });

    // ------------------------------------------------------------------ filters and export
    $("pp-filter")?.addEventListener("input", () => {
        if (!workspace) return;
        if (page === "groups") renderGroups();
        else renderPeers();
    });

    $("pp-group-filter")?.addEventListener("change", () => { if (workspace) renderPeers(); });
    $("pp-status-filter")?.addEventListener("change", () => { if (workspace) renderPeers(); });

    $("pp-export")?.addEventListener("click", () => {
        if (!workspace) return;

        // Cells starting with = + - @ are prefixed so spreadsheets do not run them as formulas.
        const safeCell = value => {
            let text = String(value ?? "");
            if (/^[\s]*[=+@-]/.test(text) || /^[\t\r\n]/.test(text)) text = "'" + text;
            return `"${text.replaceAll('"', '""')}"`;
        };

        const rows = [
            ["Name", "Type", "Registration", "PAN", "Groups", "Activity", "Notes"],
            ...visiblePeers().filter(peer => !selectedPeerIds.size || selectedPeerIds.has(peer.peerId)).map(peer => [
                peer.name, peer.entityType, peer.registrationNumber, peer.pan,
                peer.groups.map(group => group.name).join("; "),
                peer.isActive ? "Active" : "Inactive", peer.notes
            ])
        ];

        const blob = new Blob(
            ["\ufeff" + rows.map(row => row.map(safeCell).join(",")).join("\r\n")],
            { type: "text/csv;charset=utf-8" });

        const url = URL.createObjectURL(blob);
        const link = el("a");

        link.href = url;
        link.download = "peers.csv";
        link.click();

        setTimeout(() => URL.revokeObjectURL(url), 1000);
    });

    // ------------------------------------------------------------------ start
    loadWorkspace();

    const serverNote = $("pp-message")?.textContent.trim();
    if (serverNote) notify(serverNote, "warning");
})();