using System.Text;
using Nexer.Core.Text;

namespace Nexer.Core;

/// <summary>A bounded, single-owner surface. Row spans and cluster handles are borrowed until mutation.</summary>
public sealed class CellSurface : IReadOnlyCellSurface
{
    public const int MaxClusterScalars = 64;
    private readonly Cell[] cells;
    private readonly int[] rowSlots;
    private readonly ulong[] rowVersions;
    private readonly ClusterStore clusters;

    public int Columns { get; }
    public int Rows { get; }
    public ulong Version { get; private set; }
    public int RetainedClusterBytes => clusters.RetainedBytes;

    public CellSurface(int columns, int rows, int maxCells = 1_000_000, int maxClusterBytes = 1_048_576)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCells);
        ArgumentOutOfRangeException.ThrowIfNegative(maxClusterBytes);
        if ((long)columns * rows > maxCells)
        {
            throw new ArgumentOutOfRangeException(nameof(columns), "Surface exceeds the configured cell limit.");
        }

        Columns = columns;
        Rows = rows;
        cells = new Cell[columns * rows];
        rowSlots = new int[rows];
        rowVersions = new ulong[rows];
        clusters = new ClusterStore(maxClusterBytes);
        for (int row = 0; row < rows; row++) rowSlots[row] = row;
    }

    public ReadOnlySpan<Cell> GetRow(int row)
    {
        ValidateRow(row);
        return cells.AsSpan(rowSlots[row] * Columns, Columns);
    }

    public string GetText(int column, int row)
    {
        ValidatePosition(column, row);
        Cell cell = cells[rowSlots[row] * Columns + column];
        return cell.Role == CellRole.WideContinuation ? string.Empty
            : cell.HasCluster ? clusters.Resolve(cell.ClusterId) : cell.Glyph.ToString();
    }

    public ulong GetRowVersion(int row)
    {
        ValidateRow(row);
        return rowVersions[row];
    }

    /// <summary>Writes one scalar with explicit width. Use UnicodeText for measurement.</summary>
    public void Write(int column, int row, Rune glyph, int width = 1, CellStyle style = default)
    {
        ValidateWrite(column, row, width);
        WriteCell(column, row, width == 2 ? Cell.Lead(glyph, style) : Cell.Create(glyph, style), width);
    }

    /// <summary>Writes one presegmented cluster. Cluster-budget exhaustion substitutes U+FFFD.</summary>
    public void WriteCluster(int column, int row, ReadOnlySpan<Rune> cluster, int width, CellStyle style = default)
    {
        ValidateWrite(column, row, width);
        if (cluster.IsEmpty || cluster.Length > MaxClusterScalars)
        {
            throw new ArgumentOutOfRangeException(nameof(cluster));
        }

        foreach (Rune rune in cluster)
        {
            if (Rune.IsControl(rune)) throw new ArgumentException("Clusters cannot contain controls.", nameof(cluster));
        }

        if (cluster.Length == 1)
        {
            Write(column, row, cluster[0], width, style);
            return;
        }

        int id = clusters.Acquire(cluster);
        try
        {
            Cell desired = id == 0
                ? (width == 2 ? Cell.Lead(Rune.ReplacementChar, style) : Cell.Create(Rune.ReplacementChar, style))
                : Cell.Cluster(cluster[0], id, style, width);
            WriteCell(column, row, desired, width);
        }
        finally
        {
            clusters.Release(id);
        }
    }

    public void Erase(int column, int row, int count, CellStyle style = default)
    {
        ValidatePosition(column, row);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, Columns - column);
        if (count == 0) return;
        Cell blank = Cell.Create(new Rune(' '), style);
        int offset = rowSlots[row] * Columns;
        bool changed = false;
        for (int x = column; x < column + count; x++)
        {
            if (cells[offset + x] != blank) { changed = true; break; }
        }

        if (!changed) return;
        ulong version = checked(Version + 1);
        for (int x = column; x < column + count; x++)
        {
            Repair(offset + x, x);
            SetCell(offset + x, blank);
        }

        Version = rowVersions[row] = version;
    }

    public void Clear()
    {
        for (int row = 0; row < Rows; row++) Erase(0, row, Columns);
    }

    public void ScrollUp(int top, int bottom, int count = 1, CellStyle eraseStyle = default) =>
        Scroll(top, bottom, count, eraseStyle, down: false);

    public void ScrollDown(int top, int bottom, int count = 1, CellStyle eraseStyle = default) =>
        Scroll(top, bottom, count, eraseStyle, down: true);

    private void Scroll(int top, int bottom, int count, CellStyle eraseStyle, bool down)
    {
        ValidateRow(top);
        ValidateRow(bottom);
        ArgumentOutOfRangeException.ThrowIfLessThan(bottom, top);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        int height = bottom - top + 1;
        count = Math.Min(count, height);
        if (count == 0) return;
        ulong version = checked(Version + 1);
        Span<int> slots = rowSlots.AsSpan(top, height);
        int rotation = down ? height - count : count;
        slots[..rotation].Reverse();
        slots[rotation..].Reverse();
        slots.Reverse();
        Cell blank = Cell.Create(new Rune(' '), eraseStyle);
        int first = down ? top : bottom - count + 1;
        for (int row = first; row < first + count; row++)
        {
            int offset = rowSlots[row] * Columns;
            for (int column = 0; column < Columns; column++) SetCell(offset + column, blank);
        }

        Version = version;
        for (int row = top; row <= bottom; row++) rowVersions[row] = version;
    }

    private void WriteCell(int column, int row, Cell desired, int width)
    {
        bool clipped = width == 2 && column == Columns - 1;
        if (clipped) desired = Cell.Create(new Rune(' '), desired.Style);
        int index = rowSlots[row] * Columns + column;
        if (cells[index] == desired &&
            (width == 1 || clipped || cells[index + 1] == Cell.Continuation(desired.Style))) return;
        ulong version = checked(Version + 1);
        Repair(index, column);
        if (width == 2 && !clipped)
        {
            Repair(index + 1, column + 1);
            SetCell(index + 1, Cell.Continuation(desired.Style));
        }

        SetCell(index, desired);
        Version = rowVersions[row] = version;
    }

    private void SetCell(int index, Cell value)
    {
        clusters.Retain(value.ClusterId);
        clusters.Release(cells[index].ClusterId);
        cells[index] = value;
    }

    private void Repair(int index, int column)
    {
        if (cells[index].Role == CellRole.WideContinuation && column > 0) SetCell(index - 1, Cell.Blank);
        else if (cells[index].Role == CellRole.WideLead && column + 1 < Columns) SetCell(index + 1, Cell.Blank);
        SetCell(index, Cell.Blank);
    }

    private void ValidateWrite(int column, int row, int width)
    {
        ValidatePosition(column, row);
        if (width is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(width));
    }

    private void ValidatePosition(int column, int row)
    {
        ValidateRow(row);
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, Columns);
    }

    private void ValidateRow(int row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, Rows);
    }
}
