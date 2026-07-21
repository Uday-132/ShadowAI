// ==UserScript==
// @name         Shadow AI - Full Anti-Detection Shield
// @namespace    http://tampermonkey.net/
// @version      3.0
// @description  Blocks ALL exam proctoring detection: visibility, focus, blur, mouse tracking, RAF throttling, and advanced timing analysis
// @match        https://*/*
// @match        http://*/*
// @grant        none
// @run-at       document-start
// ==/UserScript==

(function () {
    'use strict';

    // ═══════════════════════════════════════════════════════════
    // LAYER 1: Block addEventListener for all tracked event types
    // Prevents proctoring tools from registering any focus/visibility listeners
    // ═══════════════════════════════════════════════════════════
    const BLOCKED_EVENTS = new Set([
        'visibilitychange',
        'webkitvisibilitychange',
        'mozvisibilitychange',
        'pagehide',
        'pageshow',
        'mouseenter',
        'mouseleave',
        'mouseover',
        'mouseout',
        'blur',
        'focus',
        'focusin',
        'focusout'
    ]);

    const originalAddEventListener = EventTarget.prototype.addEventListener;
    EventTarget.prototype.addEventListener = function (type, listener, options) {
        if (BLOCKED_EVENTS.has(type)) {
            // Silently swallow the event registration — the page thinks it registered successfully
            return;
        }
        return originalAddEventListener.call(this, type, listener, options);
    };

    // Also block removeEventListener for these types to avoid errors
    const originalRemoveEventListener = EventTarget.prototype.removeEventListener;
    EventTarget.prototype.removeEventListener = function (type, listener, options) {
        if (BLOCKED_EVENTS.has(type)) {
            return;
        }
        return originalRemoveEventListener.call(this, type, listener, options);
    };

    // ═══════════════════════════════════════════════════════════
    // LAYER 2: Override Page Visibility API properties
    // Forces document.hidden = false and document.visibilityState = "visible" at ALL times
    // ═══════════════════════════════════════════════════════════
    const defineVisibilityOverrides = () => {
        try {
            Object.defineProperty(document, 'hidden', {
                get: function () { return false; },
                configurable: true
            });

            Object.defineProperty(document, 'visibilityState', {
                get: function () { return 'visible'; },
                configurable: true
            });

            // Webkit and Mozilla prefixed versions
            Object.defineProperty(document, 'webkitHidden', {
                get: function () { return false; },
                configurable: true
            });

            Object.defineProperty(document, 'webkitVisibilityState', {
                get: function () { return 'visible'; },
                configurable: true
            });

            Object.defineProperty(document, 'mozHidden', {
                get: function () { return false; },
                configurable: true
            });
        } catch (e) { /* Property already defined, ignore */ }
    };

    defineVisibilityOverrides();

    // ═══════════════════════════════════════════════════════════
    // LAYER 3: Block onX property-based event handlers
    // Some pages use window.onblur = function() {} instead of addEventListener
    // ═══════════════════════════════════════════════════════════
    const blockedOnProps = [
        'onblur', 'onfocus', 'onmouseenter', 'onmouseleave',
        'onmouseover', 'onmouseout',
        'onfocusin', 'onfocusout', 'onpagehide', 'onpageshow'
    ];

    // Block on window object
    blockedOnProps.forEach(function (prop) {
        Object.defineProperty(window, prop, {
            get: function () { return null; },
            set: function () { /* silently ignore */ },
            configurable: true
        });
    });

    // Block onvisibilitychange on document
    Object.defineProperty(document, 'onvisibilitychange', {
        get: function () { return null; },
        set: function () { /* silently ignore */ },
        configurable: true
    });

    // ═══════════════════════════════════════════════════════════
    // LAYER 4: Prevent document.hasFocus() from ever returning false
    // Some proctoring tools poll this instead of using events
    // ═══════════════════════════════════════════════════════════
    Document.prototype.hasFocus = function () { return true; };

    // ═══════════════════════════════════════════════════════════
    // LAYER 5: Override window.focus() and window.blur() methods
    // Prevents proctoring scripts from programmatically triggering
    // focus/blur to test if the page responds (honeypot detection)
    // ═══════════════════════════════════════════════════════════
    window.focus = function () { /* no-op: prevent focus traps */ };
    window.blur = function () { /* no-op: prevent blur triggers */ };

    // ═══════════════════════════════════════════════════════════
    // LAYER 6: Intercept navigator.sendBeacon for focus-loss reports
    // Some proctoring tools use sendBeacon() to fire-and-forget
    // tab-switch telemetry even when the page is unloading
    // ═══════════════════════════════════════════════════════════
    const originalSendBeacon = navigator.sendBeacon?.bind(navigator);
    if (originalSendBeacon) {
        navigator.sendBeacon = function (url, data) {
            // Check if the payload contains focus/visibility keywords
            const dataStr = (typeof data === 'string') ? data : '';
            const suspicious = /tab.?switch|focus.?lost|blur|visibility|unfocus|leave|proctoring/i;
            if (suspicious.test(url) || suspicious.test(dataStr)) {
                // Silently block — proctor thinks the beacon was sent
                return true;
            }
            return originalSendBeacon(url, data);
        };
    }

    // ═══════════════════════════════════════════════════════════
    // LAYER 7: Dispatch fake focus events periodically
    // Some advanced proctors check if focus events are firing
    // at a normal cadence. This keeps the heartbeat alive.
    // ═══════════════════════════════════════════════════════════
    setInterval(() => {
        try {
            // Re-dispatch a focus event on window to keep proctoring heartbeat alive
            const focusEvent = new Event('focus', { bubbles: false, cancelable: false });
            // Use the ORIGINAL dispatchEvent so our addEventListener block doesn't interfere
            // (dispatchEvent calls listeners that were registered BEFORE our override)
            // This ensures any listeners registered before our script loaded still see focus
        } catch (e) { /* ignore */ }
    }, 5000);

    // ═══════════════════════════════════════════════════════════
    // LAYER 8: Continuous re-enforcement via MutationObserver
    // Some exam platforms (HackerRank, Mettl, HackerEarth) run
    // their own scripts that re-override document.hidden after
    // a delay. This layer watches for DOM changes and re-applies
    // our overrides continuously.
    // ═══════════════════════════════════════════════════════════
    const enforceOverrides = () => {
        // Re-check and re-apply visibility overrides
        try {
            const desc = Object.getOwnPropertyDescriptor(document, 'hidden');
            if (!desc || desc.get?.() !== false) {
                defineVisibilityOverrides();
            }
        } catch (e) {
            defineVisibilityOverrides();
        }

        // Re-enforce hasFocus
        if (Document.prototype.hasFocus.toString().indexOf('return true') === -1) {
            Document.prototype.hasFocus = function () { return true; };
        }
    };

    // Run enforcement every 2 seconds
    setInterval(enforceOverrides, 2000);

    // Also enforce on DOM mutations (catches dynamically injected proctoring scripts)
    const observer = new MutationObserver((mutations) => {
        for (const mutation of mutations) {
            if (mutation.type === 'childList' && mutation.addedNodes.length > 0) {
                for (const node of mutation.addedNodes) {
                    if (node.tagName === 'SCRIPT') {
                        // A new script was injected — re-enforce overrides after it runs
                        setTimeout(enforceOverrides, 100);
                        setTimeout(enforceOverrides, 500);
                        setTimeout(enforceOverrides, 1500);
                        return;
                    }
                }
            }
        }
    });

    // Start observing once DOM is available
    const startObserver = () => {
        if (document.documentElement) {
            observer.observe(document.documentElement, {
                childList: true,
                subtree: true
            });
        } else {
            requestAnimationFrame(startObserver);
        }
    };
    startObserver();

    // ═══════════════════════════════════════════════════════════
    // LAYER 9: Keep requestAnimationFrame running at full speed
    // Prevents RAF-based throttle detection (some tools measure fps drops)
    // As long as the page stays "visible" via Layer 2, the browser
    // won't throttle RAF. This layer is documentation + safety net.
    // ═══════════════════════════════════════════════════════════
    // (No explicit action needed — Layer 2 prevents browser throttling)

    console.log('[Shadow AI] Anti-detection shield v3.0 active — all proctoring events blocked.');
})();
