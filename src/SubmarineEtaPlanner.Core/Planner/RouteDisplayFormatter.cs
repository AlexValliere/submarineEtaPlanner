using System.Globalization;

namespace SubmarineEtaPlanner.Planner;

public static class RouteDisplayFormatter
{
    public static string FormatCompactRoute(
        IReadOnlyList<uint> route,
        Func<uint, string> pointName)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(pointName);

        return route.Count == 0
            ? "—"
            : string.Join(" → ", route.Select(point => ExtractPointCode(point, pointName(point))));
    }

    public static string ExtractPointCode(uint point, string? pointName)
    {
        var name = pointName?.Trim() ?? string.Empty;
        if (name.EndsWith(')'))
        {
            var opening = name.LastIndexOf('(');
            if (opening >= 0)
            {
                var candidate = name[(opening + 1)..^1].Trim();
                if (IsShortCode(candidate))
                    return candidate.ToUpperInvariant();
            }
        }

        if (IsShortCode(name))
            return name.ToUpperInvariant();

        return point.ToString(CultureInfo.InvariantCulture);
    }

    private static bool IsShortCode(string value)
        => value.Length is >= 1 and <= 3 && value.All(char.IsLetterOrDigit);
}
