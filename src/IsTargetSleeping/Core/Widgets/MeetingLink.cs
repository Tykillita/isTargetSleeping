namespace IsTargetSleeping;

public static class MeetingLink
{
    public static Uri? Extract(string? url, string? location, string? description)
    {
        var links = new List<Uri>();
        foreach (var source in new[] { url, location, description })
        {
            if (string.IsNullOrWhiteSpace(source)) continue;
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(source, "https?://[^\\s<>\\\"']+", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                var value = match.Value.TrimEnd('.', ',', ';', ')', ']');
                if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") links.Add(uri);
            }
        }
        return links.FirstOrDefault(uri => uri.Host.Equals("teams.microsoft.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("teams.live.com", StringComparison.OrdinalIgnoreCase)) ?? links.FirstOrDefault();
    }
}
