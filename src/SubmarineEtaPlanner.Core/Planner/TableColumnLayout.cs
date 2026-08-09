namespace SubmarineEtaPlanner.Planner;

public sealed record TableColumnMeasurement(
    float MeasuredWidth,
    float MinimumWidth,
    float MaximumWidth,
    bool Flexible = false,
    float FlexWeight = 1f);

public sealed record TableColumnLayout(
    IReadOnlyList<float> Widths,
    bool RequiresHorizontalScroll,
    float InnerWidth);

public static class TableColumnLayoutAllocator
{
    public static TableColumnLayout Allocate(
        IReadOnlyList<TableColumnMeasurement> columns,
        float availableWidth)
    {
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Count == 0)
            return new TableColumnLayout([], false, 0f);

        var desired = columns
            .Select(column => Math.Clamp(
                Math.Max(0f, column.MeasuredWidth),
                Math.Max(0f, column.MinimumWidth),
                Math.Max(column.MinimumWidth, column.MaximumWidth)))
            .ToArray();
        var minimum = columns.Select(column => Math.Max(0f, column.MinimumWidth)).ToArray();
        var desiredTotal = desired.Sum();
        var minimumTotal = minimum.Sum();
        var usableWidth = Math.Max(0f, availableWidth);

        if (usableWidth < minimumTotal)
            return new TableColumnLayout(desired, true, desiredTotal);

        if (usableWidth < desiredTotal)
        {
            var shrinkable = Math.Max(0.0001f, desiredTotal - minimumTotal);
            var fraction = (desiredTotal - usableWidth) / shrinkable;
            var widths = desired
                .Select((width, index) => width - ((width - minimum[index]) * fraction))
                .ToArray();
            return new TableColumnLayout(widths, false, usableWidth);
        }

        var result = desired.ToArray();
        var flexible = columns
            .Select((column, index) => (column, index))
            .Where(item => item.column.Flexible)
            .ToArray();
        if (flexible.Length > 0)
        {
            var totalWeight = flexible.Sum(item => Math.Max(0.01f, item.column.FlexWeight));
            var surplus = usableWidth - desiredTotal;
            foreach (var (column, index) in flexible)
                result[index] += surplus * (Math.Max(0.01f, column.FlexWeight) / totalWeight);
        }

        return new TableColumnLayout(result, false, usableWidth);
    }
}
