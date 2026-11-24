// Shows which opening words of a capture are read as a date, and lets
// a backspace right after them say "this is not a date". The pattern
// comes from the server (CapturePhrase.Words).
(() => {
    const selector = "input[data-capture-words]";
    const dismissed = new WeakMap();

    function found(input) {
        const value = input.value;
        const start = value.length - value.trimStart().length;
        const pattern = new RegExp(input.dataset.captureWords, "i");
        const match = pattern.exec(value.slice(start));
        return match
            ? { start, end: start + match[0].length, text: match[0] }
            : null;
    }

    function isDismissed(input) {
        const words = dismissed.get(input);
        if (!words) {
            return false;
        }

        const text = input.value.trimStart().toLowerCase();
        if (text.startsWith(words)) {
            return true;
        }

        dismissed.delete(input);
        return false;
    }

    function matchMirror(input, mirror) {
        const style = getComputedStyle(input);
        const height = input.clientHeight
            - parseFloat(style.paddingTop)
            - parseFloat(style.paddingBottom);
        mirror.style.font = style.font;
        mirror.style.letterSpacing = style.letterSpacing;
        mirror.style.lineHeight = `${height}px`;
        mirror.style.padding = style.padding;
        mirror.style.borderWidth = style.borderWidth;
        mirror.scrollLeft = input.scrollLeft;
    }

    function refresh(input) {
        const field = input.closest(".capture-field");
        const mirror = field.querySelector(".capture-mirror");
        const status = field.querySelector(".capture-status");
        const plain = field.querySelector("input[type=hidden]");
        const off = isDismissed(input);
        const date = off ? null : found(input);

        plain.value = off ? "1" : "";
        field.classList.toggle("has-date", date !== null);
        if (!date) {
            mirror.replaceChildren();
            status.textContent = "";
            return;
        }

        const words = document.createElement("span");
        words.className = "capture-date";
        words.textContent = input.value.slice(date.start, date.end);
        mirror.replaceChildren(
            input.value.slice(0, date.start),
            words,
            input.value.slice(date.end));
        matchMirror(input, mirror);
        const message =
            `Date: ${date.text}. Backspace to keep it as text.`;
        if (status.textContent !== message) {
            status.textContent = message;
        }
    }

    function refreshAll() {
        document.querySelectorAll(selector).forEach(refresh);
    }

    document.addEventListener("input", event => {
        if (event.target.matches?.(selector)) {
            refresh(event.target);
        }
    });

    document.addEventListener("beforeinput", event => {
        const input = event.target;
        if (event.inputType !== "deleteContentBackward"
            || !input.matches?.(selector)
            || input.selectionStart !== input.selectionEnd
            || isDismissed(input)) {
            return;
        }

        const date = found(input);
        const caret = input.selectionStart;
        if (date
            && caret >= date.end
            && input.value.slice(date.end, caret).trim() === "") {
            event.preventDefault();
            dismissed.set(input, date.text.toLowerCase());
            refresh(input);
        }
    });

    // The text scrolls under the mirror in a long line.
    for (const type of ["scroll", "keyup", "click", "select"]) {
        document.addEventListener(type, event => {
            if (event.target.matches?.(selector)) {
                const mirror = event.target
                    .closest(".capture-field")
                    .querySelector(".capture-mirror");
                mirror.scrollLeft = event.target.scrollLeft;
            }
        }, true);
    }

    window.addEventListener("resize", refreshAll);
    Blazor.addEventListener("enhancedload", refreshAll);
    refreshAll();
})();
