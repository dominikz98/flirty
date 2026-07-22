"use strict";

// Chat-UI der Web-Sample: konsumiert ausschließlich die HTTP-Endpunkte von Flirty.AspNetCore
// (POST/GET /flirty/sessions ...) und demonstriert Resume, Edit, Branching, Loop über Liste und Trigger.

const QuestionType = { SingleChoice: 0, MultiChoice: 1, FreeText: 2, Number: 3, Date: 4, Boolean: 5 };
const SessionStatus = { InProgress: 0, Completed: 1, Abandoned: 2 };
const DIALOG_KEY = "web-onboarding";

const dom = {
    chatLog: document.getElementById("chatLog"),
    inputArea: document.getElementById("inputArea"),
    statusLine: document.getElementById("statusLine"),
    userKeyLabel: document.getElementById("userKeyLabel"),
    resetButton: document.getElementById("resetButton"),
    skillsList: document.getElementById("skillsList"),
    triggersList: document.getElementById("triggersList"),
    webhooksList: document.getElementById("webhooksList"),
};

const state = {
    userKey: null,
    sessionId: null,
    questionsById: new Map(), // questionId -> { key, text, type, options }
};

// ---------- HTTP-Helfer ----------

async function http(method, url, body) {
    const options = { method, headers: {} };
    if (body !== undefined) {
        options.headers["Content-Type"] = "application/json";
        options.body = JSON.stringify(body);
    }
    const response = await fetch(url, options);
    if (!response.ok) {
        let detail = "";
        try { detail = (await response.json()).detail || ""; } catch { /* kein JSON-Body */ }
        throw new Error(`${response.status} ${response.statusText}${detail ? " – " + detail : ""}`);
    }
    if (response.status === 204) return null;
    const text = await response.text();
    return text ? JSON.parse(text) : null;
}

// ---------- Antwortwert-Kodierung (roher JSON-Text je Fragetyp) ----------

function encodeAnswer(type, rawInput) {
    switch (type) {
        case QuestionType.Boolean:
            return rawInput === true || rawInput === "true" ? "true" : "false";
        case QuestionType.Number: {
            const n = Number(String(rawInput).replace(",", "."));
            return Number.isFinite(n) ? String(n) : JSON.stringify(String(rawInput));
        }
        case QuestionType.MultiChoice:
            return JSON.stringify(Array.isArray(rawInput) ? rawInput : [rawInput]);
        default: // SingleChoice, FreeText, Date -> JSON-String
            return JSON.stringify(String(rawInput));
    }
}

function decodeForDisplay(question, rawValue) {
    let parsed = rawValue;
    try { parsed = JSON.parse(rawValue); } catch { /* Wert bleibt roh */ }
    if (question && question.type === QuestionType.Boolean) return parsed ? "Ja" : "Nein";
    if (question && question.type === QuestionType.SingleChoice && Array.isArray(question.options)) {
        const opt = question.options.find(o => o.value === parsed);
        if (opt) return opt.label;
    }
    if (Array.isArray(parsed)) return parsed.join(", ");
    return String(parsed);
}

// Der gespeicherte Wert als Text für ein Eingabefeld – im Gegensatz zu decodeForDisplay OHNE Übersetzung
// in die Anzeigeform (Label statt Wert, "Ja" statt true). Nur so lässt sich das Ergebnis unverändert
// wieder durch encodeAnswer schicken.
function decodeRaw(rawValue) {
    let parsed = rawValue;
    try { parsed = JSON.parse(rawValue); } catch { /* Wert bleibt roh */ }
    return Array.isArray(parsed) ? parsed.join(", ") : String(parsed);
}

// ---------- Rendering ----------

function addMessage(cssClass, html) {
    const el = document.createElement("div");
    el.className = `msg ${cssClass}`;
    el.innerHTML = html;
    dom.chatLog.appendChild(el);
    dom.chatLog.scrollTop = dom.chatLog.scrollHeight;
    return el;
}

function clearChat() {
    dom.chatLog.replaceChildren();
    dom.inputArea.replaceChildren();
}

function setStatus(text) {
    dom.statusLine.textContent = text || "";
}

function renderQuestionPrompt(question) {
    const bubble = addMessage("msg--bot", `<span class="msg__key">${question.key}</span>${escapeHtml(question.text)}`);
    return bubble;
}

function renderAnswerBubble(answer, question) {
    const label = question ? escapeHtml(question.text) : answer.questionKey;
    const value = escapeHtml(decodeForDisplay(question, answer.value));
    const iter = answer.iterationIndex != null ? ` #${answer.iterationIndex + 1}` : "";
    const bubble = addMessage("msg--user",
        `<span class="msg__key">${answer.questionKey}${iter}</span>${value}` +
        `<button class="msg__edit" title="Antwort editieren">✏️</button>`);
    bubble.querySelector(".msg__edit").addEventListener("click",
        () => startEditing(answer, question, label));
    return bubble;
}

// Baut die Eingabesteuerung zu einer Frage in die Eingabezeile: Auswahl-Buttons bei SingleChoice/Boolean,
// sonst ein typrichtiges Eingabefeld. Bewusst EINE Stelle für die offene Frage und für das Edit-Formular –
// sonst kennt der eine Pfad die Typen und der andere nicht (genau das war der Fehler: das Edit-Formular
// hat immer ein Textfeld gerendert und dessen Anzeigeform gespeichert, also "Product Manager" statt "pm").
// `onSubmit` bekommt immer den ROHEN Antwortwert, so wie ihn encodeAnswer erwartet.
function renderAnswerControls(question, { rawValue, submitLabel, onSubmit, leading = [], trailing = [] }) {
    const controls = [];
    let field = null;

    if (question.type === QuestionType.SingleChoice) {
        for (const option of question.options || []) {
            controls.push(button(option.label, () => onSubmit(option.value)));
        }
    } else if (question.type === QuestionType.Boolean) {
        controls.push(button("Ja", () => onSubmit(true)), button("Nein", () => onSubmit(false)));
    } else {
        field = document.createElement("input");
        field.className = "field";
        field.type = question.type === QuestionType.Number ? "number" : (question.type === QuestionType.Date ? "date" : "text");
        field.placeholder = "Antwort eingeben …";
        if (rawValue !== undefined) field.value = rawValue;
        const send = () => {
            const value = field.value.trim();
            if (value) onSubmit(value);
        };
        field.addEventListener("keydown", e => { if (e.key === "Enter") send(); });
        controls.push(field, button(submitLabel, send));
    }

    dom.inputArea.replaceChildren(...leading, ...controls, ...trailing);
    if (field) field.focus();
}

function renderInput(question) {
    if (!question) {
        dom.inputArea.replaceChildren();
        return;
    }

    renderAnswerControls(question, {
        submitLabel: "Senden",
        onSubmit: value => submitAnswer(question, value),
    });
}

function button(label, onClick) {
    const btn = document.createElement("button");
    btn.type = "button";
    btn.className = "btn";
    btn.textContent = label;
    btn.addEventListener("click", onClick);
    return btn;
}

function escapeHtml(text) {
    const div = document.createElement("div");
    div.textContent = text;
    return div.innerHTML;
}

// ---------- Ablauf ----------

async function loadDialogMeta() {
    // Demonstriert zusätzlich die Admin-GET-Endpunkte: den Frage-Graphen für hübsche Anzeige laden.
    const dialogs = await http("GET", "/flirty/admin/dialogs");
    const meta = (dialogs || []).find(d => d.key === DIALOG_KEY);
    if (!meta) return;
    const detail = await http("GET", `/flirty/admin/dialogs/${meta.id}`);
    state.questionsById.clear();
    for (const q of detail.questions) {
        state.questionsById.set(q.id, { key: q.key, text: q.text, type: q.type, options: q.options });
    }
}

async function startSession() {
    const result = await http("POST", "/flirty/sessions", { dialogKey: DIALOG_KEY, externalUserKey: state.userKey });
    state.sessionId = result.sessionId;
    localStorage.setItem("flirty.sessionId", state.sessionId);
    await refreshAndRender(result.isResumed ? "Bestehende Session fortgesetzt (Resume)." : "Neue Session gestartet.");
}

async function refreshAndRender(statusText) {
    const stateResponse = await http("GET", `/flirty/sessions/${state.sessionId}`);
    clearChat();

    for (const answer of stateResponse.answers) {
        const question = state.questionsById.get(answer.questionId);
        renderAnswerBubble(answer, question);
    }

    if (stateResponse.status === SessionStatus.Completed || !stateResponse.currentQuestion) {
        addMessage("msg--system", "✅ Dialog abgeschlossen. Du kannst frühere Antworten weiterhin editieren.");
        renderInput(null);
    } else {
        renderQuestionPrompt(stateResponse.currentQuestion);
        renderInput(stateResponse.currentQuestion);
    }

    renderSkills(stateResponse.answers);
    setStatus(statusText || "");
}

async function submitAnswer(question, rawInput) {
    // Eingabesteuerung sofort entfernen: verhindert Doppel-Submit und (im Test) das Treffen des
    // veralteten Feldes, während der Netzwerk-Roundtrip + Re-Render noch laufen.
    dom.inputArea.replaceChildren();
    setStatus("Sende …");
    try {
        const value = encodeAnswer(question.type, rawInput);
        const result = await http("POST", `/flirty/sessions/${state.sessionId}/answers`, { questionId: question.id, value });
        await refreshAndRender(result.isCompleted ? "Dialog abgeschlossen – Trigger ausgelöst." : "");
    } catch (err) {
        setStatus("Fehler: " + err.message);
    }
}

function startEditing(answer, question, label) {
    const type = question ? question.type : QuestionType.FreeText;

    const info = document.createElement("span");
    info.className = "pill";
    info.textContent = `Editiere: ${label}`;
    const cancel = button("Abbrechen", () => refreshAndRender(""));
    cancel.classList.add("btn--ghost");

    renderAnswerControls(question || { type, options: [] }, {
        // Vorbelegt wird der gespeicherte Wert, nicht seine Anzeigeform. Bei einer Auswahl entfällt die
        // Frage ohnehin: dort speichert der Klick auf die Option direkt deren Wert.
        rawValue: decodeRaw(answer.value),
        submitLabel: "Speichern",
        leading: [info],
        trailing: [cancel],
        onSubmit: async rawInput => {
            const value = encodeAnswer(type, rawInput);
            dom.inputArea.replaceChildren();
            setStatus("Speichere …");
            try {
                const body = { value };
                // Innerhalb einer Schleife trägt jede Iteration eine eigene Antwort -> gezielt editieren.
                if (answer.iterationIndex != null) body.iterationIndex = answer.iterationIndex;
                const result = await http("PUT", `/flirty/sessions/${state.sessionId}/answers/${answer.questionId}`, body);
                await refreshAndRender(`Antwort editiert – ${result.invalidatedAnswers} nachgelagerte Antwort(en) verworfen.`);
            } catch (err) {
                setStatus("Fehler: " + err.message);
            }
        },
    });
}

function renderSkills(answers) {
    const skills = answers
        .filter(a => a.questionKey === "skill")
        .sort((a, b) => (a.iterationIndex ?? 0) - (b.iterationIndex ?? 0))
        .map(a => decodeForDisplay(state.questionsById.get(a.questionId), a.value));
    renderList(dom.skillsList, skills.map(s => escapeHtml(s)), "Noch keine Fähigkeit erfasst.");
}

async function refreshTriggerPanels() {
    try {
        const [triggers, webhooks] = await Promise.all([
            http("GET", "/demo/triggers"),
            http("GET", "/demo/webhooks"),
        ]);
        renderList(dom.triggersList,
            (triggers || []).map(t => `<strong>${escapeHtml(t.dialogKey)}</strong> · ${t.answerCount} Antworten`),
            "Noch kein Trigger ausgelöst.");
        renderList(dom.webhooksList,
            (webhooks || []).map(w => `<strong>${escapeHtml(w.event)}</strong> empfangen`),
            "Noch kein Webhook empfangen.");
    } catch { /* Panel-Aktualisierung ist best-effort */ }
}

function renderList(container, htmlItems, emptyText) {
    container.replaceChildren();
    if (!htmlItems.length) {
        const li = document.createElement("li");
        li.className = "empty";
        li.textContent = emptyText;
        container.appendChild(li);
        return;
    }
    for (const html of htmlItems) {
        const li = document.createElement("li");
        li.innerHTML = html;
        container.appendChild(li);
    }
}

function ensureUserKey() {
    let key = localStorage.getItem("flirty.userKey");
    if (!key) {
        key = "user-" + Math.random().toString(36).slice(2, 8);
        localStorage.setItem("flirty.userKey", key);
    }
    state.userKey = key;
    dom.userKeyLabel.textContent = key;
}

async function boot() {
    ensureUserKey();
    dom.resetButton.addEventListener("click", resetSession);

    try {
        await loadDialogMeta();
    } catch (err) {
        setStatus("Dialog-Metadaten konnten nicht geladen werden (Provisioning evtl. noch aktiv): " + err.message);
    }

    const stored = localStorage.getItem("flirty.sessionId");
    try {
        if (stored) {
            state.sessionId = stored;
            await refreshAndRender("Session nach Reload wiederhergestellt (Resume).");
        } else {
            await startSession();
        }
    } catch {
        // Gespeicherte Session unbekannt (z. B. DB geleert) -> neu starten.
        localStorage.removeItem("flirty.sessionId");
        await startSession();
    }

    refreshTriggerPanels();
    setInterval(refreshTriggerPanels, 2000);
}

async function resetSession() {
    localStorage.removeItem("flirty.sessionId");
    localStorage.removeItem("flirty.userKey");
    ensureUserKey();
    state.sessionId = null;
    await startSession();
}

document.addEventListener("DOMContentLoaded", boot);
