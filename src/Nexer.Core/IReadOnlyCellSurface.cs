namespace Nexer.Core;

public interface IReadOnlyCellSurface
{
    int Columns { get; }
    int Rows { get; }
    ulong Version { get; }
    int RetainedClusterBytes { get; }
    ReadOnlySpan<Cell> GetRow(int row);
    string GetText(int column, int row);
    ulong GetRowVersion(int row);
}
