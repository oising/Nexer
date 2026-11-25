using System.Text;
using Nexer.Core;
using Nexer.Core.Protocol;
using Nexer.Core.Text;

namespace Nexer.Emulation;

/// <summary>Single-owner, headless UTF-8 terminal. See COMPATIBILITY.md for the initial supported profile.</summary>
public sealed partial class Terminal : IVtSink
{
    private readonly CellSurface screen;
    private readonly VtScanner scanner;
    private readonly bool ambiguousIsWide;
    private readonly bool[] tabs;
    private readonly Rune[] cluster = new Rune[CellSurface.MaxClusterScalars];
    private GraphemeSegmenter segmenter;
    private int clusterLength, anchorColumn, anchorRow;
    private bool clusterOverflow;
    private SavedCursor saved;

    private readonly record struct SavedCursor(int Column, int Row, CellStyle Style, bool Origin, bool AutoWrap);

    public IReadOnlyCellSurface Screen => screen;
    public int CursorColumn { get; private set; }
    public int CursorRow { get; private set; }
    public bool CursorVisible { get; private set; } = true;
    public bool WrapPending { get; private set; }
    public bool AutoWrap { get; private set; } = true;
    public bool OriginMode { get; private set; }
    public int ScrollTop { get; private set; }
    public int ScrollBottom { get; private set; }
    public CellStyle CurrentStyle { get; private set; }
    public event EventHandler? Bell;

    public Terminal(int columns, int rows, TerminalOptions? options = null)
    {
        options ??= new TerminalOptions();
        screen = new CellSurface(columns, rows, options.MaxCells, options.MaxClusterBytes);
        scanner = new VtScanner(this, maxStringBytes: options.MaxControlStringBytes);
        ambiguousIsWide = options.AmbiguousIsWide;
        tabs = new bool[columns];
        ResetState();
    }

    public void Feed(ReadOnlySpan<byte> bytes) => scanner.Feed(bytes);

    public void CompleteInput()
    {
        scanner.Complete();
        BreakCluster();
    }

    void IVtSink.Boundary() => BreakCluster();

    void IVtSink.Print(Rune value)
    {
        if (Rune.IsControl(value))
        {
            BreakCluster();
            return;
        }

        bool newCluster = segmenter.Push(value);
        if (newCluster)
        {
            clusterLength = 0;
            clusterOverflow = false;
        }
        else if (clusterOverflow)
        {
            return;
        }

        if (clusterLength == cluster.Length)
        {
            clusterOverflow = true;
        }
        else
        {
            cluster[clusterLength++] = value;
        }

        scoped ReadOnlySpan<Rune> text = cluster.AsSpan(0, clusterLength);
        Span<Rune> fallback = stackalloc Rune[CellSurface.MaxClusterScalars];
        int width = clusterOverflow ? 1 : UnicodeText.GetWidth(text, ambiguousIsWide);
        if (clusterOverflow)
        {
            fallback[0] = Rune.ReplacementChar;
            text = fallback[..1];
        }
        else if (width == 0)
        {
            width = 1;
            if (text.Length == fallback.Length)
            {
                clusterOverflow = true;
                fallback[0] = Rune.ReplacementChar;
                text = fallback[..1];
            }
            else
            {
                fallback[0] = new Rune(0x25cc);
                text.CopyTo(fallback[1..]);
                text = fallback[..(text.Length + 1)];
            }
        }

        if (newCluster)
        {
            if (WrapPending)
            {
                if (AutoWrap)
                {
                    CursorColumn = 0;
                    Index();
                }

                WrapPending = false;
            }

            if (width == 2 && CursorColumn == screen.Columns - 1 && AutoWrap && screen.Columns > 1)
            {
                CursorColumn = 0;
                Index();
            }

            anchorColumn = CursorColumn;
            anchorRow = CursorRow;
        }
        else if (width == 2 && anchorColumn == screen.Columns - 1 && AutoWrap && screen.Columns > 1)
        {
            // Width growth cannot retroactively undo a displayed provisional cell.
            screen.Erase(anchorColumn, anchorRow, 1, EraseStyle);
            CursorRow = anchorRow;
            CursorColumn = 0;
            Index();
            anchorColumn = CursorColumn;
            anchorRow = CursorRow;
        }

        screen.WriteCluster(anchorColumn, anchorRow, text, width, CurrentStyle);
        CursorRow = anchorRow;
        CursorColumn = Math.Min(anchorColumn + width, screen.Columns - 1);
        WrapPending = AutoWrap && anchorColumn + width >= screen.Columns;
    }

    void IVtSink.Execute(byte control)
    {
        BreakCluster();
        switch (control)
        {
            case 0x07:
                Bell?.Invoke(this, EventArgs.Empty);
                break;
            case 0x08:
                CursorColumn = Math.Max(0, CursorColumn - 1);
                WrapPending = false;
                break;
            case 0x09:
                int column = CursorColumn + 1;
                while (column < screen.Columns - 1 && !tabs[column]) column++;
                CursorColumn = Math.Min(column, screen.Columns - 1);
                WrapPending = false;
                break;
            case 0x0a:
            case 0x0b:
            case 0x0c:
                Index();
                WrapPending = false;
                break;
            case 0x0d:
                CursorColumn = 0;
                WrapPending = false;
                break;
        }
    }

    void IVtSink.String(VtStringKind kind, ReadOnlySpan<byte> payload) => BreakCluster();

    void IVtSink.Dispatch(VtSequenceKind kind, ReadOnlySpan<byte> parameters, ReadOnlySpan<byte> intermediates, byte final)
    {
        BreakCluster();
        if (!intermediates.IsEmpty) return;
        if (kind == VtSequenceKind.Escape)
        {
            switch (final)
            {
                case (byte)'7': SaveCursor(); break;
                case (byte)'8': RestoreCursor(); break;
                case (byte)'D': Index(); WrapPending = false; break;
                case (byte)'E': CursorColumn = 0; Index(); WrapPending = false; break;
                case (byte)'H': tabs[CursorColumn] = true; break;
                case (byte)'M':
                    if (CursorRow == ScrollTop) screen.ScrollDown(ScrollTop, ScrollBottom, eraseStyle: EraseStyle);
                    else CursorRow = Math.Max(0, CursorRow - 1);
                    WrapPending = false;
                    break;
                case (byte)'c': screen.Clear(); ResetState(); break;
            }

            return;
        }

        if (final == (byte)'m')
        {
            ApplySgr(parameters);
            return;
        }

        bool privateMode = !parameters.IsEmpty && parameters[0] == (byte)'?';
        if (privateMode) parameters = parameters[1..];
        Span<int> values = stackalloc int[32];
        if (!TryParseParameters(parameters, values, out int count)) return;
        if (privateMode)
        {
            if (final is (byte)'h' or (byte)'l') SetModes(values[..count], final == (byte)'h');
            return;
        }

        int first = Math.Max(1, values[0]);
        switch (final)
        {
            case (byte)'A':
                Move(CursorColumn, Math.Max(CursorRow >= ScrollTop ? ScrollTop : 0, CursorRow - first));
                break;
            case (byte)'B':
            case (byte)'e':
                Move(CursorColumn, Math.Min(CursorRow <= ScrollBottom ? ScrollBottom : screen.Rows - 1, CursorRow + first));
                break;
            case (byte)'C':
            case (byte)'a': Move(CursorColumn + first, CursorRow); break;
            case (byte)'D': Move(CursorColumn - first, CursorRow); break;
            case (byte)'E':
                Move(0, Math.Min(CursorRow <= ScrollBottom ? ScrollBottom : screen.Rows - 1, CursorRow + first));
                break;
            case (byte)'F':
                Move(0, Math.Max(CursorRow >= ScrollTop ? ScrollTop : 0, CursorRow - first));
                break;
            case (byte)'G':
            case (byte)'`': Move(first - 1, CursorRow); break;
            case (byte)'d': MoveAbsolute(CursorColumn, first - 1); break;
            case (byte)'H':
            case (byte)'f': MoveAbsolute((count > 1 ? Math.Max(1, values[1]) : 1) - 1, first - 1); break;
            case (byte)'J': EraseDisplay(values[0]); break;
            case (byte)'K': EraseLine(values[0]); break;
            case (byte)'X':
                screen.Erase(CursorColumn, CursorRow, Math.Min(first, screen.Columns - CursorColumn), EraseStyle);
                WrapPending = false;
                break;
            case (byte)'S':
                screen.ScrollUp(ScrollTop, ScrollBottom, first, EraseStyle);
                WrapPending = false;
                break;
            case (byte)'T':
                screen.ScrollDown(ScrollTop, ScrollBottom, first, EraseStyle);
                WrapPending = false;
                break;
            case (byte)'r':
                int bottom = count > 1 && values[1] > 0 ? values[1] : screen.Rows;
                if (first < bottom && bottom <= screen.Rows)
                {
                    ScrollTop = first - 1;
                    ScrollBottom = bottom - 1;
                    MoveAbsolute(0, 0);
                }
                break;
            case (byte)'g':
                if (values[0] == 0) tabs[CursorColumn] = false;
                else if (values[0] == 3) Array.Clear(tabs);
                break;
            case (byte)'s': SaveCursor(); break;
            case (byte)'u': RestoreCursor(); break;
        }
    }

    private CellStyle EraseStyle => new(CurrentStyle.Foreground, CurrentStyle.Background);

    private void Index()
    {
        if (CursorRow == ScrollBottom) screen.ScrollUp(ScrollTop, ScrollBottom, eraseStyle: EraseStyle);
        else CursorRow = Math.Min(screen.Rows - 1, CursorRow + 1);
    }

    private void Move(int column, int row)
    {
        CursorColumn = Math.Clamp(column, 0, screen.Columns - 1);
        CursorRow = Math.Clamp(row, 0, screen.Rows - 1);
        WrapPending = false;
    }

    private void MoveAbsolute(int column, int row) =>
        Move(column, OriginMode ? Math.Clamp(row + ScrollTop, ScrollTop, ScrollBottom) : row);

    private void SaveCursor() => saved = new(CursorColumn, CursorRow, CurrentStyle, OriginMode, AutoWrap);

    private void RestoreCursor()
    {
        CurrentStyle = saved.Style;
        OriginMode = saved.Origin;
        AutoWrap = saved.AutoWrap;
        Move(saved.Column, OriginMode ? Math.Clamp(saved.Row, ScrollTop, ScrollBottom) : saved.Row);
    }

    private void SetModes(ReadOnlySpan<int> values, bool enabled)
    {
        foreach (int value in values)
        {
            switch (value)
            {
                case 6: OriginMode = enabled; MoveAbsolute(0, 0); break;
                case 7: AutoWrap = enabled; WrapPending = false; break;
                case 25: CursorVisible = enabled; break;
            }
        }
    }

    private void EraseDisplay(int mode)
    {
        if (mode is < 0 or > 2) return;
        if (mode == 0)
        {
            screen.Erase(CursorColumn, CursorRow, screen.Columns - CursorColumn, EraseStyle);
            for (int row = CursorRow + 1; row < screen.Rows; row++) screen.Erase(0, row, screen.Columns, EraseStyle);
        }
        else if (mode == 1)
        {
            for (int row = 0; row < CursorRow; row++) screen.Erase(0, row, screen.Columns, EraseStyle);
            screen.Erase(0, CursorRow, CursorColumn + 1, EraseStyle);
        }
        else
        {
            for (int row = 0; row < screen.Rows; row++) screen.Erase(0, row, screen.Columns, EraseStyle);
        }

        WrapPending = false;
    }

    private void EraseLine(int mode)
    {
        if (mode is < 0 or > 2) return;
        int start = mode == 0 ? CursorColumn : 0;
        int count = mode == 1 ? CursorColumn + 1 : screen.Columns - start;
        screen.Erase(start, CursorRow, count, EraseStyle);
        WrapPending = false;
    }

    private void ResetState()
    {
        CursorColumn = CursorRow = ScrollTop = 0;
        ScrollBottom = screen.Rows - 1;
        CurrentStyle = default;
        AutoWrap = CursorVisible = true;
        OriginMode = WrapPending = false;
        for (int column = 0; column < tabs.Length; column++) tabs[column] = column > 0 && column % 8 == 0;
        saved = new(0, 0, default, false, true);
        BreakCluster();
    }

    private void BreakCluster()
    {
        segmenter = default;
        clusterLength = 0;
        clusterOverflow = false;
    }

    private static bool TryParseParameters(ReadOnlySpan<byte> bytes, Span<int> values, out int count)
    {
        count = 1;
        values[0] = 0;
        foreach (byte value in bytes)
        {
            if (value == (byte)';')
            {
                if (count == values.Length) return false;
                values[count++] = 0;
            }
            else if (value is >= (byte)'0' and <= (byte)'9')
            {
                int next = values[count - 1] * 10 + value - '0';
                if (next > 1_000_000) return false;
                values[count - 1] = next;
            }
            else return false;
        }

        return true;
    }
}
