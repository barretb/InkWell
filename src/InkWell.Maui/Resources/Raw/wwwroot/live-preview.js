// Obsidian-style live preview for CodeMirror 6.
//
// The writer sees formatted prose and inline images while they type, but the document itself stays
// plain markdown — `view.state.doc.toString()` is exactly what gets stored. That is the whole point
// of the decoration approach over a WYSIWYG editor: there is no serialisation step that could
// corrupt a manuscript someone has worked on for a year (research.md §1).
//
// Two rules govern what is hidden:
//   1. Markdown punctuation is hidden only on lines the cursor is not on. Put the caret in a line
//      and its raw syntax reappears, so the text stays directly editable.
//   2. Decorations only ever *style* or *replace* — they never remove text from the document. A
//      screen reader reading the editor still encounters every character (FR-019).

import { syntaxTree } from '@codemirror/language';
import { Decoration, ViewPlugin, WidgetType } from '@codemirror/view';
import { RangeSetBuilder } from '@codemirror/state';

/** Inline images, resolved by the host to data URIs, keyed by image id. */
const imageRegistry = new Map();

/** Registers or replaces an image the host has embedded. */
export function registerImage(id, dataUri, altText) {
    imageRegistry.set(id, { dataUri, altText: altText ?? null });
}

/** Forgets every registered image; called when a different chapter is loaded. */
export function clearImages() {
    imageRegistry.clear();
}

/** Reports images referenced by the document that have no alt text (FR-019 accessibility gap). */
export function imagesMissingAltText() {
    const missing = [];
    for (const [id, image] of imageRegistry) {
        if (!image.altText || image.altText.trim() === '') {
            missing.push(id);
        }
    }
    return missing;
}

const HIDDEN = Decoration.replace({});

const MARK = {
    ATXHeading1: 'ink-h1',
    ATXHeading2: 'ink-h2',
    ATXHeading3: 'ink-h3',
    ATXHeading4: 'ink-h3',
    ATXHeading5: 'ink-h3',
    ATXHeading6: 'ink-h3',
    StrongEmphasis: 'ink-strong',
    Emphasis: 'ink-em',
    Strikethrough: 'ink-strike',
    InlineCode: 'ink-code',
    Blockquote: 'ink-quote',
    Link: 'ink-link',
};

// Node types that are pure punctuation and carry no reader-facing text.
const SYNTAX_NODES = new Set([
    'HeaderMark',
    'EmphasisMark',
    'StrikethroughMark',
    'CodeMark',
    'QuoteMark',
    'LinkMark',
    'URL',
]);

/** Renders an embedded image inline, with its alt text attached. */
class ImageWidget extends WidgetType {
    constructor(id, dataUri, altText) {
        super();
        this.id = id;
        this.dataUri = dataUri;
        this.altText = altText;
    }

    eq(other) {
        return other.id === this.id && other.dataUri === this.dataUri && other.altText === this.altText;
    }

    toDOM() {
        const wrapper = document.createElement('span');

        const img = document.createElement('img');
        img.className = 'ink-image';
        img.src = this.dataUri;
        // An empty alt attribute would tell a screen reader the image is decorative, which is a
        // claim we cannot make about a writer's illustration. Missing alt text is surfaced instead.
        img.alt = this.altText ?? '';
        wrapper.appendChild(img);

        if (!this.altText || this.altText.trim() === '') {
            const badge = document.createElement('span');
            badge.className = 'ink-image-missing-alt';
            badge.textContent = 'Image needs alternative text';
            wrapper.appendChild(badge);
        }

        return wrapper;
    }

    ignoreEvent() {
        return false;
    }
}

function cursorLines(state) {
    const lines = new Set();
    for (const range of state.selection.ranges) {
        const from = state.doc.lineAt(range.from).number;
        const to = state.doc.lineAt(range.to).number;
        for (let line = from; line <= to; line++) {
            lines.add(line);
        }
    }
    return lines;
}

function buildDecorations(view) {
    const builder = new RangeSetBuilder();
    const active = cursorLines(view.state);
    const pending = [];

    for (const { from, to } of view.visibleRanges) {
        syntaxTree(view.state).iterate({
            from,
            to,
            enter(node) {
                const cls = MARK[node.name];
                if (cls && node.to > node.from) {
                    pending.push({ from: node.from, to: node.to, deco: Decoration.mark({ class: cls }) });
                    return;
                }

                if (node.name === 'Image') {
                    const raw = view.state.doc.sliceString(node.from, node.to);
                    const match = /!\[(.*?)\]\((.*?)\)/.exec(raw);
                    if (!match) {
                        return;
                    }

                    const target = match[2];
                    const id = target.startsWith('inkwell-img://') ? target.slice('inkwell-img://'.length) : null;
                    const registered = id ? imageRegistry.get(id) : null;
                    const dataUri = registered ? registered.dataUri : (target.startsWith('data:') ? target : null);
                    if (!dataUri) {
                        return;
                    }

                    const altText = match[1] || (registered ? registered.altText : null);
                    const line = view.state.doc.lineAt(node.from).number;
                    if (!active.has(line)) {
                        pending.push({
                            from: node.from,
                            to: node.to,
                            deco: Decoration.replace({ widget: new ImageWidget(id ?? target, dataUri, altText) }),
                        });
                    }

                    return;
                }

                if (SYNTAX_NODES.has(node.name)) {
                    const line = view.state.doc.lineAt(node.from).number;
                    if (!active.has(line)) {
                        pending.push({ from: node.from, to: node.to, deco: HIDDEN });
                    }
                }
            },
        });
    }

    // RangeSetBuilder requires ascending, non-overlapping starts; the tree walk yields marks and
    // replacements interleaved, so they are sorted before being added.
    pending.sort((a, b) => a.from - b.from || a.to - b.to);
    let lastFrom = -1;
    let lastTo = -1;
    for (const item of pending) {
        if (item.from < lastFrom || (item.from < lastTo && item.deco === HIDDEN)) {
            continue;
        }
        builder.add(item.from, item.to, item.deco);
        lastFrom = item.from;
        lastTo = Math.max(lastTo, item.to);
    }

    return builder.finish();
}

/** The live-preview extension. */
export const livePreview = ViewPlugin.fromClass(
    class {
        constructor(view) {
            this.decorations = buildDecorations(view);
        }

        update(update) {
            // Rebuilt on selection change too: that is what makes syntax reappear on the line the
            // writer moves the caret to.
            if (update.docChanged || update.viewportChanged || update.selectionSet) {
                this.decorations = buildDecorations(update.view);
            }
        }
    },
    { decorations: (plugin) => plugin.decorations },
);
