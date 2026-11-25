using System.Text;
using Nexer.Core;
using Nexer.Core.Text;

namespace Nexer.Rendering;

/// <summary>
/// Diffing VT renderer: turns an <see cref="IReadOnlyCellSurface"/> into the bytes that update a terminal from
/// this renderer's baseline (the last committed frame). Single owner; not thread-safe.
/// </summary>
/// <remarks>
/// The renderer assumes it exclusively owns the screen region (0,0)-(Columns,Rows) of a terminal with autowrap on,
/// no scrolling margins and origin mode off. External writes to the terminal require <see cref="Invalidate"/>.
/// After a failed or partial write the terminal state is unknown: call <see cref="Invalidate"/> (or use a new
/// session) before preparing the next frame.
/// </remarks>
public sealed class VtRenderer
{
    // Room for "\e[?2026h" + "\e[?25l" written in front of the frame body.
    private const int PrefixReserve = 16;
    private const int MaxRunes = CellSurface.MaxClusterScalars + 1;

    private readonly RendererOptions options;
    private readonly OutputBuffer output = new();
    private readonly Rune[] runeScratch = new Rune[MaxRunes];
    private RenderedCell[] baseline = [];
    private RenderedCell[] desired = [];
    private bool[] changed = [];
    private ulong[] ackRowVersions = [];
    private ulong[] candidateRowVersions = [];
    private PendingCell[] pending = new PendingCell[256];
    private int pendingCount;
    private bool baselineValid;
    private IReadOnlyCellSurface? ackSurface;
    private IReadOnlyCellSurface? candidateSurface;
    private int ackSurfaceColumns, ackSurfaceRows;
    private TerminalState state = TerminalState.Unknown;
    private TerminalState work;
    private long token;
    private long generation;
    private long frameGeneration;
    private bool inFlight;
    private int frameStart;

    // Grapheme-join guard: consecutive glyphs that would merge into one cluster are separated by a CHA.
    private bool joinBarrier;
    private bool segmenterValid;
    private GraphemeSegmenter segmenter;
    private Rune lastRune;

    /// <summary>Creates a renderer for a terminal of the given size. The first frame is a full repaint.</summary>
    public VtRenderer(int columns, int rows, RendererOptions? options = null)
    {
        this.options = options ?? new RendererOptions();
        Resize(columns, rows);
    }

    /// <summary>Terminal width in cells.</summary>
    public int Columns { get; private set; }

    /// <summary>Terminal height in cells.</summary>
    public int Rows { get; private set; }

    /// <summary>The options this renderer was created with.</summary>
    public RendererOptions Options => options;

    /// <summary>True between <see cref="Prepare"/> and the frame's commit or disposal.</summary>
    public bool HasFrameInFlight => inFlight;

    /// <summary>Changes the terminal size and forces a full repaint. An in-flight frame becomes stale (its commit is ignored).</summary>
    public void Resize(int columns, int rows)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        if ((long)columns * rows > 4_000_000) throw new ArgumentOutOfRangeException(nameof(columns), "Terminal too large.");
        Columns = columns;
        Rows = rows;
        baseline = new RenderedCell[columns * rows];
        desired = new RenderedCell[columns];
        changed = new bool[columns + 1];
        ackRowVersions = new ulong[rows];
        candidateRowVersions = new ulong[rows];
        Invalidate();
    }

    /// <summary>
    /// Forgets everything known about the terminal (content, rendition, cursor). The next frame repaints every
    /// cell with explicit positioning and rendition. An in-flight frame becomes stale.
    /// </summary>
    public void Invalidate()
    {
        baselineValid = false;
        ackSurface = null;
        state = TerminalState.Unknown;
        generation++;
    }

    /// <summary>
    /// Encodes the changes needed to display <paramref name="surface"/> and <paramref name="cursor"/>. No I/O is
    /// performed and the baseline is unchanged until <see cref="RenderFrame.Commit"/>. A surface whose size differs
    /// from the renderer is clipped or padded with default blanks.
    /// </summary>
    /// <exception cref="InvalidOperationException">Another frame is still in flight.</exception>
    public RenderFrame Prepare(IReadOnlyCellSurface surface, RenderCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (inFlight) throw new InvalidOperationException("A frame is already in flight; commit or dispose it first.");
        work = state;
        pendingCount = 0;
        output.Reset(PrefixReserve);
        joinBarrier = true;
        Array.Copy(ackRowVersions, candidateRowVersions, Rows);
        bool full = !baselineValid;
        bool compareAll = full || !ReferenceEquals(surface, ackSurface) ||
            surface.Columns != ackSurfaceColumns || surface.Rows != ackSurfaceRows;
        for (int row = 0; row < Rows; row++)
        {
            ulong version = row < surface.Rows ? surface.GetRowVersion(row) : 0;
            if (!compareAll && version == ackRowVersions[row]) continue;
            candidateRowVersions[row] = version;
            RenderRow(surface, row, full);
        }

        bool content = output.Length > PrefixReserve;
        bool synchronized = content && options.SynchronizedOutput;
        int prefixLength = 0;
        Span<byte> prefix = stackalloc byte[PrefixReserve];
        if (synchronized)
        {
            "\e[?2026h"u8.CopyTo(prefix);
            prefixLength = 8;
        }
        else if (content && work.CursorVisible != 0)
        {
            // Hide the cursor while painting to avoid flicker; it is restored below if requested.
            "\e[?25l"u8.CopyTo(prefix);
            prefixLength = 6;
            work.CursorVisible = 0;
        }

        if (cursor.Visible)
        {
            MoveTo(Math.Clamp(cursor.Column, 0, Columns - 1), Math.Clamp(cursor.Row, 0, Rows - 1));
            if (work.CursorVisible != 1)
            {
                output.Append("\e[?25h"u8);
                work.CursorVisible = 1;
            }
        }
        else if (work.CursorVisible != 0)
        {
            output.Append("\e[?25l"u8);
            work.CursorVisible = 0;
        }

        if (synchronized) output.Append("\e[?2026l"u8);
        frameStart = PrefixReserve - prefixLength;
        output.WriteAt(frameStart, prefix[..prefixLength]);
        candidateSurface = surface;
        frameGeneration = generation;
        inFlight = true;
        return new RenderFrame(this, ++token);
    }

    internal ReadOnlyMemory<byte> GetFrameBytes(long frameToken)
    {
        if (!inFlight || frameToken != token) throw new ObjectDisposedException(nameof(RenderFrame));
        return output.Slice(frameStart);
    }

    internal void Commit(long frameToken)
    {
        if (!inFlight || frameToken != token) throw new InvalidOperationException("The frame was already committed or discarded.");
        inFlight = false;
        IReadOnlyCellSurface? surface = candidateSurface;
        candidateSurface = null;
        if (frameGeneration != generation) return; // Invalidated or resized while in flight: stay invalid.
        for (int i = 0; i < pendingCount; i++) baseline[pending[i].Index] = pending[i].Cell;
        (ackRowVersions, candidateRowVersions) = (candidateRowVersions, ackRowVersions);
        ackSurface = surface;
        ackSurfaceColumns = surface!.Columns;
        ackSurfaceRows = surface.Rows;
        baselineValid = true;
        state = work;
    }

    internal void Discard(long frameToken)
    {
        if (!inFlight || frameToken != token) return;
        inFlight = false;
        candidateSurface = null;
    }

    private void RenderRow(IReadOnlyCellSurface surface, int row, bool full)
    {
        int columns = Columns;
        ReadOnlySpan<Cell> source = row < surface.Rows ? surface.GetRow(row) : default;
        int offset = row * columns;
        bool any = false;
        for (int column = 0; column < columns; column++)
        {
            desired[column] = Resolve(surface, source, column, row);
            bool differs = full || desired[column] != baseline[offset + column];
            changed[column] = differs;
            any |= differs;
        }

        if (!any) return;
        if (!full) ExpandWideCells(offset);

        // Trailing run of identical erasable blanks: candidates for EL.
        int tailStart = columns;
        RenderedCell last = desired[columns - 1];
        if (IsErasable(last))
        {
            tailStart = columns - 1;
            while (tailStart > 0 && desired[tailStart - 1] == last) tailStart--;
        }

        int columnIndex = 0;
        while (columnIndex < columns)
        {
            if (!changed[columnIndex])
            {
                columnIndex++;
                continue;
            }

            int start = columnIndex, end = columnIndex;
            while (true)
            {
                while (end + 1 < columns && changed[end + 1]) end++;
                int next = end + 1;
                while (next < columns && !changed[next]) next++;
                if (next >= columns || !GapIsCheaper(end, next)) break;
                end = next;
            }

            if (end >= tailStart)
            {
                int eraseFrom = Math.Max(start, tailStart);
                if (columns - eraseFrom > 3)
                {
                    for (int column = start; column < eraseFrom;) column = EmitCell(column, row, offset);
                    MoveTo(eraseFrom, row);
                    SetStyle(last.Style);
                    output.Append("\e[K"u8);
                    work.WrapPending = false;
                    for (int column = eraseFrom; column < columns; column++) AddPending(offset + column, last);
                    return;
                }
            }

            for (int column = start; column <= end;) column = EmitCell(column, row, offset);
            columnIndex = end + 1;
        }
    }

    private void ExpandWideCells(int offset)
    {
        int columns = Columns;
        for (int column = columns - 1; column > 0; column--)
        {
            if (changed[column] && (desired[column].Role == CellRole.WideContinuation ||
                baseline[offset + column].Role == CellRole.WideContinuation))
            {
                changed[column - 1] = true;
            }
        }

        for (int column = 0; column < columns - 1; column++)
        {
            if (changed[column] && (desired[column].Role == CellRole.WideLead ||
                baseline[offset + column].Role == CellRole.WideLead))
            {
                changed[column + 1] = true;
            }
        }
    }

    private bool GapIsCheaper(int end, int next)
    {
        int gap = next - end - 1;
        int moveCost = 3 + (gap == 1 ? 0 : OutputBuffer.DigitCount(gap));
        if (gap > moveCost) return false;
        CellStyle style = desired[end].Style;
        for (int column = end + 1; column < next; column++)
        {
            RenderedCell cell = desired[column];
            if (cell.Role != CellRole.Single || cell.Text is not null || cell.Style != style || cell.Glyph.Value >= 0x7f)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsErasable(RenderedCell cell) =>
        cell.Role == CellRole.Single && cell.Text is null && cell.Glyph.Value == ' ' && cell.Style.Flags == CellStyleFlags.None;

    private RenderedCell Resolve(IReadOnlyCellSurface surface, ReadOnlySpan<Cell> source, int column, int row)
    {
        if (column >= source.Length) return RenderedCell.Blank;
        Cell cell = source[column];
        CellStyle style = ColorProjection.Project(cell.Style, options.Colors);
        switch (cell.Role)
        {
            case CellRole.WideLead when column + 1 < Columns && column + 1 < source.Length &&
                source[column + 1].Role == CellRole.WideContinuation:
                return new RenderedCell(cell.Glyph, cell.HasCluster ? surface.GetText(column, row) : null, style, CellRole.WideLead);
            case CellRole.WideContinuation when column > 0 && source[column - 1].Role == CellRole.WideLead:
                return new RenderedCell(new Rune(' '), null,
                    ColorProjection.Project(source[column - 1].Style, options.Colors), CellRole.WideContinuation);
            case CellRole.Single:
                return new RenderedCell(cell.Glyph, cell.HasCluster ? surface.GetText(column, row) : null, style, CellRole.Single);
            default:
                // Orphaned half of a wide glyph (invariant violation or clipped by the terminal edge).
                return new RenderedCell(new Rune(' '), null, style, CellRole.Single);
        }
    }

    /// <summary>Emits the cell at <paramref name="column"/> (and its continuation); returns the next column.</summary>
    private int EmitCell(int column, int row, int offset)
    {
        RenderedCell cell = desired[column];
        MoveTo(column, row);
        SetStyle(cell.Style);
        int expected = cell.Role == CellRole.WideLead ? 2 : 1;
        Span<Rune> runes = runeScratch.AsSpan(1);
        int count;
        if (cell.Text is null)
        {
            runes[0] = cell.Glyph;
            count = 1;
        }
        else
        {
            count = 0;
            foreach (Rune rune in cell.Text.EnumerateRunes())
            {
                if (count == runes.Length) break;
                runes[count++] = rune;
            }
        }

        ReadOnlySpan<Rune> glyph = runes[..count];
        int actual = count == 1 && glyph[0].Value < 0x7f ? 1 : UnicodeText.GetWidth(glyph, options.AmbiguousIsWide);
        if (actual == 0)
        {
            // Lone combining marks get a dotted-circle base, matching Nexer.Emulation's policy.
            runeScratch[0] = new Rune(0x25cc);
            glyph = runeScratch.AsSpan(0, count + 1);
            actual = 1;
        }

        if (actual > expected)
        {
            runeScratch[0] = new Rune('?');
            glyph = runeScratch.AsSpan(0, 1);
            actual = 1;
        }

        WriteGlyph(glyph, column);
        if (actual < expected)
        {
            runeScratch[0] = new Rune(' ');
            WriteGlyph(runeScratch.AsSpan(0, 1), column + 1);
        }

        work.Column = column + expected;
        if (work.Column >= Columns)
        {
            work.Column = Columns - 1;
            work.WrapPending = true;
        }

        AddPending(offset + column, cell);
        if (expected == 2) AddPending(offset + column + 1, desired[column + 1]);
        return column + expected;
    }

    private void WriteGlyph(ReadOnlySpan<Rune> runes, int column)
    {
        Rune first = runes[0];
        bool ascii = runes.Length == 1 && first.Value < 0x80;
        if (!joinBarrier)
        {
            // ASCII never extends a preceding ASCII cluster. Otherwise consult the segmenter; when it is not valid
            // the last emitted glyph was ASCII, whose state equals a fresh segmenter fed that one scalar.
            bool boundary;
            if (ascii && lastRune.Value < 0x80)
            {
                boundary = true;
                segmenterValid = false;
            }
            else
            {
                if (!segmenterValid)
                {
                    segmenter = default;
                    segmenter.Push(lastRune);
                }

                boundary = segmenter.Push(first);
                segmenterValid = true;
            }

            if (!boundary)
            {
                output.AppendCsi(column + 1, (byte)'G');
                joinBarrier = true;
            }
        }

        if (joinBarrier)
        {
            segmenter = default;
            segmenterValid = !ascii;
            if (segmenterValid) segmenter.Push(first);
        }

        if (segmenterValid)
        {
            for (int i = 1; i < runes.Length; i++) segmenter.Push(runes[i]);
        }
        foreach (Rune rune in runes) output.AppendRune(rune);
        lastRune = runes[^1];
        joinBarrier = false;
    }

    private void SetStyle(CellStyle style)
    {
        if (work.StyleKnown && work.Style == style) return;
        SgrEncoder.Append(output, work.StyleKnown ? work.Style : null, style);
        work.Style = style;
        work.StyleKnown = true;
    }

    private void MoveTo(int column, int row)
    {
        if (work.PositionKnown && !work.WrapPending && work.Row == row && work.Column == column) return;
        joinBarrier = true;
        int cupCost = 3 + (row == 0 && column == 0 ? 0 : (row == 0 ? 0 : OutputBuffer.DigitCount(row + 1)) +
            (column == 0 ? 0 : 1 + OutputBuffer.DigitCount(column + 1)));
        MoveKind best = MoveKind.Cup;
        int bestCost = cupCost;
        if (work.PositionKnown)
        {
            if (row == work.Row)
            {
                Consider(MoveKind.Cha, 3 + (column == 0 ? 0 : OutputBuffer.DigitCount(column + 1)), ref best, ref bestCost);
                if (column == 0) Consider(MoveKind.Cr, 1, ref best, ref bestCost);
                if (!work.WrapPending && column != work.Column)
                {
                    Consider(column > work.Column ? MoveKind.Cuf : MoveKind.Cub,
                        RelativeCost(Math.Abs(column - work.Column)), ref best, ref bestCost);
                }
            }
            else
            {
                int vertical = RelativeCost(Math.Abs(row - work.Row));
                if (column == 0) Consider(MoveKind.CrVertical, 1 + vertical, ref best, ref bestCost);
                if (column == work.Column && !work.WrapPending) Consider(MoveKind.Vertical, vertical, ref best, ref bestCost);
            }
        }

        switch (best)
        {
            case MoveKind.Cup:
                output.Append("\e["u8);
                if (row != 0) output.AppendNumber(row + 1);
                if (column != 0)
                {
                    output.Append((byte)';');
                    output.AppendNumber(column + 1);
                }

                output.Append((byte)'H');
                break;
            case MoveKind.Cha: output.AppendCsi(column + 1, (byte)'G'); break;
            case MoveKind.Cr: output.Append((byte)'\r'); break;
            case MoveKind.Cuf: output.AppendCsi(column - work.Column, (byte)'C'); break;
            case MoveKind.Cub: output.AppendCsi(work.Column - column, (byte)'D'); break;
            case MoveKind.CrVertical:
                output.Append((byte)'\r');
                AppendVertical(row);
                break;
            case MoveKind.Vertical: AppendVertical(row); break;
        }

        work.PositionKnown = true;
        work.WrapPending = false;
        work.Column = column;
        work.Row = row;
    }

    private void AppendVertical(int row)
    {
        if (row > work.Row) output.AppendCsi(row - work.Row, (byte)'B');
        else output.AppendCsi(work.Row - row, (byte)'A');
    }

    private static int RelativeCost(int distance) => 3 + (distance == 1 ? 0 : OutputBuffer.DigitCount(distance));

    private static void Consider(MoveKind kind, int cost, ref MoveKind best, ref int bestCost)
    {
        if (cost < bestCost)
        {
            best = kind;
            bestCost = cost;
        }
    }

    private void AddPending(int index, RenderedCell cell)
    {
        if (pendingCount == pending.Length) Array.Resize(ref pending, pending.Length * 2);
        pending[pendingCount++] = new PendingCell(index, cell);
    }

    private enum MoveKind : byte
    {
        Cup,
        Cha,
        Cr,
        Cuf,
        Cub,
        CrVertical,
        Vertical,
    }

    /// <summary>A projected cell as the terminal displays it. Text is set only for multi-scalar clusters.</summary>
    private readonly record struct RenderedCell(Rune Glyph, string? Text, CellStyle Style, CellRole Role)
    {
        public static RenderedCell Blank => new(new Rune(' '), null, default, CellRole.Single);
    }

    private readonly record struct PendingCell(int Index, RenderedCell Cell);

    private struct TerminalState
    {
        public bool PositionKnown;
        public bool WrapPending;
        public int Column;
        public int Row;
        public bool StyleKnown;
        public CellStyle Style;

        /// <summary>-1 unknown, 0 hidden, 1 visible.</summary>
        public sbyte CursorVisible;

        public static TerminalState Unknown => new() { CursorVisible = -1 };
    }
}
