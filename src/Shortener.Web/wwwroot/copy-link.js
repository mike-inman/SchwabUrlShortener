const listenerKey = Symbol.for("ziplio.copy-link.listener");

if (!document[listenerKey]) {
    document[listenerKey] = true;
    const resetTimers = new WeakMap();

    document.addEventListener("click", async event => {
        const button = event.target instanceof Element
            ? event.target.closest("button[data-copy-text]")
            : null;
        if (!button) return;

        const label = button.querySelector(".copy-label");
        const announcement = button.nextElementSibling?.matches(".copy-announcement")
            ? button.nextElementSibling : null;
        const copyIcon = button.querySelector(".copy-icon");
        const checkIcon = button.querySelector(".copy-check");
        const originalLabel = button.dataset.copyLabel || button.getAttribute("aria-label") || "Copy short link";
        button.dataset.copyLabel = originalLabel;
        const previousTimer = resetTimers.get(button);
        if (previousTimer) clearTimeout(previousTimer);

        try {
            if (navigator.clipboard?.writeText) {
                await navigator.clipboard.writeText(button.dataset.copyText);
            } else {
                const input = document.createElement("textarea");
                input.value = button.dataset.copyText;
                input.setAttribute("readonly", "");
                input.style.position = "fixed";
                input.style.opacity = "0";
                document.body.append(input);
                try {
                    input.select();
                    if (!document.execCommand("copy")) throw new Error("Copy unavailable");
                } finally {
                    input.remove();
                }
            }
            label.textContent = "Copied";
            if (announcement) announcement.textContent = "Copied";
            button.setAttribute("aria-label", "Copied short link");
            copyIcon.hidden = true;
            checkIcon.hidden = false;
        } catch {
            label.textContent = "Copy failed";
            if (announcement) announcement.textContent = "Copy failed";
            button.setAttribute("aria-label", "Copy failed");
            copyIcon.hidden = false;
            checkIcon.hidden = true;
        }

        resetTimers.set(button, setTimeout(() => {
            label.textContent = "Copy";
            if (announcement) announcement.textContent = "";
            button.setAttribute("aria-label", originalLabel);
            copyIcon.hidden = false;
            checkIcon.hidden = true;
            resetTimers.delete(button);
        }, 2000));
    });
}
