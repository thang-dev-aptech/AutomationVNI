namespace Backend.Shared;

public class PagedFilterRequest
{
    public string? Keyword { get; set; }
    public int Index { get; set; } = 1;
    public int Size { get; set; } = 20;
}
