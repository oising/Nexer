# Reusable .NET VT library design

Status: proposed architecture and implementation plan; no implementation or
performance claims. Working name: `VtKit` (package-name availability not checked).

> **Implementation status.** This document is kept as the original design record. It is implemented in
> this repository under the name **Nexer**: `VtKit.Core` → `Nexer.Core`, `VtKit.Emulation` → `Nexer.Emulation`,
> `VtKit.Input` → `Nexer.Input`, `VtKit.Rendering` → `Nexer.Rendering` (`VtRenderer`), and `VtKit.Hosting` →
> `Nexer.Hosting` (`ConsoleTerminalHost`). Phases 2B (TUI backend) and 3 (hosting) are implemented and
covered by automated tests; Unix hosting has not yet been verified on a real Unix terminal. For the current API
> see [`docs/backend.md`](docs/backend.md) and [`COMPATIBILITY.md`](COMPATIBILITY.md). The windowing
> framework built on top of it (`Nexer.Ui`, XAML) is described in [`docs/architecture.md`](docs/architecture.md).

## 1. Decision and scope

Build two independently usable features over a small shared foundation:

1. **Terminal emulation:** consume a child application's output bytes, maintain
   terminal state, expose screen changes, and generate replies and input bytes.
2. **TUI backend:** accept application-drawn cells, render efficient VT output to
   an existing terminal, and decode that terminal's keyboard, mouse, and replies.

These are opposite sides of a terminal protocol, not interchangeable parsers.
Neither feature requires the other. Hosting, process creation, PTYs, graphics
rendering, widgets, layout, and window management stay outside the core.

Use .NET 10 as the initial target. Add other target frameworks only for a
demonstrated consumer requirement; avoid premature multi-targeting and runtime
conditionals. Keep the core usable without `Console`, native libraries, a UI
thread, dependency injection, logging providers, or background tasks.

Optimize for correctness first, then steady-state allocations, bytes emitted,
and CPU cost, in that order. Do not copy Jexer's object model or its wire output.

## 2. Evidence and changes to the rendering analysis

Primary input: `jexer-rendering-analysis.md`, especially sections 1-4, 7-8,
and 12-13. Supporting context: `jexer-windowing-analysis.md`, the repository
README, and targeted inspection of:

- `src\jexer\terminal\ECMA48.java`: emulator declaration, parser states, mode
  handling locations, and scrolling/wrapping structure.
- `src\jexer\terminal\DisplayLine.java`: cell storage and line metadata.
- `src\jexer\backend\ECMA48Terminal.java`: renderer entry points and tail-clear
  condition locations.

This is not a full emulator conformance audit. The original analysis explicitly
excluded `ECMA48.java`; it cannot establish complete emulator requirements.
Protocol behavior below is a proposed contract to verify against ECMA-48,
DEC documentation, and xterm control-sequence documentation during implementation.

Retain the analysis's separation of screen, renderer, input parser, and host.
Change these suggestions:

| Analysis suggestion | Design decision |
|---|---|
| Screen owns logical and physical grids | Logical surface owns content; each renderer owns its own presentation baseline. Multiple consumers must not share a physical grid. |
| `Flush(Screen, Stream)` is pure but does I/O | Separate synchronous byte encoding from asynchronous transport and successful-output acknowledgement. |
| `Cell.Unset` forces redraw | Keep validity/invalidation out of semantic cells. An unknown presentation baseline is not a character. |
| Shared/interned grapheme strings | Inline ordinary scalars; use a bounded, owner-scoped cluster store. Never use global `string.Intern`. |
| Scan every cell for dirtiness | Track row generations and conservative dirty intervals; compare only candidate cells. |
| Drop redraw requests inside throttle interval | Coalesce requests, retain dirty state, and schedule the next eligible frame. |
| Enable synchronized output based on terminal name | Prefer an explicit profile or positive capability reply; terminal branding is insufficient. |
| Golden bytes from Java are the main oracle | Use semantic expectations and independent emulator checks; optimized output intentionally differs. |
| One ESC timeout | Apply it to ambiguous host keyboard input only, never to application-output VT parsing. |

Respect the repository's MIT notices if source is adapted. Verify notices and
Unicode-data licensing for any shipped tables; this design does not require
copying Java implementation code.

## 3. Architecture and dependency direction

```text
VtKit.Core       <- VtKit.Emulation
    ^            <- VtKit.Rendering
    |            <- VtKit.Input
    +------------<- VtKit.Hosting

Hosting composes Rendering + Input for a terminal session.
An application composes Emulation + its own PTY/transport/view.
Emulation never depends on Rendering or Hosting.
```

| Package | Owns | Does not own |
|---|---|---|
| `VtKit.Core` | Cells, colors, Unicode policy, surfaces, damage, bounded VT scanner and writer primitives | Streams, tasks, terminal mode policy |
| `VtKit.Emulation` | Output interpretation, primary/alternate buffers, scrollback, modes, terminal replies, mode-aware user-input encoding | Native PTY, process lifecycle, GUI fonts |
| `VtKit.Rendering` | Capability projection, screen diff, output encoding, renderer baseline and prepared-frame lifetime | Application buffer mutation, raw console modes |
| `VtKit.Input` | Host key/mouse/focus/paste/reply decoding, query correlation, ESC ambiguity handling | Emulated terminal state |
| `VtKit.Hosting` | Async read/write loops, platform console acquisition/restoration, resize sources, scheduling | Widgets or application event dispatch |

Use these as logical project boundaries first. Publish separate packages when
their public contracts are stable; avoid forcing consumers to install all five.
No mandatory third-party runtime dependency in Core.

The shared scanner recognizes VT syntax. Direction-specific interpreters decide
what a sequence means. For example, `CSI ... R` may be a host cursor-position
reply or application-output command syntax in another context. Do not globally
classify every CSI as a key or every OSC as a host reply.

### Three supported data paths

```text
Headless emulator:
  application bytes -> output interpreter -> TerminalState -> view/snapshot
                                        \-> replies -> application transport
  user events -> mode-aware encoder ---------------------> application transport

TUI:
  drawing -> CellSurface -> renderer -> host output bytes
  host input bytes -> input decoder -> application events

Embedded terminal inside a TUI:
  application bytes -> emulator -> viewport composition -> outer CellSurface
  outer renderer -> host
  host input -> decoder -> focus routing -> emulator input encoder -> application
```

In the embedded case, child replies go to the child, never the outer terminal.
Child mouse/paste/application-cursor modes do not directly enable outer modes.
The host session owns and arbitrates outer-terminal modes.

## 4. Shared screen and text model

### Cells and colors

Use a value-type cell with explicit field equality:

```text
Cell = GlyphToken + Foreground + Background + StyleFlags + CellRole
Color = Default | Indexed(0..255) | Rgb(r,g,b)
CellRole = Single | WideLead | WideContinuation
```

`GlyphToken` holds an inline Unicode scalar or an owner-scoped cluster handle.
Continuation cells contain no independently printable glyph. Default color is
a distinct value, not an alias for white or black. Initial blanks use default
foreground/background; erase operations use their specified erase rendition.
The TUI can explicitly request Jexer's white-on-black appearance.

Start with direct style fields, not style interning. Measure actual struct size
with `Unsafe.SizeOf<Cell>()`; do not promise a packed size before implementation.
For illustration, a 32-byte cell at 240x80 costs about 600 KiB per grid, excluding
metadata and clusters. A large history therefore needs an explicit memory limit.

Cluster handles are meaningful only with their owning text store. A snapshot
retains that store; cross-surface copies resolve/remap handles. Public readers
resolve text through the associated view, not by inspecting a naked integer.
No managed allocation for ordinary ASCII/scalar cell writes.

### Storage

Use a row-major `Cell[]` arena plus a row-slot map. Row reads expose contiguous
spans; scrolling rotates row slots rather than copying all cells. Each row has
generation, dirty interval, soft-wrap flag, and any required line metadata.
Start with one conservative interval per row; multiple intervals are a measured
optimization, not a prerequisite.

The emulator owns separate primary and alternate buffers. Primary full-screen
scrolls can transfer rows into a bounded history ring; partial scroll-region
operations do not automatically enter history. History is capped by both lines
and estimated retained bytes, including text-store ownership. Eviction releases
rows and unreferenced clusters. Alternate-screen scrolling has no history.

TUI drawing uses a lightweight clipped/translated surface view. Translation and
clip intersection are explicit values, not mutable global drawing state. All
mutators go through checked APIs so damage tracking cannot be bypassed.
No widget tree or alpha compositor is required; those can be separate consumers.

### Wide-cell invariants

Writing, erasing, inserting, deleting, clipping, or resizing across either half
of a wide glyph repairs the whole affected glyph before applying the operation.
Never leave an orphan continuation or lead. Diff intervals expand to include
both halves in both old and new content.

Define distinct edge policies rather than letting an array check decide:

- TUI drawing: a wide glyph that cannot fit within the drawable region becomes
  a blank in the visible touched cell; it does not wrap outside the clip.
- Emulation: apply the chosen terminal profile's autowrap, pending-wrap, and
  no-wrap behavior at the right margin.

### Unicode and streaming

Pin a Unicode-data version and expose a width policy: ambiguous-width narrow
by default, with explicit overrides. Implement grapheme boundaries using a
versioned UAX #29 policy plus terminal-specific width rules. `Rune` decoding and
`StringInfo` alone do not define terminal width, emoji presentation, or host
agreement.

Handle combining marks, variation selectors, ZWJ emoji, and regional indicators.
Do not use the maximum member width as a general cluster-width algorithm.
Bound scalars/bytes per stored cluster; over-limit input follows a documented
replacement policy rather than growing indefinitely.

Keep the last printable cluster extendable across `Feed` calls. Display a
provisional glyph immediately; a following combining mark or variation selector
can revise its text/width and dirty the affected cells. Read chunk boundaries
must never change final screen state. Specify barriers such as cursor movement,
erase, reset, and control execution; test revision at the right margin and after
scrolling. Leading combining marks use a documented dotted-circle fallback.

Width mismatches between an emulator's profile and an outer host cannot be
solved by a table alone. Expose a host width profile and an explicit substitution
policy for unsupported clusters. Do not claim universal pixel/cell agreement.

## 5. Incremental protocol engine

Use a byte-fed deterministic state machine with ASCII fast paths. Maintain UTF-8
decoder state across feeds and dispatch printable scalar runs without producing
a heap token per character. Preserve CSI private prefixes, omitted parameters,
colon subparameters, intermediates, and final bytes.

Support ground, escape, CSI entry/parameter/intermediate/ignore, and bounded
OSC/DCS/SOS/PM/APC string states. Recognize cancellation and termination in their
valid contexts: CAN/SUB, ESC, ST, and OSC BEL. Unsupported strings must be
consumed without leaking payload into screen text.

Default to UTF-8 and seven-bit control introductions. Treat raw eight-bit C1
controls only in an explicitly selected legacy encoding profile, never by
mistaking UTF-8 continuation bytes for controls. Document malformed UTF-8
replacement and EOF handling. A feed boundary is not EOF or a parser reset.

Configuration has finite limits for parameters, numeric magnitude, string
payload, paste payload, cluster size, geometry, scrollback, and queued events.
Use checked arithmetic before allocation. On oversized control strings, stop
storing payload but remain in discard-until-terminator state. Merely returning
to ground would turn attacker-controlled payload into printable text.

Host input needs its own ambiguity layer: ESC can mean Escape, Alt-prefix, or a
sequence. Use `TimeProvider` and a configurable deadline, with a deterministic
test clock. Bracketed paste bypasses keyboard/ESC interpretation until its end
marker. Application-output emulation has no key timeout.

One parser instance per byte stream; no concurrent calls and no reentrant sink
callbacks. Sinks receive synchronous callbacks with explicitly borrowed data.
Consumers copy only when retaining a payload.

## 6. Emulator behavior and compatibility contract

Ship a named, versioned UTF-8/xterm-oriented profile, not a claim of complete
VT100, VT220, or xterm emulation.

| Area | Initial stable contract |
|---|---|
| Text | UTF-8, grapheme-aware cells, DEC special graphics G0/G1 |
| Controls | BEL event, BS, HT, CR, LF/VT/FF; documented LF/CR mode behavior |
| Cursor | Absolute/relative addressing, save/restore, visibility, origin mode |
| Editing | ED/EL/ECH, ICH/DCH, IL/DL, tabs, scrolling, vertical margins |
| Rendition | Default/16/256/RGB colors; bold, faint, italic, underline, blink flag, inverse, conceal, strike |
| State | Pending wrap, insert mode, autowrap, primary/alternate buffer semantics |
| Modes | Distinct 47/1047/1048/1049 behavior, DECCKM, keypad, bracketed paste, focus and SGR mouse |
| Replies | Defined DA identity, DSR/CPR, supported DECRQM; reply ordering is deterministic |
| Metadata | Bounded title and hyperlink metadata events; no automatic side effects |
| Reset | Explicit RIS/soft-reset behavior, including saved state and mode defaults |

The detailed sequence-support matrix must record defaults, set/reset behavior,
buffer scope, saved-state membership, and reply bytes for every supported mode.
Advertise only implemented capabilities; do not identify as a terminal whose
features are not present. Unknown well-formed sequences are ignored safely,
with optional rate-limited diagnostics.

Input encoding reads the emulator's modes: application cursor/keypad sequences,
mouse reporting rules, modifiers, focus, and bracketed paste. It is not the
inverse of the host input decoder and must work without Hosting.

Resize initially preserves a top-left rectangular viewport, clamps the cursor,
repairs wide cells, resets scrolling margins, and clears pending wrap. Do not
reflow text or implicitly pull history back into the viewport. Apply geometry
to both buffers; preserve existing tab stops and add standard stops in new
columns. Record this policy as deliberately simpler than some desktop terminals.
Retain soft-wrap metadata to permit an opt-in reflow implementation later.

Mode 2026 support, if advertised, batches presentation notifications rather than
stopping parsing. Cap batch duration and retained work so a missing end sequence
cannot indefinitely suppress display updates.

Excluded initially: sixel/images, downloadable fonts, full VT52/VT220 legacy
behavior, double-size lines, printer passthrough, OSC 52 clipboard access,
kitty keyboard/graphics, and text reflow. Some applications will require later
profiles; exclusions must remain visible in the support matrix.

## 7. Efficient VT renderer

### Separate desired state from terminal-observed state

Each renderer owns:

- A projected, last-successfully-written baseline and its validity.
- Assumed cursor position, pending wrap, SGR, visibility, and owned modes.
- Capability/width/palette profile versions.
- An encoded-frame buffer and the candidate baseline changes for that frame.

Project requested cells into what the host can represent *before* comparison:
RGB quantization, unsupported styles, and glyph substitutions. Otherwise a
limited-color host could receive the same impossible correction every frame.

A renderer baseline means "successfully handed to the ordered transport", not
proof that a physical display painted. External terminal writes invalidate it.
Multiple views/renderers keep independent baselines and damage cursors.

### Frame algorithm

1. Acquire a stable read lease and capture geometry/content/presentation versions.
2. Select candidate dirty rows since this renderer's acknowledged version.
3. Expand intervals for wide-cell boundaries; project cells and compare to the
   renderer baseline. A cursor-only frame does not scan the full surface.
4. Build changed runs; choose absolute moves, safe relative moves, or short
   unchanged-gap rewrites using actual encoded byte cost. Begin with absolute
   addressing; introduce alternatives only after semantic tests pass.
5. Emit SGR differences while carrying attribute state across rows. Encode
   printable content directly as UTF-8 into a reusable buffer.
6. Use EL only when its erase result exactly matches the projected tail,
   including background/default colors, relevant attributes, and BCE behavior.
   Otherwise emit explicit blanks. Never apply Jexer's exact tail threshold as
   a general correctness rule.
7. Finalize cursor state and wrap in synchronized-output mode only if supported
   and there are meaningful output bytes.
8. Release the read lease and hand the prepared frame to the single writer.
9. After the complete ordered write succeeds, acknowledge the captured versions
   and install candidate baseline/state. Mutations after capture remain dirty.

Damage is not a globally consumed flag. Start with per-row last-change
generations and per-renderer acknowledged generation; retained intervals can
conservatively widen until all active readers pass them. A lagging/new reader
falls back to whole-row or full-screen comparison rather than losing changes.
Cursor, palette, capability, and resize generations are tracked separately.

Only one prepared frame per renderer may be in flight. Its bytes and pending
baseline own their buffers until commit/discard; they must not alias an unlocked
mutable surface. The stable lease covers encoding, not asynchronous I/O.

### Correctness hazards

Track pending autowrap explicitly, especially when writing the bottom-right
cell. Never append a newline as a row separator. Establish owned terminal modes
at session entry and test right-margin output with supported host profiles.

Full repaint means an invalid baseline and explicit positioning/rendition, not
an unconditional `ED 2` that could erase outside an embedded viewport. Restrict
erase/scroll operations to the renderer's owned region. The outer renderer owns
the physical cursor; embedded views contribute a requested cursor.

On cancellation or write failure, discard the candidate baseline. A partial
write may leave the peer inside UTF-8, CSI, or a control string, so "retry the
frame" or "just repaint" is not a general recovery strategy. Mark the session
faulted and require a fresh transport/session or an explicit, profile-tested
resynchronization procedure. Best-effort cleanup is bounded, not a guarantee.

Blink animation is a presentation policy, not permanent content mutation.
Prefer host blink where appropriate; optional software blink invalidates only
affected rows. No animation means no periodic render wakeups.

## 8. Hosting, scheduling, and concurrency

Single-writer ownership is the default:

- One owner mutates each emulator/surface; cross-thread producers submit work.
- One ordered transport writer serializes frames, query traffic, and teardown.
- One read loop owns decoder state. User callbacks run outside internal locks.
- Views use short read leases; cross-thread retained views use explicit immutable
  snapshots. Do not deep-copy the screen on every feed or keystroke.

Use a bounded event channel in Hosting, not inside the synchronous core.
Backpressure must not drop keys, paste, replies, or button transitions. Coalesce
only safe motion/resize events. Bound paste size or stream paste chunks; never
buffer an unlimited paste into one string. If a consumer cannot drain safely,
surface an explicit overflow/fault rather than silently losing protocol data.

The scheduler has one dirty wakeup and one next-frame deadline. Coalesce repeated
requests, keep a pending frame during throttling, and flush the latest state at
the deadline. Use monotonic `TimeProvider` timestamps. Do not schedule continuous
frames for a static surface. Large frames use bounded staging and backpressure,
not parallel output writers.

### Platform adapters

Windows: acquire only supported console handles, capture input/output modes and
code pages, enable VT modes where applicable, and restore exactly what changed.
Do not combine a VT byte reader and native input-record reader on one handle.
A native record-based adapter is an alternative, not a concurrent size listener.

Unix: acquire raw mode through a tested termios adapter, capture/restore the
original state, and react to SIGWINCH by rereading dimensions outside the signal
callback. ABI differences belong in this adapter, not in protocol code.

Redirected streams do not imply an interactive terminal. Detect them and use an
explicit stream/headless mode; never send mode changes or queries blindly.
PTY/ConPTY process management is a separate optional adapter.

Acquisition must roll back partial setup. Normal disposal, cancellation, and
handled exceptions attempt bounded restoration. Process kill, runtime failure,
or power loss cannot guarantee restoration; document that limitation.

### Capabilities and query safety

Start from an explicit conservative profile. Probe only capabilities needed by
enabled features. Correlate replies with bounded pending queries and deadlines;
late replies must not become ordinary typed keys. Distinguish unsolicited
protocol events from matched replies. Do not infer OSC 52 support from an
unrelated DA value or grant clipboard permission because a terminal supports it.

Titles, hyperlinks, clipboard, host resize, and similar operations are policy
requests, disabled or surfaced as events by default. Sanitize/encode outbound
metadata so embedded ESC/BEL/ST cannot inject terminal commands. Untrusted child
output never passes raw through the outer TUI renderer.

## 9. Public API shape

Illustrative contracts, not final signatures:

```csharp
// Synchronous, deterministic, no streams or implicit tasks.
terminal.Feed(applicationOutputBytes, terminalEventsAndReplySink);
terminal.Resize(columns, rows);
terminal.EncodeInput(inputEvent, applicationInputWriter);
terminal.CompleteInput(terminalEventsAndReplySink);

surface.WriteText(column, row, text, style);
surface.Fill(rectangle, cell);
using var view = surface.AcquireRead();

// Preparation owns encoded bytes and candidate baseline, but does not commit.
using var frame = renderer.Prepare(view, capabilities);
await transport.WriteAsync(frame.Bytes, cancellationToken);
frame.Commit();
```

`Feed` consumes its input span before returning and retains only bounded partial
decoder state. Reply sinks use `IBufferWriter<byte>`-style byte destinations;
metadata/event sinks have explicit payload lifetimes. `CompleteInput` distinguishes
EOF from an ordinary read boundary and reports truncated input consistently.

`Prepare` is stateful, deterministic under fixed inputs, and free of I/O; it is
not mathematically pure. An empty prepared frame may still acknowledge damage
that projects to no visible change. The frame's disposal without commit
discards its candidate state. Concurrent prepare and double commit are rejected.

Keep high-level options immutable per session where possible. Expose policies
only for real compatibility differences; avoid per-cell interface dispatch.
Transport and console integration are convenience layers, not prerequisites.

## 10. Performance plan and budgets

These are hypotheses/acceptance targets to measure, not existing results:

| Workload | Expected property |
|---|---|
| Unchanged surface, no presentation changes | No encoded bytes, no full-grid scan, no steady-state allocation |
| One changed row | Work proportional to candidate interval plus bounded row bookkeeping |
| Ordinary ASCII output | No per-character allocations; incremental scan/decode |
| Full-screen scroll | Row-slot rotation plus clearing exposed rows; no full-grid move |
| Long-running session | Memory bounded by configured geometry, history, strings, clusters, queues |
| Partial updates | Fewer output bytes than a full redraw; select gap rewrites by byte cost |

Benchmark with BenchmarkDotNet and allocation diagnostics on Windows and Linux.
Record runtime, hardware, terminal profile, Unicode version, and repository
revision. Report throughput, allocated bytes, retained memory, output bytes,
and latency distributions for end-to-end runs. Set numeric regression thresholds
only after collecting repeatable baselines.

Cases: 80x24, 120x40, and 240x80; unchanged frames; cursor-only movement; typing;
sparse and dense edits; RGB/style alternation; CJK/emoji/combining text; malformed
and chunked input; scroll-heavy logs; resize storms; slow output sinks; and large
but bounded OSC/paste traffic.

Do not add SIMD, unsafe parsing, custom allocators, style interning, hashed row
equality, compression, or scroll-command detection until profiling identifies
their value. Hashes may reject equality, never prove it without collision-safe
comparison. Pool large transient buffers only with clear ownership; pooling is
not a substitute for bounded retained state.

## 11. Verification strategy

1. **Deterministic protocol tests:** table-driven sequences, omitted/colon
   parameters, cancellation, ignored strings, malformed UTF-8, EOF, every split
   point, and randomized partitions. Include fake-clock ESC ambiguity tests.
2. **Terminal-state tests:** pending wrap, margins/origin, erases with background,
   save/restore, alternate modes, reset, tabs, input-mode encoding, replies, wide
   overwrite/repair, streamed cluster extension, resize, and history eviction.
3. **Renderer semantics:** apply emitted bytes to a known independent emulator
   and compare projected visible cells, cursor, and relevant modes. Use small
   exact-byte tests for local encoding decisions, not Java's whole-frame format.
4. **Invariants and fuzzing:** no orphan wide cells, bounded memory, no crashes,
   chunk-invariant results, query/paste isolation, and defined recovery after
   limits. Run hostile OSC/DCS streams and checked-geometry cases.
5. **Transport/lifetime tests:** slow/failing/cancelled writes at byte offsets,
   no premature commit, mutation during an in-flight frame, pool lifetime,
   retained damage for multiple renderers, partial setup rollback, and disposal.
6. **Compatibility:** selected vttest cases and representative shell/editor/TUI
   transcripts; Windows Terminal and representative Unix terminals for the
   backend. Keep a versioned pass/fail support matrix, not a blanket claim.

Use both hand-derived expectations and an independent implementation. Testing
only our renderer against our emulator can hide matching bugs. Java output is
a useful differential input for its supported subset, not the authority for
Unicode, erase semantics, parser defects, or optimization.

Normal CI runs unit/property tests and build/analyzers. Keep benchmarks and
interactive terminal compatibility runs separate from deterministic unit tests.
Document unsupported cases rather than silently adjusting expected results.

## 12. Phased implementation and exit criteria

| Phase | Deliverable | Exit criterion |
|---|---|---|
| 0. Contracts | Sequence/mode matrix, ownership/lifetime rules, width/resize/limit policies, project skeleton | Both data paths have runnable design-level usage samples and unambiguous supported behavior |
| 1. Foundation | Cell/color/surface model, damage, Unicode tables, bounded scanner/writer | Chunk/invariant tests pass; baseline allocation/memory measurements recorded |
| 2A. Emulator | Primary/alternate state, text/cursor/editing/scrolling, replies, input encoder | Headless transcript runner passes the initial profile matrix |
| 2B. TUI backend | Diff renderer, host input decoder, transactional frames | Independent semantic rendering tests and failure-injection tests pass |
| 3. Hosting | Windows/Unix adapters, async session, scheduler, resize/capability handling | Sample TUI works on both platforms; lifecycle and redirection cases pass |
| 4. Composition | Embedded-terminal sample using an externally supplied process transport | Child/host mode isolation, reply routing, focus, cursor, and viewport behavior pass |
| 5. Optimization/release | Profile-guided improvements, package docs, compatibility report | Measured regressions bounded; no unbounded retained resource; public contracts reviewed |

Phases 2A and 2B are independent after phase 1; neither should wait for a PTY or
GUI implementation. Hosting depends on 2B. Composition depends on 2A and 3.
Constrain the first release to the documented text-terminal profile rather than
porting every Jexer feature.

## 13. Main risks and decisions reserved for measurement

**Unicode compatibility:** cluster segmentation and host width disagree in real
terminals. Version/policy injection and targeted compatibility cases mitigate
this; they do not eliminate it.

**Retention and copying:** compact scalar cells help only if history and cluster
stores release data. Exercise bounded-memory soak tests before optimization.
Begin with direct styles and row slots; benchmark before changing layout.

**Output state corruption:** a successful diff algorithm cannot repair arbitrary
partial transport writes. Keep commit boundaries, exclusive writers, and faulted
session behavior part of the public contract.

**Compatibility expansion:** arbitrary xterm branding invites unsupported queries
and extensions. Keep identities honest and additions gated by support-matrix
entries and tests.

**First implementation priority:** establish cell/Unicode/parser invariants and
frame ownership before native hosting or micro-optimization. Those contracts
are the reusable library; console convenience code is an adapter.
