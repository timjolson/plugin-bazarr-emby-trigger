using System;
using System.IO;

namespace Plugin.Bazarr.Emby.Trigger.Services;

internal static class MatchingHelpers
{
    public static int? TryParseInt(string? value)
        => int.TryParse(value, out var parsed) ? parsed : null;

    public static bool TitleAndYearMatch(string leftTitle, string rightTitle, string leftYear, int? rightYear)
    {
        if (!string.Equals(Normalize(leftTitle), Normalize(rightTitle), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!rightYear.HasValue || !int.TryParse(leftYear, out var parsedYear))
        {
            return true;
        }

        return Math.Abs(parsedYear - rightYear.Value) <= 1;
    }

    public static string Normalize(string value)
        => (value ?? string.Empty).Trim().Replace("_", " ").Replace(".", " ");

    public static bool PathsEquivalent(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
