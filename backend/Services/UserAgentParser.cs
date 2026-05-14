using System.Text.RegularExpressions;

namespace SayimLink.Api.Services;

/// <summary>Tiny, dependency-free user-agent parser. Returns "Chrome", "Edge",
/// "macOS" etc. — enough for a friendly device label on the active-sessions list.
/// Not a substitute for a proper UA library; covers the major modern targets.</summary>
public static class UserAgentParser
{
    public static (string browser, string os) Parse(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return ("Bilinmeyen tarayıcı", "Bilinmeyen sistem");
        var ua = userAgent;
        return (DetectBrowser(ua), DetectOs(ua));
    }

    private static string DetectBrowser(string ua)
    {
        // Order matters — Edge advertises Chrome, Chrome advertises Safari.
        if (Regex.IsMatch(ua, @"Edg/\d", RegexOptions.IgnoreCase))     return "Edge";
        if (Regex.IsMatch(ua, @"OPR/\d|Opera/\d", RegexOptions.IgnoreCase)) return "Opera";
        if (Regex.IsMatch(ua, @"Firefox/\d", RegexOptions.IgnoreCase)) return "Firefox";
        if (Regex.IsMatch(ua, @"Chrome/\d", RegexOptions.IgnoreCase))  return "Chrome";
        if (Regex.IsMatch(ua, @"Safari/\d", RegexOptions.IgnoreCase))  return "Safari";
        return "Tarayıcı";
    }

    private static string DetectOs(string ua)
    {
        if (Regex.IsMatch(ua, @"Windows NT", RegexOptions.IgnoreCase))  return "Windows";
        if (Regex.IsMatch(ua, @"iPhone|iPad|iPod", RegexOptions.IgnoreCase)) return "iOS";
        if (Regex.IsMatch(ua, @"Android", RegexOptions.IgnoreCase))     return "Android";
        if (Regex.IsMatch(ua, @"Mac OS X|Macintosh", RegexOptions.IgnoreCase)) return "macOS";
        if (Regex.IsMatch(ua, @"Linux", RegexOptions.IgnoreCase))       return "Linux";
        return "Sistem";
    }
}
