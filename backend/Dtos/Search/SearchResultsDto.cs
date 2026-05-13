namespace SayimLink.Api.Dtos.Search;

public sealed class SearchResultsDto
{
    public List<SearchResultItem> Firmalar { get; set; } = new();
    public List<SearchResultItem> Magazalar { get; set; } = new();
    public List<SearchResultItem> Kullanicilar { get; set; } = new();
    public List<SearchResultItem> Oturumlar { get; set; } = new();
}

public sealed class SearchResultItem
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? Badge { get; set; }
    /// <summary>Optional client route hint (e.g. "/firmalar", "/magazalar"). Frontend may override.</summary>
    public string? Route { get; set; }
}
