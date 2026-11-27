# Nexer VT layers: Core, Input, Rendering, Hosting, Emulation

Underneath the widget toolkit sit reusable, dependency-free VT layers. Each ships as its own NuGet package and can be
used without `Nexer.Ui`:

| Package | Use it for |
|---------|------------|
| `Nexer.Core` | Cells, colours, styles, `CellSurface`, Unicode width, `VtScanner`. |
| `Nexer.Input` | Decoding VT input. |
| `Nexer.Rendering` | Rendering cell surfaces to VT output. |
| `Nexer.Hosting` | Running in a real console (`ConsoleTerminalHost`). |
| `Nexer.Emulation` | The headless terminal emulator. |

See [backend.md](backend.md) for Input, Rendering and Hosting, `samples\Nexer.Backend.Sample` for a program that uses
them without the toolkit, and [COMPATIBILITY.md](../COMPATIBILITY.md) for precise emulator support.

## Headless emulator usage

`Nexer.Emulation` can be used on its own to interpret application output:

```csharp
using Nexer.Emulation;

var terminal = new Terminal(80, 24);
terminal.Feed("hello \u001b[32mworld\u001b[0m\r\n"u8);
terminal.CompleteInput(); // Only at EOF, not after each read.
string text = terminal.Screen.GetText(0, 1);
```

It is not a complete xterm implementation; see [COMPATIBILITY.md](../COMPATIBILITY.md) for precise support.

## Core and emulation contracts

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
