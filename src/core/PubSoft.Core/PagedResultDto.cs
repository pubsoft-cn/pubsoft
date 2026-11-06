namespace PubSoft.Core;

/// <summary>A page of items plus the total unpaged count.</summary>
public class PagedResultDto<T>
{

    /// <summary>Creates an empty page.</summary>
    public PagedResultDto() { }

    /// <summary>Creates a page with items and total.</summary>
    public PagedResultDto(long totalCount, List<T> items)
    {
        TotalCount = totalCount;
        Items = items;
    }
    /// <summary>The page contents.</summary>
    public List<T> Items { get; set; } = [];
    /// <summary>Total rows matching the query before paging.</summary>
    public long TotalCount { get; set; }
}
