(() => {
    const stream = document.getElementById("chatStream");
    const form = document.getElementById("chatForm");
    const input = document.getElementById("questionInput");
    const liveStatus = document.getElementById("liveStatus");
    const documentList = document.getElementById("documentList");
    const subjectFilter = document.getElementById("subjectFilter");
    const chapterFilter = document.getElementById("chapterFilter");
    const clearDocumentFilters = document.getElementById("clearDocumentFilters");
    const visibleDocumentCount = document.getElementById("visibleDocumentCount");
    const totalDocumentCount = document.getElementById("totalDocumentCount");
    const sessionId = window.ass2Chat?.sessionId ?? crypto.randomUUID();
    const activeTabOverride = sessionStorage.getItem("ass2ActiveTab");
    if (activeTabOverride) {
        sessionStorage.removeItem("ass2ActiveTab");
    }
    const initialHistory = Array.isArray(window.ass2Chat?.initialHistory)
        ? window.ass2Chat.initialHistory
        : [];

    setupTabs();
    setupDocumentFilters();
    renderInitialHistory();

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

        addFilterOption(subjectFilter, uploadedDocument.subject);
        addFilterOption(chapterFilter, uploadedDocument.chapter);
        documentList.prepend(createDocumentItem(uploadedDocument));
        applyDocumentFilters();
    });

    connection.on("AccountUpdated", () => {
        reloadKeepingCurrentTab();
    });

    connection.on("SubjectsChanged", () => {
        if (document.getElementById("subjectsTab") || document.getElementById("adminTab")) {
            reloadKeepingCurrentTab();
        }
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
            .map((source) => {
                const meta = `${escapeHtml(source.title)} | ${escapeHtml(source.subject)} | ${escapeHtml(source.chapter)} | GV: ${escapeHtml(source.teacher ?? "")}`;
                const snippet = source.snippet
                    ? `<small>${escapeHtml(source.snippet)}</small>`
                    : "";
                return `<li><strong>${meta}</strong>${snippet}</li>`;
            })
            .join("");

        const sources = sourceList ? `<div class="source-block"><span>Nguon tai lieu</span><ul class="source-list">${sourceList}</ul></div>` : "";
        return `${escapeHtml(turn.answer)}${sources}`;
    }

    function renderInitialHistory() {
        if (!stream || initialHistory.length === 0) {
            return;
        }

        stream.replaceChildren();
        initialHistory.forEach((turn) => {
            appendMessage("Sinh vien", turn.question, "user");
            appendMessage("Bot", formatAnswer(turn), "bot");
        });
    }

    function escapeHtml(value) {
        return String(value)
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    function createDocumentItem(uploadedDocument) {
        const item = document.createElement("article");
        item.className = "document-item document-item-new";
        item.dataset.subject = uploadedDocument.subject ?? "";
        item.dataset.chapter = uploadedDocument.chapter ?? "";

        const header = document.createElement("div");
        header.className = "document-header";

        const title = document.createElement("strong");
        title.textContent = uploadedDocument.title ?? "Tai lieu moi";

        const department = document.createElement("span");
        department.className = "department-badge";
        department.textContent = `Bo mon: ${uploadedDocument.department ?? ""}`;

        header.append(title, department);

        const meta = document.createElement("div");
        meta.className = "document-meta";

        const subject = document.createElement("span");
        subject.textContent = `Mon: ${uploadedDocument.subject ?? ""}`;

        const chapter = document.createElement("span");
        chapter.textContent = `Chuong: ${uploadedDocument.chapter ?? ""}`;

        const teacher = document.createElement("span");
        teacher.textContent = `GV: ${uploadedDocument.teacher ?? ""}`;

        meta.append(subject, chapter, teacher);

        const detailsLine = document.createElement("small");
        detailsLine.textContent = `${uploadedDocument.fileName ?? ""} | Upload: ${uploadedDocument.uploadedBy ?? ""} | ${uploadedDocument.uploadedAt ?? ""}`;

        const viewer = document.createElement("details");
        viewer.className = "document-viewer";

        const summary = document.createElement("summary");
        summary.textContent = "Xem tai lieu";

        const content = document.createElement("pre");
        content.textContent = uploadedDocument.content ?? "";

        viewer.append(summary, content);
        item.append(header, meta, detailsLine, viewer);

        return item;
    }

    function setupTabs() {
        const buttons = Array.from(document.querySelectorAll("[data-tab-target]"));
        const panels = Array.from(document.querySelectorAll(".tab-panel"));

        buttons.forEach((button) => {
            button.addEventListener("click", () => {
                activateTab(button.getAttribute("data-tab-target"));
            });
        });

        if (activeTabOverride || window.ass2Chat?.activeTab) {
            activateTab(activeTabOverride || window.ass2Chat.activeTab);
        }

        function activateTab(targetId) {
            if (!targetId) {
                return;
            }

            buttons.forEach((item) => {
                item.classList.toggle("active", item.getAttribute("data-tab-target") === targetId);
            });
            panels.forEach((panel) => {
                const isActive = panel.id === targetId;
                panel.classList.toggle("active", isActive);
                panel.hidden = !isActive;
            });
        }
    }

    function reloadKeepingCurrentTab() {
        const activePanel = document.querySelector(".tab-panel.active");
        if (activePanel?.id) {
            sessionStorage.setItem("ass2ActiveTab", activePanel.id);
        }

        window.location.reload();
    }

    function setupDocumentFilters() {
        if (!documentList || !subjectFilter || !chapterFilter) {
            return;
        }

        subjectFilter.addEventListener("change", applyDocumentFilters);
        chapterFilter.addEventListener("change", applyDocumentFilters);

        if (clearDocumentFilters) {
            clearDocumentFilters.addEventListener("click", () => {
                subjectFilter.value = "";
                chapterFilter.value = "";
                applyDocumentFilters();
            });
        }

        applyDocumentFilters();
    }

    function applyDocumentFilters() {
        if (!documentList || !subjectFilter || !chapterFilter) {
            return;
        }

        const subject = subjectFilter.value;
        const chapter = chapterFilter.value;
        const documents = Array.from(documentList.querySelectorAll(".document-item"));
        let visibleCount = 0;

        documents.forEach((item) => {
            const matchesSubject = !subject || item.dataset.subject === subject;
            const matchesChapter = !chapter || item.dataset.chapter === chapter;
            const isVisible = matchesSubject && matchesChapter;

            item.hidden = !isVisible;
            if (isVisible) {
                visibleCount++;
            }
        });

        if (visibleDocumentCount) {
            visibleDocumentCount.textContent = String(visibleCount);
        }

        if (totalDocumentCount) {
            totalDocumentCount.textContent = String(documents.length);
        }
    }

    function addFilterOption(select, value) {
        if (!select || !value) {
            return;
        }

        const exists = Array.from(select.options).some((option) => option.value === value);
        if (exists) {
            return;
        }

        const option = document.createElement("option");
        option.value = value;
        option.textContent = value;
        select.append(option);
    }
})();
