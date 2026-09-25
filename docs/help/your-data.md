# Your data and your privacy

Open **Your data** from the library.

## Where your writing lives

In one file on this device, encrypted.

Everything is in it: manuscripts, chapters, the images you embedded, your character and plot-thread
notes, your goals, and your writing history. The **Your data** screen names the file and its size,
so the claim is one you can check rather than one you have to take on trust.

The key that decrypts it is held in your device's own secure storage — Keychain on Apple devices,
the Keystore on Android, DPAPI on Windows. It is generated on this device, on first run, and it
never leaves it.

## What InkWell sends

Nothing.

There is no account, no sync, and no analytics. InkWell does not request network permission on any
platform — not "we have it and don't use it"; the permission is absent from the app, and a test
fails the build if anyone adds it back. The app works identically with your device in aeroplane
mode, because as far as it is concerned there is no difference.

The one way anything gets out is [Export](./export.md), which you trigger and which writes only
where you point it.

## Seeing what is stored

**Your data** lists every manuscript with what it holds: chapters, words, images, characters, plot
threads, and days of writing history. Nothing the app stores is left off that list.

## Deleting one manuscript

**Delete** beside a manuscript removes it and everything it owns. You are told exactly what will go
before it happens, and nothing else is touched.

## Deleting everything

**Delete everything** erases every manuscript on this device and removes the encryption key.

This is genuinely final. It is not a tidy-up that leaves recoverable pieces behind: the database is
rewritten so deleted pages are actually gone rather than merely unlinked, and without the key any
bytes that survive anywhere — an old backup, a filesystem that has not reused the blocks — are
unreadable ciphertext. Nobody can undo it, including InkWell.

**Export anything you want to keep first.** Exported files are ordinary files and are unaffected.

## If InkWell cannot open your writing

Two messages mean different things:

**"InkWell cannot unlock your writing"** — the app could not reach your device's secure storage.
Your manuscripts are intact and still on the device; the app just cannot get at the key right now.
This is usually an installation problem. Restart the app; if it persists, reinstall InkWell from the
same source.

**"InkWell could not read your writing"** — the file itself could not be read. Nothing has been
deleted. The usual causes are a full disk or another program holding the file open.

Neither message means your work is gone. InkWell says so explicitly in both, because that is the
only thing anyone actually wants to know at that moment.

## Moving to a new device

There is no sync, and the encryption key does not travel — a restored backup on a new device is a
file whose key stayed behind. Export your manuscripts and carry the exported files across.
