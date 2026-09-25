# Writing a chapter

## Saving

There is no save button. InkWell commits your chapter a second or two after you stop typing, and
straight away when you:

- leave the chapter,
- enter or leave focus mode,
- or put the app in the background or close it.

At worst an unexpected shutdown costs you the last moment of typing, never a session's work.

The status line under the editor tells you where you stand — "Saved", the word counts, and your
goal for the day. If InkWell cannot reach the editor at all, it says **"Not saved — the writing
surface did not load"** and shows an explanation. That message means what it says: stop typing and
restart the app, because nothing you write is being kept.

## Formatting

Chapters are written in Markdown, and the editor shows it formatted as you type rather than as
symbols. Put `**` around a word and it becomes bold; the markers disappear once your cursor leaves
the line.

| To get | Type |
|---|---|
| **bold** | `**bold**` |
| *italic* | `*italic*` |
| A heading | `# Heading` |
| A larger heading's subheading | `## Subheading` |
| A bulleted list | `- item` on its own line |
| A numbered list | `1. item` on its own line |
| A block quote | `> quoted text` |
| A scene break | `---` on its own line |

Your chapter is stored as the Markdown itself, not as a rendering of it. Nothing is reinterpreted
or reformatted behind your back, and a chapter you wrote years ago opens exactly as you left it.

## Word counts

The counts under the editor are **prose words only**. Markdown symbols, link addresses, and image
markers are not counted, so `**mill**` counts as one word and so does `mill`. The chapter count and
the manuscript count both work this way, and so does your daily goal.

## Images

Choose **Add image** to embed a picture at your cursor.

InkWell copies the image *into* your manuscript rather than remembering where it came from. Move or
delete the original file afterwards and your chapter is unaffected — which also means the picture
travels with your book when you export it.

You will be asked to describe the image. That description is what someone using a screen reader
hears in place of the picture, and it goes into your exported EPUB and PDF too. You can skip it, and
InkWell will not stop you — it just notes underneath the editor how many images still need one, so
you can come back to them.

## Focus mode

**Focus mode** hides everything except your text.

- Enter it with the **Focus mode** button, or press **Ctrl+Shift+F** (**Cmd+Shift+F** on a Mac).
- Leave it with **Escape**, or the **Leave focus mode** button that stays visible in the corner.

Your cursor does not move and nothing is reloaded — the interface simply gets out of the way.
Saving works exactly as it does normally.

## Accessibility mode

The **Accessibility mode** button switches to a plain text editor showing your Markdown as written:
`**bold**` stays as `**bold**`, and images appear as their reference text rather than as pictures.

It exists because it is an ordinary platform text box, so VoiceOver, TalkBack, and Narrator handle
it exactly as they handle text anywhere else on your device — reading, reviewing, and selecting all
work the way you already expect.

Nothing is lost switching either way. Both editors read and write the same chapter, so you can move
between them mid-book.

## Looking things up while writing

**Characters** and **Plot threads** open your notes beside the chapter. When you come back, your
cursor is exactly where you left it — looking something up is not an edit, so nothing is reloaded
and nothing moves.
