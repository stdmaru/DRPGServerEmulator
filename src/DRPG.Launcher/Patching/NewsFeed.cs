using System.Text.Json;
using System.Text.Json.Serialization;

namespace DRPG.Launcher.Patching;

/// <summary>One news entry fetched from <c>MainUrl.NewsPageUrl</c>: <c>{ Category, Title, Date, URL }</c>.</summary>
public sealed record NewsItem
{
    public string Category { get; init; } = "Info";
    public string Title { get; init; } = "";
    public string Date { get; init; } = "";

    [JsonPropertyName("URL")]
    public string Url { get; init; } = "";
}

/// <summary>Parses the dynamic news JSON array returned by <c>MainUrl.NewsPageUrl</c>.</summary>
public static class NewsFeed
{
    public static IReadOnlyList<NewsItem> Parse(string json)
    {
        NewsItem[]? raw;
        try
        {
            raw = JsonSerializer.Deserialize(json, NewsJsonContext.Default.NewsItemArray);
        }
        catch (JsonException e)
        {
            throw new PatchConfigurationException($"invalid news JSON: {e.Message}");
        }
        if (raw is null || raw.Length == 0)
            return [];
        return raw
            .Where(n => n is not null && !string.IsNullOrWhiteSpace(n.Title))
            .Select(n => new NewsItem
            {
                Category = NormalizeCategory(n.Category),
                Title = n.Title.Trim(),
                Date = (n.Date ?? "").Trim(),
                Url = NormalizeUrl(n.Url),
            })
            .ToArray();
    }

    private static string NormalizeCategory(string? raw)
    {
        string trimmed = (raw ?? "").Trim();
        if (string.Equals(trimmed, "Event", StringComparison.OrdinalIgnoreCase))
            return "Event";
        if (string.Equals(trimmed, "Update", StringComparison.OrdinalIgnoreCase))
            return "Update";
        return "Info";
    }

    /// <summary>Only http/https absolute URLs are accepted as clickable links.</summary>
    private static string NormalizeUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";
        string trimmed = raw.Trim();
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http"
            ? uri.AbsoluteUri
            : "";
    }
}

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(NewsItem[]))]
internal sealed partial class NewsJsonContext : JsonSerializerContext;
