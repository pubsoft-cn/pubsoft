namespace PubSoft.Core;

public class PagedResultDto<T>
{
    public List<T> Items { get; set; } = [];
    public long TotalCount { get; set; }

    public PagedResultDto() { }

    public PagedResultDto(long totalCount, List<T> items)
    {
        TotalCount = totalCount;
        Items = items;
    }
}
