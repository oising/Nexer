# Nexer backend: Rendering, Input, Hosting

This document covers the three backend projects (`Nexer.Rendering`, `Nexer.Input`, `Nexer.Hosting`)
and the `samples/Nexer.Backend.Sample` app. The authoritative design is `dotnet-vt-library-design.md`
(phases 2B and 3). The contracts (`InputEvents.cs`, `ITerminalHost.cs`) are frozen.

## Quick start (what the UI layer uses)

```csharp
await using ConsoleTerminalHost host = await ConsoleTerminalHost.StartAsync(
    new ConsoleTerminalHostOptions { MouseMotion = true }, cancellationToken);

var surface = new CellSurface(host.Size.Columns, host.Size.Rows);
// ... draw into surface on the UI thread ...
host.Present(surface, new CursorState(Visible: true, Column: 3, Row: 1));

await foreach (InputEvent e in host.Input.ReadAllAsync(cancellationToken))
{
    if (e is ResizeEvent r) { surface = new CellSurface(r.Columns, r.Rows); }
    // dispatch, redraw, Present ...
}
```

`StartAsync` throws `InvalidOperationException` when stdin or stdout is redirected (there is no
console to drive) and `PlatformNotSupportedException` when the Windows console cannot enable VT
processing. If startup fails part-way, the terminal is restored before the exception propagates.

## Public API overview

### Nexer.Rendering
- `VtRenderer(int columns, int rows, RendererOptions? options = null)` — diff renderer from an
  `IReadOnlyCellSurface` to VT bytes.
  - `RenderFrame Prepare(IReadOnlyCellSurface surface, RenderCursor cursor)` — computes bytes against
    the renderer's baseline without changing it.
  - `RenderFrame.Bytes` / `IsEmpty` / `Commit()` / `Dispose()` — write `Bytes`, then `Commit()` to
    adopt the frame as the new baseline. Disposing without committing (e.g. write failed) leaves the
    baseline untouched; `Invalidate()` afterwards forces a full repaint if the terminal state is unknown.
  - `Resize(columns, rows)`, `Invalidate()`, `HasFrameInFlight`.
- `RendererOptions` — `SynchronizedOutput` (DEC 2026 wrapping), `Colors` (`ColorMode.TrueColor`,
  `Indexed256`, `Indexed16`; colors are projected down), `AmbiguousIsWide`.
- `RenderCursor(bool Visible, int Column, int Row)`, `RenderCursor.Hidden`.
- `ColorProjection`, `SgrEncoder` — public helpers (color down-projection, minimal SGR diffs).

### Nexer.Input
- `InputDecoder(Action<InputEvent> onEvent, InputDecoderOptions? options = null, TimeProvider? time = null)`
  - `Feed(ReadOnlySpan<byte>)`, `ResolveTimeout()`, `Complete()` (EOF),
    `HasPendingSequence`, `TimeUntilTimeout`.
- `InputDecoderOptions` — `EscapeTimeout` (50 ms), `MaxPasteChunkBytes` (256 KiB),
  `ControlHIsControlBackspace` (true).

### Nexer.Hosting
- `ConsoleTerminalHost : ITerminalHost` — `StartAsync(options, ct)`, `Size`, `Input`, `Time`,
  `Present(surface, cursor)`, `DisposeAsync()`.
- `ConsoleTerminalHostOptions` (record) — `TimeProvider`, `AlternateScreen` (true), `Mouse` (true,
  1000/1002/1006), `MouseMotion` (false, 1003), `FocusEvents` (true, 1004), `BracketedPaste` (true,
  2004), `SynchronizedOutput` (true), `Colors` (TrueColor), `MinFrameInterval` (16 ms),
  `InputCapacity` (1024), `Input` (decoder options).

## Threading

- **Present** is called on the UI thread. It never performs I/O: under a lock it copies the changed
  rows of the surface (by row version; cluster text is copied by value) into a host-owned snapshot,
  records the cursor, marks the host dirty and wakes the frame loop. The caller may mutate the
  surface as soon as `Present` returns. Presenting a surface whose size differs from the terminal is
  allowed; it is clipped/padded.
- **Frame loop** (thread pool task): waits for a wake, enforces `MinFrameInterval` since the previous
  frame (using the configured `TimeProvider`), then `Prepare`s from the snapshot under the snapshot
  lock, and writes + `Commit`s outside it. Several `Present` calls inside one interval are coalesced
  into one frame containing only the net change. It is the single writer to the terminal.
- **Read loop** (long-running task): blocks in the platform read with a timeout of
  `min(100 ms, decoder.TimeUntilTimeout)`, feeds the decoder, resolves lone-ESC timeouts, polls the
  terminal size (also triggered by SIGWINCH on Unix) and posts `ResizeEvent`s. Events go into a
  bounded channel; when it is full the reader waits (backpressure; nothing is dropped).
- A frame write failure completes `Input` with an `IOException`; EOF on stdin completes it normally.

## Terminal state and restore

Enter: platform raw mode (Windows: VT input/output modes, quick-edit off, UTF-8 code pages;
Unix: `cfmakeraw`), then `ESC[?1049h ESC[?25l` + mouse/focus/paste modes per options.
Restore runs exactly once on `DisposeAsync`, `ProcessExit`, unhandled exceptions and
SIGINT/SIGTERM/SIGHUP/SIGQUIT (`PosixSignalRegistration`; the default action still runs). It writes
the inverse sequence (`?2026l ?2004l ?1004l ?1006l ?1003l ?1002l ?1000l`, `ESC[m`, `?25h`, `?1049l`)
and then restores the exact original console modes / code pages / termios bytes.

## Decisions

- **Lone ESC** is resolved by a timeout (50 ms after the last byte) using `TimeProvider`; an
  abandoned incomplete sequence becomes Alt+<next byte> and remaining bytes are replayed.
  `ESC ESC <key>` yields Alt+key for CSI/SS3 keys, otherwise Escape followed by Alt+char.
- **CPR vs F3**: `CSI 1;m R` with m>1 is Shift/Alt/Ctrl+F3; every other `CSI … R` is a cursor
  position report and swallowed. The host therefore never sends DSR queries. DA/DSR/DECRQM/OSC/DCS
  replies are swallowed.
- **Control bytes**: 0x0d → Enter, 0x0a → Ctrl+J, 0x09 → Tab, 0x7f → Backspace, 0x08 → Ctrl+Backspace
  (configurable to Ctrl+H), 0x00 → Ctrl+Space, 0x1c–0x1f → Ctrl+`\ ] ^ _`.
- **Paste** is never surfaced as keys. Very large pastes are delivered as several consecutive
  `PasteEvent`s of at most `MaxPasteChunkBytes` each (split on UTF-8 boundaries).
- **Mouse**: SGR 1006, legacy X10 and urxvt 1015 are decoded; coordinates are zero-based. Horizontal
  wheel and extended buttons 8–11 are dropped because the contract has no kinds for them.
- **Windows input** uses `ReadConsoleInputW` (not `ReadFile`) so the wait can time out for the ESC
  resolver; key-down characters (honoring repeat counts) are re-encoded to UTF-8 and fed to the
  same decoder as Unix. Size comes from the window rectangle (`srWindow`), polled on every read tick.
- **Unix size** uses `ioctl(TIOCGWINSZ)` only on Linux (ioctl is variadic, unsafe to P/Invoke on
  Apple arm64); elsewhere `Console.WindowWidth/Height` is used.
- **Renderer**: damage detection by row version + surface identity; erase-to-end-of-line only when it
  produces exactly the same cells (blank tail in the current background); cheapest of CUP/CHA/CUF/literal
  re-emission for cursor moves; per-frame SGR state carried across rows.

## Known limitations

- The Unix platform layer (termios, poll, SIGWINCH) compiles but has not been exercised on a real
  Unix terminal in this environment.
- No capability probing (DA/XTVERSION/DECRQM); options decide which modes are enabled.
  Synchronized output is harmless on terminals that ignore it.
- Mouse motion without buttons (1003) is off by default.
- `PasteEvent` chunking, horizontal wheel, and Super/Meta modifiers are limited by the contract.
- The sample cannot be driven interactively in CI; `--smoke` renders one frame and exits.

## Sample

`dotnet run --project samples/Nexer.Backend.Sample` draws a box and echoes decoded key, mouse,
resize, focus and paste events; the mouse position is marked. Ctrl+Q quits. `--smoke` renders a
single frame and exits with code 0 (exit code 2 when no interactive console is available).
