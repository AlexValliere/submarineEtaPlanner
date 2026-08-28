using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using SubmarineEtaPlanner.Planner;

namespace SubmarineEtaPlanner.Ui;

public sealed partial class PlannerWindow
{
    private sealed record ResponsiveTableColumn(
        string Label,
        IEnumerable<string> Values,
        float MinimumWidth,
        float MaximumWidth,
        bool Flexible = false,
        float FlexWeight = 1f,
        bool FillRemaining = false);

    private sealed record ResponsiveTableLayout(
        IReadOnlyList<ResponsiveTableColumn> Columns,
        IReadOnlyList<float> Widths,
        bool RequiresHorizontalScroll,
        float InnerWidth);

    private static ResponsiveTableLayout CalculateResponsiveTableLayout(
        float availableWidth,
        params ResponsiveTableColumn[] columns)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var horizontalPadding = (ImGui.GetStyle().CellPadding.X * 2f) + (12f * scale);
        var measurements = columns.Select(column =>
        {
            var widest = column.Values
                .Append(column.Label)
                .DefaultIfEmpty(string.Empty)
                .Max(value => ImGui.CalcTextSize(value ?? string.Empty).X);
            return new TableColumnMeasurement(
                widest + horizontalPadding,
                column.MinimumWidth * scale,
                column.MaximumWidth * scale,
                column.Flexible,
                column.FlexWeight);
        }).ToArray();
        var borderAllowance = Math.Max(0, columns.Length - 1) * scale;
        var allocation = TableColumnLayoutAllocator.Allocate(
            measurements,
            Math.Max(1f, availableWidth - borderAllowance));
        return new ResponsiveTableLayout(
            columns,
            allocation.Widths,
            allocation.RequiresHorizontalScroll,
            allocation.InnerWidth + borderAllowance);
    }

    private static void SetupResponsiveTableColumns(ResponsiveTableLayout layout)
    {
        for (var index = 0; index < layout.Columns.Count; index++)
        {
            var column = layout.Columns[index];
            var fillRemaining = column.FillRemaining && !layout.RequiresHorizontalScroll;
            ImGui.TableSetupColumn(
                column.Label,
                fillRemaining ? ImGuiTableColumnFlags.WidthStretch : ImGuiTableColumnFlags.WidthFixed,
                fillRemaining ? Math.Max(0.01f, column.FlexWeight) : layout.Widths[index]);
        }
    }
}
