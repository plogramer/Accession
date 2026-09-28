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
    const pageShortcuts = new Set(["ctrl+n", "ctrl+o", "f1", "f5", "ctrl+,", "alt+1", "alt+2", "alt+3", "alt+4", "alt+5", "alt+6", "alt+7"]);
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
        // Places a floating menu next to its button, inside the window: below and right-aligned, or above when there is no room.
        placeMenu: function (trigger, menu, alignLeft) {
            if (!trigger || !menu) {
                return;
            }

            const gap = 6, margin = 8;
            const button = trigger.getBoundingClientRect();
            const width = menu.offsetWidth, height = menu.offsetHeight;
            let left = alignLeft ? button.left : button.right - width;
            left = Math.max(margin, Math.min(left, window.innerWidth - width - margin));
            let top = button.bottom + gap;
            if (top + height > window.innerHeight - margin && button.top - gap - height >= margin) {
                top = button.top - gap - height;
            }

            menu.style.left = left + "px";
            menu.style.top = top + "px";
            menu.style.visibility = "visible";
        },
        // Places a right-click menu at (x, y), kept on screen. (0, 0) comes from the keyboard (menu key, Shift+F10):
        // then it opens next to the focused element's selected row. Focuses the first item.
        placeContextMenu: function (menu, x, y) {
            if (!menu) {
                return;
            }

            const margin = 8;
            if (!x && !y) {
                const focused = document.activeElement;
                const row = (focused && focused.querySelector && focused.querySelector(".is-selected")) || focused;
                const box = row && row.getBoundingClientRect ? row.getBoundingClientRect() : { left: 100, bottom: 100 };
                x = box.left + 24;
                y = box.bottom;
            }

            const width = menu.offsetWidth, height = menu.offsetHeight;
            let left = Math.max(margin, Math.min(x, window.innerWidth - width - margin));
            let top = y + height > window.innerHeight - margin ? Math.max(margin, y - height) : y;
            menu.style.left = left + "px";
            menu.style.top = top + "px";
            menu.style.visibility = "visible";
            const first = menu.querySelector(".menu-item:not(:disabled)");
            (first || menu).focus();
            menu.onkeydown = function (e) {
                if (e.key !== "ArrowDown" && e.key !== "ArrowUp") {
                    return;
                }

                const items = Array.from(menu.querySelectorAll(".menu-item:not(:disabled)"));
                const i = items.indexOf(document.activeElement);
                const next = items[(i + (e.key === "ArrowDown" ? 1 : items.length - 1)) % items.length];
                next && next.focus();
                e.preventDefault();
            };
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
