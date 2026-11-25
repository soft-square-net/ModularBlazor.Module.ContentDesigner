namespace soft.square.net.ContentDesigner.Rendering.Blocks;

/// <summary>Where a media block's file is: an asset, or an embeddable player address (YouTube, Vimeo, embedUrl).</summary>
public sealed record Embed(string Url, bool IsEmbed)
{
    public static Embed? Of(BlockContext context, string key)
    {
        if (context.Text("embedUrl") is { } embedUrl && IsHttps(embedUrl)) return new(embedUrl, true);
        if (context.Text(key) is not { } value) return null;
        if (IsHttps(value) && Player(value) is { } player) return new(player, true);
        return context.Asset(key) is { } asset ? new(asset.Url, false) : null;
    }

    private static bool IsHttps(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    /// <summary>The embed address of a YouTube or Vimeo page, or null.</summary>
    internal static string? Player(string url)
    {
        var uri = new Uri(url);
        var host = uri.Host.ToLowerInvariant();
        if (host is "www.youtube.com" or "youtube.com" or "m.youtube.com")
        {
            if (uri.AbsolutePath.StartsWith("/embed/", StringComparison.Ordinal)) return url;
            var id = uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).FirstOrDefault(p => p[0] == "v" && p.Length == 2)?[1];
            return id is { Length: > 0 } ? $"https://www.youtube.com/embed/{Uri.EscapeDataString(Uri.UnescapeDataString(id))}" : null;
        }
        if (host == "youtu.be") return $"https://www.youtube.com/embed/{Uri.EscapeDataString(uri.AbsolutePath.Trim('/'))}";
        if (host is "vimeo.com" or "www.vimeo.com") return $"https://player.vimeo.com/video/{Uri.EscapeDataString(uri.AbsolutePath.Trim('/'))}";
        if (host == "player.vimeo.com") return url;
        return null;
    }
}
