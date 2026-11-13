/* Navigation feedback only. Report data/HTML are rendered by MVC and Razor. */
(() => {
  "use strict";
  if (window.PeerPulseReportLoader) return;

  let active = false;
  let showTimer;
  let slowTimer;
  let releaseTimer;
  let previousFocus;
  let previousOverflow;
  let liveRegion;

  const root = () => document.getElementById("pp-report-loader");

  function hide() {
    clearTimeout(showTimer);
    clearTimeout(slowTimer);
    clearTimeout(releaseTimer);
    const element = root();
    if (element) element.hidden = true;
    if (active) document.documentElement.style.overflow = previousOverflow || "";
    active = false;
    if (liveRegion) { liveRegion.remove(); liveRegion = null; }
    if (previousFocus?.isConnected) previousFocus.focus({ preventScroll: true });
    previousFocus = null;
  }

  function show() {
    if (active) return false;
    const element = root();
    if (!element) return true; // Progressive enhancement: navigation still works.
    active = true;
    previousFocus = document.activeElement;
    previousOverflow = document.documentElement.style.overflow;
    const message = document.getElementById("pp-report-loader-message");
    const dismiss = document.getElementById("pp-report-loader-dismiss");
    if (message) message.textContent = "Retrieving your selected peers and building the report.";
    if (dismiss) dismiss.hidden = true;
    // Avoid flashing an overlay for a very fast navigation.
    showTimer = setTimeout(() => {
      if (!active) return;
      element.hidden = false;
      document.documentElement.style.overflow = "hidden";
    }, 120);
    slowTimer = setTimeout(() => {
      if (!active) return;
      if (message) message.textContent = "The server is still preparing your report. Please wait.";
      if (dismiss) dismiss.hidden = false;
    }, 12000);
    // This is a UI escape hatch, not cancellation of the server operation.
    releaseTimer = setTimeout(hide, 45000);
    return true;
  }

  function navigate(value) {
    let destination;
    try { destination = new URL(value, window.location.href); }
    catch { return false; }
    if (destination.origin !== window.location.origin ||
        !["http:", "https:"].includes(destination.protocol)) return false;
    if (!show()) return false;
    // Allow the loading state to paint before leaving the page.
    requestAnimationFrame(() => requestAnimationFrame(() => {
      try { window.location.assign(destination.href); }
      catch { hide(); }
    }));
    return true;
  }

  document.addEventListener("click", event => {
    const print = event.target.closest?.("[data-report-print]");
    if (print) { event.preventDefault(); window.print(); return; }
    const share = event.target.closest?.("[data-report-share]");
    if (share) {
      event.preventDefault();
      const status = document.getElementById("report-action-status");
      if (navigator.clipboard && window.isSecureContext) {
        navigator.clipboard.writeText(window.location.href).then(() => {
          if (status) status.textContent = "Report link copied. Access still requires authorization.";
        }).catch(() => {
          if (status) status.textContent = "Copy the report URL from your browser address bar.";
        });
      } else if (status) status.textContent = "Copy the report URL from your browser address bar.";
      return;
    }
    const dismiss = event.target.closest?.("#pp-report-loader-dismiss");
    if (dismiss) { hide(); return; }
    const anchor = event.target.closest?.("a[data-report-navigation]");
    if (!anchor || event.defaultPrevented || event.button !== 0 || event.ctrlKey ||
        event.metaKey || event.shiftKey || event.altKey || anchor.hasAttribute("download") ||
        (anchor.target && anchor.target !== "_self")) return;
    const url = new URL(anchor.href, location.href);
    if (url.origin !== location.origin ||
        (url.pathname === location.pathname && url.search === location.search)) return;
    if (!navigator.onLine) return; // Let the browser handle normal offline navigation.
    event.preventDefault();
    navigate(url.href);
  });

  document.addEventListener("submit", event => {
    const form = event.target;
    if (!(form instanceof HTMLFormElement) ||
        !form.matches("[data-report-navigation]") || event.defaultPrevented ||
        (form.target && form.target !== "_self")) return;
    const url = new URL(form.action, location.href);
    if (url.origin !== location.origin) return;
    if (!show()) event.preventDefault();
    // Normal browser form submission continues, including validation/tokens.
  });

  document.addEventListener("change", event => {
    const select = event.target;
    if (select instanceof HTMLSelectElement &&
        select.closest("form[data-report-year-form]")) {
      select.form?.requestSubmit();
    }
  });

  // Reset overlays and navigation locks when restoring a page from bfcache.
  window.addEventListener("pageshow", hide);
  window.addEventListener("pagehide", hide);
  window.addEventListener("offline", hide);
  document.addEventListener("peerpulse:report-error", hide);
  window.PeerPulseReportLoader = Object.freeze({ show, hide, navigate, get isActive() { return active; } });
})();
