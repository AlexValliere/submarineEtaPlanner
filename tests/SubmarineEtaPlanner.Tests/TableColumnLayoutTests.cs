using SubmarineEtaPlanner.Planner;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class TableColumnLayoutTests
{
    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void IncomeTableScrollsWhenContentFitsButCellPaddingDoesNot(float scale)
    {
        float[] minimumWidths = [120, 68, 72, 105, 125, 82, 105, 125, 125];
        var columns = minimumWidths.Select(width =>
            new TableColumnMeasurement(width * scale, width * scale, width * scale)).ToArray();
        var overhead = (9 * 14 + 10) * scale;

        var layout = TableColumnLayoutAllocator.Allocate(columns, 936 * scale, overhead);

        Assert.True(layout.RequiresHorizontalScroll);
        Assert.Equal(120 * scale, layout.Widths[0]);
        Assert.Equal(minimumWidths.Sum() * scale + overhead, layout.InnerWidth, 3);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void PaddingIsReservedBeforeDistributingRemainingWidth(float scale)
    {
        var layout = TableColumnLayoutAllocator.Allocate(
            [
                new TableColumnMeasurement(120 * scale, 120 * scale, 220 * scale, Flexible: true),
                new TableColumnMeasurement(100 * scale, 80 * scale, 140 * scale),
            ],
            300 * scale,
            30 * scale);

        Assert.False(layout.RequiresHorizontalScroll);
        Assert.Equal(170 * scale, layout.Widths[0], 3);
        Assert.Equal(100 * scale, layout.Widths[1], 3);
        Assert.Equal(300 * scale, layout.Widths.Sum() + 30 * scale, 3);
        Assert.Equal(300 * scale, layout.InnerWidth, 3);
    }

    [Fact]
    public void WideViewportDistributesSurplusAcrossFlexibleColumns()
    {
        var layout = TableColumnLayoutAllocator.Allocate(
            [
                new TableColumnMeasurement(100, 80, 140),
                new TableColumnMeasurement(200, 120, 300, Flexible: true, FlexWeight: 2),
                new TableColumnMeasurement(100, 80, 140, Flexible: true),
            ],
            700);

        Assert.False(layout.RequiresHorizontalScroll);
        Assert.Equal(100f, layout.Widths[0], 3);
        Assert.Equal(400f, layout.Widths[1], 3);
        Assert.Equal(200f, layout.Widths[2], 3);
    }

    [Fact]
    public void MediumViewportShrinksTowardSemanticMinimums()
    {
        var layout = TableColumnLayoutAllocator.Allocate(
            [
                new TableColumnMeasurement(180, 100, 240),
                new TableColumnMeasurement(220, 120, 300),
            ],
            300);

        Assert.False(layout.RequiresHorizontalScroll);
        Assert.Equal(135.556f, layout.Widths[0], 3);
        Assert.Equal(164.444f, layout.Widths[1], 3);
    }

    [Fact]
    public void NarrowViewportUsesDesiredWidthsAndHorizontalScroll()
    {
        var layout = TableColumnLayoutAllocator.Allocate(
            [
                new TableColumnMeasurement(180, 120, 240),
                new TableColumnMeasurement(220, 140, 300),
            ],
            240);

        Assert.True(layout.RequiresHorizontalScroll);
        Assert.Equal([180f, 220f], layout.Widths);
        Assert.Equal(400f, layout.InnerWidth, 3);
    }

    [Fact]
    public void MeasurementsAreClampedAndScaleIndependently()
    {
        const float scale = 1.5f;
        var layout = TableColumnLayoutAllocator.Allocate(
            [
                new TableColumnMeasurement(40 * scale, 80 * scale, 120 * scale),
                new TableColumnMeasurement(500 * scale, 100 * scale, 180 * scale),
            ],
            300 * scale);

        Assert.False(layout.RequiresHorizontalScroll);
        Assert.Equal(120f, layout.Widths[0], 3);
        Assert.Equal(270f, layout.Widths[1], 3);
    }
}
