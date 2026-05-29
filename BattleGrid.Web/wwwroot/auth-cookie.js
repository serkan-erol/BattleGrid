window.bgAuth = {
    read: async function () {
        const controller = new AbortController();
        const timeoutId = setTimeout(() => controller.abort(), 10000);

        try {
            const response = await fetch("/auth/session/read", {
                method: "GET",
                credentials: "include",
                signal: controller.signal
            });

            if (!response.ok || response.status === 204) {
                return null;
            }

            return await response.json();
        } catch {
            return null;
        } finally {
            clearTimeout(timeoutId);
        }
    },

    store: async function (state) {
        const controller = new AbortController();
        const timeoutId = setTimeout(() => controller.abort(), 10000);

        try {
            await fetch("/auth/session/store", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                credentials: "include",
                signal: controller.signal,
                body: JSON.stringify(state)
            });
        } catch {
            // Local auth state is still updated by the caller.
        } finally {
            clearTimeout(timeoutId);
        }
    },

    clear: async function () {
        const controller = new AbortController();
        const timeoutId = setTimeout(() => controller.abort(), 10000);

        try {
            await fetch("/auth/session/clear", {
                method: "POST",
                credentials: "include",
                signal: controller.signal
            });
        } catch {
            // Best-effort cookie clear.
        } finally {
            clearTimeout(timeoutId);
        }
    },

    // User-initiated logout: wipe client-side persistence for this origin (with cookie clear).
    clearBrowserStorages: function () {
        try {
            localStorage.clear();
        } catch { }
        try {
            sessionStorage.clear();
        } catch { }
    }
};
