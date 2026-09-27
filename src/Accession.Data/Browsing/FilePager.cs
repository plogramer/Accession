namespace Accession.Data.Browsing;

/// <summary>How a page was read (tests and logging).</summary>
public enum FilePageMethod
{
    /// <summary>Keyset after the previous page's last row (or the first page).</summary>
    After,

    /// <summary>Keyset before the current page's first row.</summary>
    Before,

    /// <summary>The last rows, read from the end.</summary>
    Last,

    /// <summary>LIMIT/OFFSET (a jump to a page whose boundaries are not known yet).</summary>
    Offset,
}

/// <summary>
/// Numbered pages over the files matching one filter and sort (requirements BRW-02, NFR-02). Picks the fastest
/// query for each jump: keyset for next/previous, reading from the end for the last page, and OFFSET only for
/// jumps to pages not visited yet. Page boundaries seen so far are remembered, so going back is instant.
/// Create a new pager when the filter, sort or page size changes. <see cref="CountTotals"/> may run while a page
/// loads (the count can take a while on large results); loads themselves must not overlap.
/// </summary>
public sealed class FilePager
{
    private readonly FileBrowserQueries _queries;
    private readonly InventoryDatabase _database;
    private readonly object _gate = new();

    /// <summary>Keyset cursor to read page N with "after" (the last row of page N-1). Page 0 starts with null.</summary>
    private readonly Dictionary<int, FilePageCursor?> _starts = new() { [0] = null };

    public FilePager(FileBrowserQueries queries, InventoryDatabase database, FileFilter filter, FileSortColumn sort, bool descending, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        _queries = queries;
        _database = database;
        Filter = filter;
        Sort = sort;
        Descending = descending;
        PageSize = pageSize;
    }

    public FileFilter Filter { get; }
    public FileSortColumn Sort { get; }
    public bool Descending { get; }
    public int PageSize { get; }

    /// <summary>Matching files; set by <see cref="CountTotals"/>. Until then paging works but Last is unknown.</summary>
    public long? TotalCount
    {
        get
        {
            lock (_gate)
            {
                return _totalCount;
            }
        }
    }

    public long TotalBytes
    {
        get
        {
            lock (_gate)
            {
                return _totalBytes;
            }
        }
    }

    public int PageIndex { get; private set; } = -1;

    public IReadOnlyList<FileItem> Items { get; private set; } = [];

    public FilePageMethod LastMethod { get; private set; }

    public int PageCount => (int)Math.Max(1, ((TotalCount ?? 0) + PageSize - 1) / PageSize);

    private long? _totalCount;
    private long _totalBytes;

    public void CountTotals(CancellationToken cancellationToken = default)
    {
        var totals = _queries.Totals(_database, Filter, cancellationToken);
        lock (_gate)
        {
            _totalCount = totals.FileCount;
            _totalBytes = totals.TotalBytes;
        }
    }

    /// <summary>Reads page <paramref name="pageIndex"/> (zero-based, clamped to the known page count).</summary>
    public IReadOnlyList<FileItem> Load(int pageIndex, CancellationToken cancellationToken = default)
    {
        var last = PageCount - 1;
        pageIndex = Math.Max(0, TotalCount is null ? pageIndex : Math.Min(pageIndex, last));

        IReadOnlyList<FileItem> items;
        if (_starts.TryGetValue(pageIndex, out var start))
        {
            items = _queries.Page(_database, Filter, Sort, Descending, start, PageSize, cancellationToken).Items;
            LastMethod = FilePageMethod.After;
        }
        else if (pageIndex == PageIndex - 1 && Items.Count > 0)
        {
            items = _queries.PageBefore(_database, Filter, Sort, Descending, FileBrowserQueries.CursorOf(Items[0], Sort), PageSize, cancellationToken);
            LastMethod = FilePageMethod.Before;
        }
        else if (TotalCount is { } total && pageIndex == last && pageIndex > 0)
        {
            var count = (int)(total - (long)last * PageSize);
            items = _queries.LastRows(_database, Filter, Sort, Descending, count, cancellationToken);
            LastMethod = FilePageMethod.Last;
        }
        else
        {
            items = _queries.PageAtOffset(_database, Filter, Sort, Descending, (long)pageIndex * PageSize, PageSize, cancellationToken);
            LastMethod = FilePageMethod.Offset;
        }

        if (items.Count == PageSize)
        {
            _starts[pageIndex + 1] = FileBrowserQueries.CursorOf(items[^1], Sort);
        }

        PageIndex = pageIndex;
        Items = items;
        return items;
    }
}
