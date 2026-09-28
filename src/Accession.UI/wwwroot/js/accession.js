// Accession web UI helpers: keyboard shortcuts and scrolling selected table rows into view.
(function () {
    "use strict";

    let shortcutTarget = null;

    function isTyping(element) {
        if (!element) {
            return false;
        }

        const tag = element.tagName;
        return tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT" || element.isContentEditable;
    }

    function combo(e) {
        const parts = [];
        if (e.ctrlKey) parts.push("ctrl");
        if (e.altKey) parts.push("alt");
        if (e.shiftKey && e.key.length > 1) parts.push("shift");
        parts.push(e.key.length === 1 ? e.key.toLowerCase() : e.key.toLowerCase());
        return parts.join("+");
    }

    // Shortcuts handled by the page (.NET), and the ones only handled when not typing in a field.
    const pageShortcuts = new Set(["ctrl+n", "ctrl+o", "f5", "ctrl+,", "alt+1", "alt+2", "alt+3", "alt+4", "alt+5", "alt+6", "alt+7"]);
    const plainShortcuts = new Set(["?"]);

    // Keeps Tab inside the top dialog while one is open.
    function trapFocus(e) {
        const dialogs = document.querySelectorAll(".modal");
        if (e.key !== "Tab" || dialogs.length === 0) {
            return false;
        }

        const dialog = dialogs[dialogs.length - 1];
        const focusable = Array.from(dialog.querySelectorAll(
            "button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), a[href], [tabindex]:not([tabindex='-1'])"));
        if (focusable.length === 0) {
            e.preventDefault();
            dialog.focus();
            return true;
        }

        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        if (!dialog.contains(document.activeElement)) {
            e.preventDefault();
            first.focus();
        } else if (e.shiftKey && document.activeElement === first) {
            e.preventDefault();
            last.focus();
        } else if (!e.shiftKey && document.activeElement === last) {
            e.preventDefault();
            first.focus();
        }

        return true;
    }

    document.addEventListener("keydown", function (e) {
        if (trapFocus(e)) {
            return;
        }

        const key = combo(e);

        // Arrow keys in keyboard-navigable tables must not scroll the page.
        if (e.target.closest && e.target.closest("[data-keynav]") && !isTyping(e.target)
            && ["ArrowUp", "ArrowDown", "PageUp", "PageDown", "Home", "End"].includes(e.key)) {
            e.preventDefault();
        }

        if (key === "ctrl+f") {
            e.preventDefault();
            const search = document.querySelector("[data-shortcut-search]");
            if (search) {
                search.focus();
                search.select && search.select();
            }
            return;
        }

        if (!shortcutTarget) {
            return;
        }

        if (pageShortcuts.has(key) || (plainShortcuts.has(key) && !isTyping(e.target))) {
            e.preventDefault();
            shortcutTarget.invokeMethodAsync("OnShortcut", key);
        }
    });

    window.accession = {
        registerShortcuts: function (dotNetRef) {
            shortcutTarget = dotNetRef;
        },
        unregisterShortcuts: function () {
            shortcutTarget = null;
        },
        // Keeps the keyboard-selected row visible, also in virtualized tables (rows not in the DOM yet).
        scrollRowIntoView: function (container, index, rowHeight, headerHeight) {
            if (!container) {
                return;
            }

            const top = headerHeight + index * rowHeight;
            const bottom = top + rowHeight;
            if (top < container.scrollTop + headerHeight) {
                container.scrollTop = top - headerHeight;
            } else if (bottom > container.scrollTop + container.clientHeight) {
                container.scrollTop = bottom - container.clientHeight;
            }
        },
        // Scrolls a list's selected row (e.g. the media chosen with "Browse files") into view.
        scrollSelectedIntoView: function (container) {
            const selected = container && container.querySelector(".is-selected");
            selected && selected.scrollIntoView({ block: "nearest" });
        },
        focus: function (element) {
            element && element.focus();
        },
        // Focuses a dialog's [autofocus] field, else its first editable field, else the dialog.
        focusFirst: function (dialog) {
            if (!dialog) {
                return;
            }

            const field = dialog.querySelector("[autofocus]:not([disabled]):not([readonly])")
                || dialog.querySelector("input:not([disabled]):not([readonly]):not([type=checkbox]), textarea:not([disabled]):not([readonly]), select:not([disabled])");
            (field || dialog).focus();
            if (field && field.select && field.tagName === "INPUT") {
                field.select();
            }
        }
    };
})();
