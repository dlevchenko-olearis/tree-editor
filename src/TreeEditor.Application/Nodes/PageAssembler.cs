namespace TreeEditor.Application.Nodes;

/// <summary>Turns rows fetched with one extra row into a page with a keyset cursor.</summary>
internal static class PageAssembler
{
    /// <summary>Number of rows to fetch for a page of <paramref name="limit"/> rows.</summary>
    public static int RowsToFetch(PageLimit limit) => limit.Value + 1;

    /// <summary>Keeps the first <paramref name="limit"/> rows and sets the cursor only when more rows exist.</summary>
    public static NodePage Assemble(IReadOnlyList<NodeListItem> rows, PageLimit limit)
    {
        if (rows.Count <= limit.Value)
        {
            return new NodePage(rows, null);
        }

        var items = rows.Take(limit.Value).ToList();
        return new NodePage(items, items[^1].Id);
    }
}
