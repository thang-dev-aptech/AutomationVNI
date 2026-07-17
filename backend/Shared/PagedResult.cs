namespace Backend.Shared;

public class PagedResult<T>
{
    public List<T> Items { get; set; } = [];
    public int Total { get; set; }
    public int Index { get; set; }
    public int Size { get; set; }
}
