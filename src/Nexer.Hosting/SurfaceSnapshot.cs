using Nexer.Core;

namespace Nexer.Hosting;

/// <summary>
/// A host-owned copy of a presented surface. Only rows whose source version changed since the previous copy
/// from the same source are copied, and each copied row gets a fresh version so the renderer's row-version
/// damage detection keeps working across snapshots.
/// </summary>
internal sealed class SurfaceSnapshot : IReadOnlyCellSurface
{
    private readonly Cell[] cells;
    private readonly string?[] texts;
    private readonly ulong[] rowVersions;
    private readonly ulong[] sourceVersions;
    private IReadOnlyCellSurface? source;

    public SurfaceSnapshot(int columns, int rows)
    {
        Columns = columns;
        Rows = rows;
        cells = new Cell[columns * rows];
        texts = new string?[columns * rows];
        rowVersions = new ulong[rows];
        sourceVersions = new ulong[rows];
    }

    public int Columns { get; }

    public int Rows { get; }

    public ulong Version { get; private set; }

    public int RetainedClusterBytes => 0;

    public ReadOnlySpan<Cell> GetRow(int row) => cells.AsSpan(row * Columns, Columns);

    public ulong GetRowVersion(int row) => rowVersions[row];

    public string GetText(int column, int row)
    {
        int index = row * Columns + column;
        Cell cell = cells[index];
        return cell.Role == CellRole.WideContinuation ? string.Empty : texts[index] ?? cell.Glyph.ToString();
    }

    /// <summary>Copies changed rows of <paramref name="surface"/>, which must have this snapshot's size.</summary>
    public void CopyFrom(IReadOnlyCellSurface surface)
    {
        bool all = !ReferenceEquals(surface, source);
        source = surface;
        for (int row = 0; row < Rows; row++)
        {
            ulong version = surface.GetRowVersion(row);
            if (!all && version == sourceVersions[row]) continue;
            sourceVersions[row] = version;
            ReadOnlySpan<Cell> from = surface.GetRow(row);
            Span<Cell> to = cells.AsSpan(row * Columns, Columns);
            bool changed = !from.SequenceEqual(to);
            for (int column = 0; column < Columns; column++)
            {
                int index = row * Columns + column;
                string? text = from[column].HasCluster ? surface.GetText(column, row) : null;
                if (text != texts[index])
                {
                    texts[index] = text;
                    changed = true;
                }
            }

            if (!changed) continue;
            from.CopyTo(to);
            rowVersions[row] = ++Version;
        }
    }
}
