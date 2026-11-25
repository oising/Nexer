# Nexer initial headless profile

This is a deliberately limited UTF-8/seven-bit text terminal, not a complete
xterm/VT220 implementation. No DA identity or reply-producing feature is
advertised yet. Unsupported well-formed commands and all OSC/DCS/SOS/PM/APC
payloads are consumed without side effects. Dimensions are fixed at construction.

## Text and storage

Unicode **17.0.0**, UAX #29 extended grapheme boundaries, generated from pinned,
SHA-256-verified Unicode data. The segmenter is checked against the official
GraphemeBreakTest fixture. Terminal width is a separate policy: East Asian W/F
are wide, A is narrow unless `AmbiguousIsWide` is enabled; emoji presentation,
emoji variation/ZWJ clusters, flags, and keycaps are wide. VS15 suppresses emoji
promotion but not inherently wide East Asian characters. This is a declared
policy, not a promise of agreement with every terminal's font/width handling.

The latest printable cluster remains extendable across `Feed` calls and is
displayed immediately. Controls, ESC introductions, cancellation, DEL, complete
strings, and EOF are text barriers. Even ignored/over-limit strings cannot join
surrounding text into one cluster. Feed boundaries are never barriers.

Combining-only clusters display with U+25CC dotted circle. Clusters are limited
to 64 scalars including any synthetic dotted circle; excess replaces the whole
cluster with U+FFFD and discards extensions until the next grapheme boundary.
Segmentation still proceeds with constant state while discarding extensions.
Exhausting the live cluster UTF-16-byte budget substitutes U+FFFD while preserving
the requested cell footprint. Ordinary scalars do not require cluster storage.
Unique cluster strings are reference-counted, deduplicated per surface, and
released when erased/overwritten/scrolled out. The byte budget counts UTF-16
payload, not CLR object overhead; metadata is bounded by the high-water number
of live cluster entries. Handles and row spans are borrowed until mutation.
Resolve text through `Screen.GetText(column, row)`; `Cell.Glyph` is only the first
scalar. A continuation resolves to the empty string.

A wide glyph occupies a lead and continuation. Overwriting/erasing either half
repairs the other with a default-style blank, even just outside the requested
erase interval. A TUI surface write never wraps; a clipped wide write becomes a
styled blank. Scrolling rotates complete row slots, preserves paired cells, and
uses the current erase rendition for newly exposed rows.

## State and control matrix

Coordinates below are wire-level one-based values; public cursor properties
are zero-based. Omitted/zero movement counts default to 1.

| Commands | Behavior/defaults |
|---|---|
| BS / HT / CR | Move left / next tab stop or last column / column zero; cancel pending wrap |
| LF / VT / FF | Index one row without CR; scroll at bottom margin; cancel pending wrap |
| BEL | Synchronous `Bell` event; no screen change |
| Other C0, DEL, decoded C1 | No display effect; break printable-cluster continuity |
| `ESC D`, `ESC E`, `ESC M` | Index, CR+index, reverse index; scroll within vertical margins |
| `ESC H`, `CSI 0 g`, `CSI 3 g` | Set current tab stop, clear current, clear all |
| `CSI Ps A/B/C/D`, `a/e` | Relative movement, clamped to screen/margins |
| `CSI Ps E/F/G/d`, `` ` `` | Next/previous line, absolute column/row; clamp |
| `CSI row;column H/f` | Absolute cursor; row relative to top margin in origin mode |
| `CSI Ps J/K` | ED/EL modes 0 (cursor onward), 1 (through cursor), 2 (all); cursor unchanged |
| `CSI Ps X` | Erase cells, clamped to current row; cursor unchanged |
| `CSI Ps S/T` | Scroll vertical region up/down, count clamped to region height |
| `CSI top;bottom r` | Set vertical margins and home cursor; defaults 1/height; require top < bottom |
| `CSI ?6 h/l` | Origin mode on/off; home cursor; default off |
| `CSI ?7 h/l` | Autowrap on/off; cancel pending wrap; default on |
| `CSI ?25 h/l` | Cursor visibility; default on |
| `ESC 7/8`, `CSI s/u` | Save/restore cursor, rendition, origin/autowrap modes; restoration clears pending wrap |
| `ESC c` | RIS: clear display with defaults, reset cursor/margins/tabs/rendition/modes/saved state |

Tabs start every 8 columns (zero-based 8, 16, ...). Saved state initially equals
home, default rendition, origin off, autowrap on. Relative upward motion clamps
at the top margin when starting at/below it, otherwise at the screen top.
Downward motion clamps at the bottom margin when starting at/above it,
otherwise at the screen bottom.
Erase uses current foreground/background with all style flags cleared. ED/EL,
ECH, explicit scroll, movement, and valid margin changes cancel pending wrap.
SGR, BEL, unknown sequences, save-cursor, and visibility changes do not.

Pending wrap is set after writing the last occupied column, and wrapping happens
only when the next printable cluster begins. No newline is inserted into text.
A wide glyph at the last column wraps before being drawn when autowrap is on
and there are at least two columns; otherwise it becomes a styled blank. With
autowrap off, repeated text overwrites the last column. A one-column surface
cannot display a wide glyph and uses a blank without a pre-wrap loop.

Streaming width revisions stay anchored to the initially displayed location.
Growth from narrow to wide at the last column blanks the provisional cell and
wraps before redrawing (including scrolling at the bottom margin). Shrinkage
does not undo a wrap/scroll already performed for a provisional glyph. These
rules are identical for single-feed and arbitrarily partitioned input.

## SGR

Supported flags: 1 bold, 2 faint, 3 italic, 4 underline, 5 blink, 7 inverse,
8 conceal, 9 strike. Resets: 0 all, 22 bold+faint, 23/24/25/27/28/29 corresponding
flags. Blink is stored, not animated. Colors: 30-37/40-47, 90-97/100-107,
39/49 defaults, `38/48;5;index`, `38/48;2;r;g;b`, `38/48:5:index`,
`38/48:2:r:g:b`, and `38/48:2::r:g:b` (also color space 0).
`4:0`/`4:1` clear/set underline; other substyles are ignored.

Missing/zero SGR parameters reset rendition. RGB/index components must be in
0..255. Unsupported codes/groups are ignored. Invalid extended semicolon color
groups stop the remainder of that SGR (earlier valid changes remain); invalid
colon groups are ignored as a unit. Numeric parse failure/parameter overflow
rejects the entire command without partial changes.

## Resource and lifecycle limits

Default geometry limit: 1,000,000 cells. Default live cluster payload: 1 MiB.
Default control-string payload: 4096 bytes; raw header: 128 bytes.
Interpreted parameters: at most 32 fields, each at most 1,000,000.
Allocation checks precede geometry multiplication. Surface damage is monotonic
whole-row generations; consumers keep independent acknowledged generations and
must establish an initial baseline. Cursor/mode properties must also be compared
by consumers; a cursor-only command does not dirty otherwise unchanged cells.

`Feed` is synchronous and retains no input buffer. Scanner callbacks use borrowed
spans. `CompleteInput` replaces incomplete UTF-8, discards partial sequences, and
seals the last cluster; it does not reset screen/modes. A later feed begins new
text continuity. Instances require exclusive single-owner access, including
callbacks; do not mutate a screen obtained by downcasting its read-only view.
Synchronous sink/user callback failures fault that input stream; discard it.

No resize/reflow, history, alternate buffers, DEC special graphics, insert/delete
editing, device reports, input encoding, native hosting, or output renderer yet.
