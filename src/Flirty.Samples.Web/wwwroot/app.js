"use strict";

// Chat UI of the web sample: consumes exclusively the HTTP endpoints of Flirty.AspNetCore
// (POST/GET /flirty/sessions ...) and demonstrates resume, edit, branching, loop over list and triggers.

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
    busy: false,              // while an answer request is running: accept no further input
};

// ---------- HTTP helpers ----------

async function http(method, url, body) {
    const options = { method, headers: {} };
    if (body !== undefined) {
        options.headers["Content-Type"] = "application/json";
        options.body = JSON.stringify(body);
    }
    const response = await fetch(url, options);
    if (!response.ok) {
        let detail = "";
        try { detail = (await response.json()).detail || ""; } catch { /* no JSON body */ }
        throw new Error(`${response.status} ${response.statusText}${detail ? " – " + detail : ""}`);
    }
    if (response.status === 204) return null;
    const text = await response.text();
    return text ? JSON.parse(text) : null;
}

// ---------- Answer value encoding (raw JSON text per question type) ----------

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
        default: // SingleChoice, FreeText, Date -> JSON string
            return JSON.stringify(String(rawInput));
    }
}

function decodeForDisplay(question, rawValue) {
    let parsed = rawValue;
    try { parsed = JSON.parse(rawValue); } catch { /* value stays raw */ }
    if (question && question.type === QuestionType.Boolean) return parsed ? "Yes" : "No";
    if (question && question.type === QuestionType.SingleChoice && Array.isArray(question.options)) {
        const opt = question.options.find(o => o.value === parsed);
        if (opt) return opt.label;
    }
    if (Array.isArray(parsed)) return parsed.join(", ");
    return String(parsed);
}

// The stored value as text for an input field – unlike decodeForDisplay WITHOUT translation
// into the display form (label instead of value, "Yes" instead of true). Only this way can the result be
// sent back through encodeAnswer unchanged.
function decodeRaw(rawValue) {
    let parsed = rawValue;
    try { parsed = JSON.parse(rawValue); } catch { /* value stays raw */ }
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
        `<button class="msg__edit" title="Edit answer">✏️</button>`);
    const edit = bubble.querySelector(".msg__edit");
    edit.disabled = state.busy;
    edit.addEventListener("click", () => startEditing(answer, question, label));
    return bubble;
}

// Locks and unlocks the edit buttons for the duration of a request. The input line is cleared on submit
// anyway; without this the pencils of the answer bubbles would stay clickable, and an edit could overtake
// the still-flying answer: the server then discarded too little (it did not yet know the last answer) and
// rejected the trailing submit with 409.
function setBusy(value) {
    state.busy = value;
    for (const button of dom.chatLog.querySelectorAll(".msg__edit")) {
        button.disabled = value;
    }
}

// Builds the input control for a question into the input line: choice buttons for SingleChoice/Boolean,
// otherwise a type-correct input field. Deliberately ONE place for the open question and for the edit form –
// otherwise one path knows the types and the other does not (that was exactly the bug: the edit form
// always rendered a text field and stored its display form, i.e. "Product Manager" instead of "pm").
// `onSubmit` always receives the RAW answer value, exactly as encodeAnswer expects it.
function renderAnswerControls(question, { rawValue, submitLabel, onSubmit, leading = [], trailing = [] }) {
    const controls = [];
    let field = null;

    if (question.type === QuestionType.SingleChoice) {
        for (const option of question.options || []) {
            controls.push(button(option.label, () => onSubmit(option.value)));
        }
    } else if (question.type === QuestionType.Boolean) {
        controls.push(button("Yes", () => onSubmit(true)), button("No", () => onSubmit(false)));
    } else {
        field = document.createElement("input");
        field.className = "field";
        field.type = question.type === QuestionType.Number ? "number" : (question.type === QuestionType.Date ? "date" : "text");
        field.placeholder = "Enter an answer …";
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
        submitLabel: "Send",
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

// ---------- Flow ----------

async function loadDialogMeta() {
    // Additionally demonstrates the admin GET endpoints: load the question graph for a nice display.
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
    await refreshAndRender(result.isResumed ? "Existing session continued (resume)." : "New session started.");
}

async function refreshAndRender(statusText) {
    const stateResponse = await http("GET", `/flirty/sessions/${state.sessionId}`);
    clearChat();

    for (const answer of stateResponse.answers) {
        const question = state.questionsById.get(answer.questionId);
        renderAnswerBubble(answer, question);
    }

    if (stateResponse.status === SessionStatus.Completed || !stateResponse.currentQuestion) {
        addMessage("msg--system", "✅ Dialog completed. You can still edit earlier answers.");
        renderInput(null);
    } else {
        renderQuestionPrompt(stateResponse.currentQuestion);
        renderInput(stateResponse.currentQuestion);
    }

    renderSkills(stateResponse.answers);
    setStatus(statusText || "");
}

async function submitAnswer(question, rawInput) {
    // Remove the input control immediately: prevents double submit and (in the test) hitting the
    // stale field while the network round-trip + re-render are still running. setBusy additionally
    // locks the edit pencils – otherwise an edit overtakes the still-flying answer.
    dom.inputArea.replaceChildren();
    setBusy(true);
    setStatus("Sending …");
    try {
        const value = encodeAnswer(question.type, rawInput);
        const result = await http("POST", `/flirty/sessions/${state.sessionId}/answers`, { questionId: question.id, value });
        setBusy(false);
        await refreshAndRender(result.isCompleted ? "Dialog completed – trigger fired." : "");
    } catch (err) {
        setBusy(false);
        setStatus("Error: " + err.message);
    }
}

function startEditing(answer, question, label) {
    const type = question ? question.type : QuestionType.FreeText;

    const info = document.createElement("span");
    info.className = "pill";
    info.textContent = `Editing: ${label}`;
    const cancel = button("Cancel", () => refreshAndRender(""));
    cancel.classList.add("btn--ghost");

    renderAnswerControls(question || { type, options: [] }, {
        // The stored value is prefilled, not its display form. For a choice the question is omitted
        // anyway: there the click on the option stores its value directly.
        rawValue: decodeRaw(answer.value),
        submitLabel: "Save",
        leading: [info],
        trailing: [cancel],
        onSubmit: async rawInput => {
            const value = encodeAnswer(type, rawInput);
            dom.inputArea.replaceChildren();
            setBusy(true);
            setStatus("Saving …");
            try {
                const body = { value };
                // Within a loop each iteration carries its own answer -> edit it specifically.
                if (answer.iterationIndex != null) body.iterationIndex = answer.iterationIndex;
                const result = await http("PUT", `/flirty/sessions/${state.sessionId}/answers/${answer.questionId}`, body);
                setBusy(false);
                await refreshAndRender(`Answer edited – ${result.invalidatedAnswers} downstream answer(s) discarded.`);
            } catch (err) {
                setBusy(false);
                setStatus("Error: " + err.message);
            }
        },
    });
}

function renderSkills(answers) {
    const skills = answers
        .filter(a => a.questionKey === "skill")
        .sort((a, b) => (a.iterationIndex ?? 0) - (b.iterationIndex ?? 0))
        .map(a => decodeForDisplay(state.questionsById.get(a.questionId), a.value));
    renderList(dom.skillsList, skills.map(s => escapeHtml(s)), "No skill recorded yet.");
}

async function refreshTriggerPanels() {
    try {
        const [triggers, webhooks] = await Promise.all([
            http("GET", "/demo/triggers"),
            http("GET", "/demo/webhooks"),
        ]);
        renderList(dom.triggersList,
            (triggers || []).map(t => `<strong>${escapeHtml(t.dialogKey)}</strong> · ${t.answerCount} answers`),
            "No trigger fired yet.");
        renderList(dom.webhooksList,
            (webhooks || []).map(w => `<strong>${escapeHtml(w.event)}</strong> received`),
            "No webhook received yet.");
    } catch { /* panel refresh is best-effort */ }
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
        setStatus("Dialog metadata could not be loaded (provisioning may still be running): " + err.message);
    }

    const stored = localStorage.getItem("flirty.sessionId");
    try {
        if (stored) {
            state.sessionId = stored;
            await refreshAndRender("Session restored after reload (resume).");
        } else {
            await startSession();
        }
    } catch {
        // Stored session unknown (e.g. DB cleared) -> start fresh.
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
