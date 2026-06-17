(() => {
    const stream = document.getElementById("chatStream");
    const form = document.getElementById("chatForm");
    const input = document.getElementById("questionInput");
    const liveStatus = document.getElementById("liveStatus");
    const documentList = document.getElementById("documentList");
    const sessionId = window.ass2Chat?.sessionId ?? crypto.randomUUID();

    setupTabs();

    if (!stream || !form || !input || typeof signalR === "undefined") {
        if (liveStatus) {
            liveStatus.textContent = "Offline";
        }
        return;
    }

    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/chatHub")
        .withAutomaticReconnect()
        .build();

    connection.on("ReceiveAnswer", (turn) => {
        appendMessage("Sinh vien", turn.question, "user");
        appendMessage("Bot", formatAnswer(turn), "bot");
    });

    connection.on("DocumentUploaded", (uploadedDocument) => {
        if (!documentList) {
            return;
        }

        const item = document.createElement("article");
        item.className = "document-item document-item-new";
        item.innerHTML = `<strong>${escapeHtml(uploadedDocument.title)}</strong>
            <span>${escapeHtml(uploadedDocument.subject)} - ${escapeHtml(uploadedDocument.teacher)}</span>
            <small>Realtime update ${escapeHtml(uploadedDocument.uploadedAt)}</small>`;
        documentList.prepend(item);
    });

    connection.onreconnecting(() => {
        liveStatus.textContent = "Reconnecting";
    });

    connection.onreconnected(async () => {
        liveStatus.textContent = "Live";
        await connection.invoke("JoinSession", sessionId);
    });

    form.addEventListener("submit", async (event) => {
        event.preventDefault();
        const question = input.value.trim();
        if (!question) {
            return;
        }

        input.value = "";
        await connection.invoke("Ask", sessionId, question);
    });

    start();

    async function start() {
        try {
            await connection.start();
            await connection.invoke("JoinSession", sessionId);
            liveStatus.textContent = "Live";
        } catch {
            liveStatus.textContent = "Offline";
        }
    }

    function appendMessage(author, body, type) {
        const article = document.createElement("article");
        article.className = `message message-${type}`;
        article.innerHTML = `<strong>${escapeHtml(author)}</strong><p>${body}</p>`;
        stream.appendChild(article);
        stream.scrollTop = stream.scrollHeight;
    }

    function formatAnswer(turn) {
        const sourceList = (turn.sources ?? [])
            .map((source) => `<li>${escapeHtml(source.title)} | ${escapeHtml(source.subject)} | ${escapeHtml(source.chapter)} | ${escapeHtml(source.teacher)}</li>`)
            .join("");

        const sources = sourceList ? `<ul class="source-list">${sourceList}</ul>` : "";
        return `${escapeHtml(turn.answer)}${sources}`;
    }

    function escapeHtml(value) {
        return String(value)
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    function setupTabs() {
        const buttons = Array.from(document.querySelectorAll("[data-tab-target]"));
        const panels = Array.from(document.querySelectorAll(".tab-panel"));

        buttons.forEach((button) => {
            button.addEventListener("click", () => {
                const targetId = button.getAttribute("data-tab-target");

                buttons.forEach((item) => item.classList.toggle("active", item === button));
                panels.forEach((panel) => {
                    const isActive = panel.id === targetId;
                    panel.classList.toggle("active", isActive);
                    panel.hidden = !isActive;
                });
            });
        });
    }
})();
