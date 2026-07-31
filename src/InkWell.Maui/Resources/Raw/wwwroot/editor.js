// The InkWell chapter editor: CodeMirror 6 plus the whole JS side of the host bridge
// (contracts/chapter-editor-bridge.md).
//
// The document is markdown and nothing else. Every message to C# carries the buffer as-is, so what
// the writer sees, what CodeMirror holds, and what the database stores are the same text.

import { EditorState } from '@codemirror/state';
import { EditorView, keymap, drawSelection, highlightActiveLine, placeholder } from '@codemirror/view';
import { defaultKeymap, history, historyKeymap } from '@codemirror/commands';
import { markdown } from '@codemirror/lang-markdown';
import { livePreview, registerImage, clearImages } from './live-preview.js';

const root = document.getElementById('editor-root');
const live = document.getElementById('a11y-live');

let view = null;
let chapterId = null;
let distractionFree = false;

/* ------------------------------------------------------------------ bridge */

function send(message) {
    const payload = JSON.stringify(message);
    if (window.HybridWebView && typeof window.HybridWebView.SendRawMessage === 'function') {
        window.HybridWebView.SendRawMessage(payload);
    }
}

function announce(text) {
    // Replacing the text of an aria-live="polite" region is what gets it spoken. Clearing first
    // makes a repeated identical message announce again, which matters for "Saved".
    if (!live) {
        return;
    }
    live.textContent = '';
    window.setTimeout(() => { live.textContent = text; }, 30);
}

function reportContent(type) {
    if (!view || !chapterId) {
        return;
    }
    send({ type, chapterId, markdown: view.state.doc.toString() });
}

/* ------------------------------------------------------------------ editor */

const contentReporter = EditorView.updateListener.of((update) => {
    if (update.docChanged) {
        // Only the markdown crosses the bridge on the keystroke path — never image bytes, which
        // would make typing cost a base64 round trip (research.md §1).
        reportContent('contentChanged');
    }
});

const blurFlusher = EditorView.domEventHandlers({
    blur() {
        reportContent('flushNow');
        return false;
    },
});

const focusModeKeys = keymap.of([
    {
        // Enter focus mode. Bound here rather than in MAUI because a focused WebView consumes the
        // key before any native accelerator sees it.
        key: 'Mod-Shift-f',
        preventDefault: true,
        run: () => { send({ type: 'toggleDistractionFree' }); return true; },
    },
    {
        key: 'Escape',
        run: () => {
            if (!distractionFree) {
                return false;
            }
            send({ type: 'toggleDistractionFree' });
            return true;
        },
    },
]);

function createState(doc) {
    return EditorState.create({
        doc,
        extensions: [
            history(),
            drawSelection(),
            highlightActiveLine(),
            placeholder(root.dataset.emptyHint ?? ''),
            markdown(),
            livePreview,
            keymap.of([...defaultKeymap, ...historyKeymap]),
            focusModeKeys,
            contentReporter,
            blurFlusher,
            EditorView.lineWrapping,
            EditorView.contentAttributes.of({
                // Announces the surface as an editable multi-line region rather than a bare div.
                role: 'textbox',
                'aria-multiline': 'true',
                'aria-label': 'Chapter text',
                spellcheck: 'true',
            }),
        ],
    });
}

function loadChapter(payload) {
    chapterId = payload.chapterId;

    clearImages();
    for (const image of payload.images ?? []) {
        registerImage(image.id, image.dataUri, image.altText);
    }

    // A fresh state per chapter — one CodeMirror instance never holds the whole manuscript, which
    // is what keeps a 150,000-word book responsive (SC-004).
    if (view) {
        view.setState(createState(payload.markdown ?? ''));
    } else {
        view = new EditorView({ state: createState(payload.markdown ?? ''), parent: root });
    }

    view.focus();
    announce('Chapter open. Your words are saved automatically.');
}

function setDistractionFree(enabled) {
    distractionFree = Boolean(enabled);
    document.documentElement.classList.toggle('distraction-free', distractionFree);

    // Nothing about the document or the selection is touched, so the caret is exactly where it was
    // (FR-008, SC-006).
    if (view) {
        view.focus();
    }

    announce(distractionFree
        ? 'Distraction-free mode on. Press Escape to leave.'
        : 'Distraction-free mode off.');
}

function focusEditor(payload) {
    if (!view) {
        return;
    }
    if (payload && typeof payload.selection === 'number') {
        view.dispatch({ selection: { anchor: payload.selection } });
    }
    view.focus();
}

function insertImage(payload) {
    if (!view || !payload || !payload.id) {
        return;
    }

    registerImage(payload.id, payload.dataUri, payload.altText);

    const alt = payload.altText ?? '';
    const snippet = `![${alt}](inkwell-img://${payload.id})`;
    const at = view.state.selection.main.head;
    view.dispatch({
        changes: { from: at, insert: snippet },
        selection: { anchor: at + snippet.length },
    });
    view.focus();

    if (!alt.trim()) {
        send({ type: 'imageMissingAltText', imageId: payload.id });
        announce('Image added. It still needs alternative text.');
    } else {
        announce('Image added.');
    }
}

/* -------------------------------------------------------------- host input */

function handleHostMessage(raw) {
    // Any message at all proves the host is listening, so the readiness announcement can stop.
    stopAnnouncingReady();

    let message;
    try {
        message = JSON.parse(raw);
    } catch {
        return;
    }

    const payload = message.payload ?? {};
    switch (message.type) {
        case 'loadChapter': loadChapter(payload); break;
        case 'setDistractionFree': setDistractionFree(payload.enabled); break;
        case 'focusEditor': focusEditor(payload); break;
        case 'insertImage': insertImage(payload); break;
        default: break;
    }
}

// MAUI exposes the incoming channel differently across versions; both routes are wired so the
// editor works on every supported host without a version check.
if (window.HybridWebView && typeof window.HybridWebView.AddRawMessageListener === 'function') {
    window.HybridWebView.AddRawMessageListener(handleHostMessage);
}

window.addEventListener('HybridWebViewMessageReceived', (event) => {
    const detail = event.detail;
    if (detail && typeof detail.message === 'string') {
        handleHostMessage(detail.message);
    }
});

// Paste and drop both route through the same insert request the native picker uses, so an image
// arrives in the store the same way however the writer added it (FR-003a).
function requestImageInsert(file) {
    const reader = new FileReader();
    reader.onload = () => {
        const result = String(reader.result ?? '');
        const comma = result.indexOf(',');
        if (comma < 0) {
            return;
        }
        send({
            type: 'insertImageRequested',
            chapterId,
            bytes: result.slice(comma + 1),
            mimeType: file.type || 'image/png',
        });
    };
    reader.readAsDataURL(file);
}

root.addEventListener('paste', (event) => {
    const items = event.clipboardData ? event.clipboardData.files : null;
    if (!items || items.length === 0) {
        return;
    }
    for (const file of items) {
        if (file.type.startsWith('image/')) {
            event.preventDefault();
            requestImageInsert(file);
        }
    }
});

root.addEventListener('drop', (event) => {
    const files = event.dataTransfer ? event.dataTransfer.files : null;
    if (!files || files.length === 0) {
        return;
    }
    for (const file of files) {
        if (file.type.startsWith('image/')) {
            event.preventDefault();
            requestImageInsert(file);
        }
    }
});

/* ------------------------------------------------------------------- ready */

// The host will not send anything — not even the `loadChapter` that tells this editor which
// chapter it is editing — until it hears that the editor exists. Announcing once is not enough:
// `window.HybridWebView` is injected by the platform and, on some engines, is not present the
// instant this bundle runs. So the announcement repeats until the host answers with any message.
//
// Getting this wrong is silent and total: no chapter id means `reportContent` suppresses every
// change, and the writer's typing is never saved.
let readyAnnouncer = null;
let readyAttempts = 0;

function stopAnnouncingReady() {
    if (readyAnnouncer !== null) {
        window.clearInterval(readyAnnouncer);
        readyAnnouncer = null;
    }
}

function announceReady() {
    readyAttempts += 1;
    send({ type: 'editorReady' });

    // ~10 seconds. Past that the host's own timeout has told the writer something is wrong, and a
    // forever-running interval would just burn battery.
    if (readyAttempts > 60) {
        stopAnnouncingReady();
    }
}

// An empty editor until the host sends a chapter, so the surface is never a blank unlabelled box.
view = new EditorView({ state: createState(''), parent: root });

announceReady();
readyAnnouncer = window.setInterval(announceReady, 150);
