/* Load once per page, before deferred Alpine initializes. Do not call Alpine.start(). */
(() => {
    'use strict';
    const defaults = () => ({ currency: 'INR', financialFormat: 'lakhs', exchangeRate: 83.5, revision: 0 });
    const scales = { absolute: 1, thousands: 1000, lakhs: 100000, crores: 10000000 };
    const suffixes = { absolute: '', thousands: ' K', lakhs: ' L', crores: ' Cr' };
    const clone = value => JSON.parse(JSON.stringify(value));
    function validatePreferences(p) {
        if (!['INR', 'USD'].includes(p.currency) || !Object.hasOwn(scales, p.financialFormat) ||
            !Number.isFinite(Number(p.exchangeRate)) || Number(p.exchangeRate) < 1 || Number(p.exchangeRate) > 100000)
            throw new Error('Choose a currency and format, and enter a rate between 1 and 100,000.');
    }
    function money(raw, p, format = p.financialFormat) {
        if (raw === null || raw === undefined || raw === '') return '—';
        const value = Number(raw);
        const rate = Number(p.exchangeRate);
        if (!Number.isFinite(value) || !Number.isFinite(rate) || rate <= 0 || !scales[format]) return '—';
        const amount = (p.currency === 'USD' ? value / rate : value) / scales[format];
        return `${p.currency === 'USD' ? '$' : '₹'} ${amount.toLocaleString(p.currency === 'USD' ? 'en-US' : 'en-IN', {
            minimumFractionDigits: format === 'absolute' ? 0 : 2, maximumFractionDigits: 2
        })}${suffixes[format]}`;
    }
    async function api(root, url, body) {
        if (!url) throw new Error('The page endpoint is not configured.');
        const controller = new AbortController();
        const timer = setTimeout(() => controller.abort(), 30000);
        try {
            const headers = { Accept: 'application/json' };
            if (body !== undefined) {
                headers['Content-Type'] = 'application/json';
                const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value;
                if (!token) throw new Error('The request token is missing. Reload the page.');
                headers['X-CSRF-TOKEN'] = token;
            }
            const response = await fetch(url, {
                method: body === undefined ? 'GET' : 'POST',
                credentials: 'same-origin', cache: 'no-store', headers,
                signal: controller.signal, body: body === undefined ? undefined : JSON.stringify(body)
            });
            if (response.redirected || response.status === 401) throw new Error('Your session may have expired. Sign in and reload this page.');
            if (response.status === 429) throw new Error('Too many requests. Wait a minute and try again.');
            let data;
            try { data = await response.json(); }
            catch { throw new Error(response.status === 400 ? 'The request was rejected. Reload the page and try again.' : 'The server returned an unexpected response.'); }
            if (!response.ok) throw new Error(data.message || `Request failed (${response.status}). Reload and try again.`);
            return data;
        } catch (error) {
            if (error.name === 'AbortError') throw new Error('The request timed out. Reload to check whether the save completed.');
            throw error;
        } finally { clearTimeout(timer); }
    }
    function notifyError(app, error) {
        app.error = error.message || 'The request could not be completed.';
        if (window.Swal?.fire) window.Swal.fire({ icon: 'error', title: 'Unable to complete', text: app.error });
    }
    window.peerComparePreferencesApp = () => ({
        preferences: defaults(), loading: true, ready: false, busy: false, error: '', savedToast: false,
        _saveTimer: null, _toastTimer: null, _pending: Promise.resolve(), _disposed: false,
        async init() {
            try {
                const result = await api(this.$root, this.$root.dataset.load);
                this.preferences = result.data; this.ready = true;
            } catch (error) { notifyError(this, error); }
            finally { this.loading = false; }
        },
        destroy() { this._disposed = true; clearTimeout(this._saveTimer); clearTimeout(this._toastTimer); },
        setCurrency(value) { this.preferences.currency = value; this.savePreferences(false); },
        setFormat(value) { this.preferences.financialFormat = value; this.savePreferences(false); },
        setPresetRate(value) { this.preferences.exchangeRate = value; this.savePreferences(false); },
        sampleValue(format) { return money(100000, this.preferences, format); },
        formatValue(value) { return money(value, this.preferences); },
        savePreferences(showToast = false) {
            clearTimeout(this._saveTimer);
            if (!this.ready || this._disposed) return;
            if (!showToast) {
                this._saveTimer = setTimeout(() => this.persist(false).catch(error => notifyError(this, error)), 600);
                return;
            }
            return this.persist(true).catch(error => notifyError(this, error));
        },
        persist(showToast) {
            clearTimeout(this._saveTimer);
            const next = this._pending.catch(() => { }).then(async () => {
                if (this._disposed) throw new Error('The page is no longer available.');
                const snapshot = clone(this.preferences);
                validatePreferences(snapshot);
                this.busy = true; this.error = '';
                try {
                    const result = await api(this.$root, this.$root.dataset.save, {
                        currency: snapshot.currency, financialFormat: snapshot.financialFormat,
                        exchangeRate: Number(snapshot.exchangeRate), expectedRevision: snapshot.revision
                    });
                    // Keep any in-progress local edits; update the optimistic concurrency token.
                    this.preferences.revision = result.data.revision;
                    if (showToast) {
                        this.savedToast = true; clearTimeout(this._toastTimer);
                        this._toastTimer = setTimeout(() => this.savedToast = false, 2500);
                    }
                    return result.data;
                } finally { this.busy = false; }
            });
            this._pending = next;
            return next;
        },
        async proceedToSelect() {
            try { await this.persist(false); window.location.assign(this.$root.dataset.select); }
            catch (error) { notifyError(this, error); }
        },
        resetDefaults() {
            this.preferences = { ...defaults(), revision: this.preferences.revision };
            this.savePreferences(true);
        }
    });
    window.peerCompareSelectApp = () => ({
        allPeers: [], groups: [], selectedPeerIds: [], selectedGroupId: 'all', searchQuery: '',
        preferences: defaults(), draftRevision: 0,
        loading: true, ready: false, busy: false, error: '',
        async init() {
            try {
                const result = await api(this.$root, this.$root.dataset.load);
                const data = result.data;
                this.preferences = data.preferences;
                this.allPeers = data.peers || []; this.groups = data.groups || [];
                this.selectedPeerIds = (data.draft?.peers || []).map(p => p.peerId);
                this.draftRevision = data.draft?.revision || 0;
                this.ready = true;
            } catch (error) { notifyError(this, error); }
            finally { this.loading = false; }
        },
        get filteredPeers() {
            const q = this.searchQuery.trim().toLocaleLowerCase();
            return this.allPeers.filter(p =>
                (this.selectedGroupId === 'all' || p.groups.some(g => String(g.id) === String(this.selectedGroupId))) &&
                (!q || [p.company, p.tradeName, p.cin, p.pan].some(v => String(v || '').toLocaleLowerCase().includes(q))));
        },
        get selectedGroupLabel() {
            if (this.selectedGroupId === 'all') return 'Select Group';
            const group = this.groups.find(g => String(g.id) === String(this.selectedGroupId));
            const count = this.allPeers.filter(p => p.groups.some(g => String(g.id) === String(this.selectedGroupId))).length;
            return `${group?.name || 'Group'} (${count})`;
        },
        isSelected(id) { return this.selectedPeerIds.includes(id); },
        togglePeer(id) {
            if (!this.ready || this.busy) return;
            if (this.isSelected(id)) this.selectedPeerIds = this.selectedPeerIds.filter(x => x !== id);
            else if (this.selectedPeerIds.length < 500) this.selectedPeerIds = [...this.selectedPeerIds, id];
        },
        selectAll() { this.selectedPeerIds = [...new Set([...this.selectedPeerIds, ...this.filteredPeers.map(p => p.peerId)])]; },
        deselectAll() { this.selectedPeerIds = []; },
        selectGroupPeers(id) {
            if (id === 'all') return;
            const ids = this.allPeers.filter(p => p.groups.some(g => String(g.id) === String(id))).map(p => p.peerId);
            this.selectedPeerIds = [...new Set([...this.selectedPeerIds, ...ids])];
        },
        formatCapital(value) { return money(value, this.preferences); },
        async proceedToReport() {
            if (!this.ready || this.busy || !this.selectedPeerIds.length || window.PeerPulseReportLoader?.isActive) return;
            this.busy = true; this.error = '';
            try {
                if (this.preferences.revision === 0) {
                    const saved = await api(this.$root, this.$root.dataset.preferenceSave, {
                        currency: this.preferences.currency, financialFormat: this.preferences.financialFormat,
                        exchangeRate: this.preferences.exchangeRate, expectedRevision: 0
                    });
                    this.preferences = saved.data;
                }
                const result = await api(this.$root, this.$root.dataset.save, {
                    peerIds: this.selectedPeerIds, preferenceRevision: this.preferences.revision,
                    expectedRevision: this.draftRevision
                });
                this.draftRevision = result.data.revision;
                const next = new URL(result.reportUrl, window.location.href);
                if (next.origin !== window.location.origin) throw new Error('Invalid report address.');
                if (window.PeerPulseReportLoader) {
                    if (!window.PeerPulseReportLoader.navigate(next.href)) {
                        throw new Error('The report is already opening or the address is invalid.');
                    }
                } else {
                    window.location.assign(next.href);
                }
            } catch (error) { window.PeerPulseReportLoader?.hide(); notifyError(this, error); }
            finally { this.busy = false; }
        }
    });
})();
