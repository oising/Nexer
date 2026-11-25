# Nexer

A reusable .NET 10 VT library with an initial headless text emulator. The architecture
is described in `..\dotnet-vt-library-design.md`; Nexer is the implementation name
for that document's working name, VtKit.

## Available now

- `Nexer.Core`: value-type cells, distinct default/indexed/RGB colors, style
  flags, and a bounded row-major `CellSurface`.
- Scalar/cluster writes, paired wide-cell repair, right-edge clipping, styled
  erasure, region scrolling through row slots, and independent row generations.
- Unicode 17 extended grapheme segmentation, explicit terminal width policy,
  bounded reference-counted cluster storage, and streaming cluster revisions.
- A byte-fed, bounded UTF-8/seven-bit `VtScanner` with synchronous `IVtSink`
  callbacks for scalars, C0 execution, ESC/CSI sequences, and opaque strings.
- `Nexer.Emulation`: headless UTF-8 output interpretation with cursor movement,
  SGR colors/styles, erasing, scrolling regions, tabs, origin/autowrap modes,
  save/restore, and reset.
- Official Unicode grapheme conformance tests and focused protocol/surface/
  emulator tests, including chunked transcripts, right-margin revisions,
  malformed input, bounded memory, damage, and allocation-free scalar hot paths.

See [COMPATIBILITY.md](COMPATIBILITY.md) for precise support and edge-case rules.
This is **not a complete xterm implementation or console backend**. Alternate
buffers, history, resize, DEC graphics, insert/delete editing, replies,
input encoding/decoding, clipped drawing views, rendering, and native hosting
remain unimplemented.

## Headless usage

Reference `src\Nexer.Emulation\Nexer.Emulation.csproj` from a .NET 10 application:

```csharp
using Nexer.Emulation;

var terminal = new Terminal(80, 24);
terminal.Feed("hello \u001b[32mworld\u001b[0m\r\n"u8);
terminal.Feed("e"u8);
terminal.Feed("\u0301"u8); // Extends the visible e, even across reads.
terminal.CompleteInput(); // Only at EOF, not after each read.

string grapheme = terminal.Screen.GetText(0, 1); // e + combining acute
int row = terminal.CursorRow;
```

Feed bytes from any application-output transport; the library itself starts no
processes and opens no terminal handles. The read-only screen view exposes
cells, resolved cluster text, and damage generations for a consumer's renderer.

## Build and test

Run from this directory using the .NET 10 SDK:

```powershell
dotnet build .\Nexer.slnx
dotnet test .\Nexer.slnx
dotnet format .\Nexer.slnx --verify-no-changes
```

The libraries have no third-party runtime dependencies. Tests use xUnit.
Generated Unicode data and the official fixture ship with the Unicode license
and attribution; see `THIRD-PARTY-NOTICES.txt`. Regeneration requires Python and
network access (`python .\tools\generate_unicode.py`), not normal builds/tests.

## Contracts for this increment

`CellSurface`, `VtScanner`, and `Terminal` require a single owner; they are not thread-safe.
Row spans must not be retained across mutations. Surface versions are monotonic,
and row generations are never consumed/reset by readers. A new renderer must
establish its own baseline even when every row is still at generation zero.
This increment tracks whole dirty rows, not minimal intervals.

`Write` accepts one non-control scalar; `WriteCluster` accepts one presegmented
cluster. Both take width 1 or 2. Low-level surface callers supply width;
`Terminal` segments/measures automatically. At the right edge, a width-2
write becomes a styled blank. Replacing either half of a wide glyph blanks its
partner with default style. Geometry is validated against a configurable cell
limit before allocation. Cells default to a space with default terminal colors.

The scanner does not interpret parameters numerically or execute commands.
CSI parameter bytes retain private prefixes, omitted values, and colon syntax.
DCS callbacks contain the entire body after `ESC P`, including its raw header.
String payloads are bytes, not automatically decoded text.

Callback spans are borrowed until that callback returns. Callbacks must not
reenter the scanner; exceptions from a sink should be treated as a failed stream
and that scanner discarded. There are no I/O operations, timers, or background
tasks. `Complete` is explicit EOF, not an ordinary read boundary, and resets
partial sequence state so the instance can then process a fresh stream.

OSC ends at BEL or ST; DCS/SOS/PM/APC end at ST. CAN/SUB cancel sequences and are
consumed rather than emitted as input keys. DEL is ignored outside strings.
ESC followed by a non-backslash byte inside a string remains opaque payload;
only CAN/SUB or a proper terminator ends it. This conservative policy prevents
unsupported or overflowing strings from leaking embedded commands into text.
Over-limit strings are discarded in full, while over-limit/malformed sequence
headers are ignored through the next final byte. Raw eight-bit C1 bytes are not
control introductions in this UTF-8 profile.

The scanner bounds raw header bytes rather than allocating numeric parameter
objects. The emulator additionally bounds numeric values and
parameter counts. Invalid UTF-8 uses replacement characters; incomplete UTF-8 is
preserved across feeds and replaced at a control boundary or EOF.
