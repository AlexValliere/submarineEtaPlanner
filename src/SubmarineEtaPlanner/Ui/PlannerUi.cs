using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace SubmarineEtaPlanner.Ui;

internal static class PlannerUi
{
    internal static readonly Vector4 WindowBackground = PlannerTheme.Window;
    internal static readonly Vector4 SidebarBackground = PlannerTheme.Sidebar;
    internal static readonly Vector4 PanelBackground = PlannerTheme.Panel;
    internal static readonly Vector4 PanelBackgroundAlt = PlannerTheme.Input;
    internal static readonly Vector4 Border = PlannerTheme.Border;
    internal static readonly Vector4 Teal = PlannerTheme.Teal;
    internal static readonly Vector4 Cyan = PlannerTheme.Cyan;
    internal static readonly Vector4 Amber = PlannerTheme.Amber;
    internal static readonly Vector4 Green = PlannerTheme.Green;
    internal static readonly Vector4 Red = PlannerTheme.Red;
    internal static readonly Vector4 Muted = PlannerTheme.Muted;
    internal static bool DrawHeader(PlannerTypography typography, string title, string status, bool showRefresh, bool refreshing)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var buttonLabel = refreshing ? "Cancel" : "Refresh";
        var buttonWidth = showRefresh ? ImGui.CalcTextSize(buttonLabel).X + 50f * scale : 0;
        var padding = 12f * scale;
        var textWidth = Math.Max(1f, width - buttonWidth - padding * (showRefresh ? 3 : 2));
        float titleHeight;
        using (typography.Heading())
            titleHeight = ImGui.CalcTextSize(title, false, textWidth).Y;
        var topRowHeight = Math.Max(titleHeight, showRefresh ? ImGui.GetFrameHeight() : 0f);
        var statusHeight = ImGui.CalcTextSize(status, false, Math.Max(1f, width - padding * 2)).Y;
        var height = topRowHeight + statusHeight + padding * 2 + 4f * scale;
        var end = origin + new Vector2(width, height);
        ImGui.GetWindowDrawList().AddRectFilled(origin, end, ColorU32(PanelBackground), PlannerTheme.PanelRounding * scale);
        ImGui.SetCursorScreenPos(origin + new Vector2(padding, padding + (topRowHeight - titleHeight) / 2));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + textWidth);
        using (typography.Heading())
            ImGui.TextUnformatted(title);
        ImGui.PopTextWrapPos();
        var clicked = false;
        if (showRefresh)
        {
            ImGui.SetCursorScreenPos(new Vector2(end.X - buttonWidth - padding, origin.Y + padding));
            clicked = ImGuiComponents.IconButtonWithText(
                refreshing ? FontAwesomeIcon.Times : FontAwesomeIcon.SyncAlt,
                $"{buttonLabel}##header-refresh", Vector2.Zero);
        }
        ImGui.SetCursorScreenPos(origin + new Vector2(padding, padding + topRowHeight + 4f * scale));
        ImGui.PushStyleColor(ImGuiCol.Text, Muted);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Max(1f, width - padding * 2));
        ImGui.TextUnformatted(status);
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();
        ImGui.SetCursorScreenPos(new Vector2(origin.X, end.Y));
        ImGui.Dummy(Vector2.Zero);
        return clicked;
    }

    internal static void SameLineIfFits(string label, float extraWidth = 0)
    {
        var style = ImGui.GetStyle();
        var width = ImGui.CalcTextSize(label).X + style.FramePadding.X * 2 + extraWidth;
        var right = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X;
        if (ImGui.GetItemRectMax().X + style.ItemSpacing.X + width <= right)
            ImGui.SameLine();
    }

    internal static void WrappedText(string text, Vector4? color = null)
    {
        if (color is { } value) ImGui.PushStyleColor(ImGuiCol.Text, value);
        ImGui.PushTextWrapPos(0);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        if (color is not null) ImGui.PopStyleColor();
    }

    internal static void DrawBrandMark(bool compact)
    {
        const string label = "SUB ETA";
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = Math.Max(1f, ImGui.GetContentRegionAvail().X);
        var iconText = FontAwesomeIcon.Ship.ToIconString();
        Vector2 iconSize;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            iconSize = ImGui.CalcTextSize(iconText);

        var labelSize = ImGui.CalcTextSize(label);
        var rowSize = new Vector2(width, Math.Max(iconSize.Y, labelSize.Y));
        var iconPosition = SidebarIconPosition(origin, rowSize, iconSize, compact, scale);
        var drawList = ImGui.GetWindowDrawList();
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            drawList.AddText(iconPosition, ColorU32(Teal), iconText);

        if (!compact)
        {
            var labelPosition = new Vector2(
                origin.X + SidebarTextOffset(scale),
                origin.Y + ((rowSize.Y - labelSize.Y) / 2f));
            drawList.AddText(labelPosition, ColorU32(ImGui.GetStyle().Colors[(int)ImGuiCol.Text]), label);
        }

        ImGui.Dummy(rowSize);
        if (compact)
            Tooltip("Submarine ETA Planner");
        else
            ImGui.TextColored(Muted, "Command deck");
    }

    internal static bool NavigationButton(string id, FontAwesomeIcon icon, string label, bool compact, bool selected)
    {
        using var selection = PlannerTheme.Selection(selected);
        ImGui.PushStyleColor(ImGuiCol.Button, selected ? PlannerTheme.Selected : Vector4.Zero);

        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(Math.Max(1f, ImGui.GetContentRegionAvail().X), Math.Max(36f * scale, ImGui.GetFrameHeight()));
        var clicked = ImGui.Button($"##{id}", size);
        var rowMin = ImGui.GetItemRectMin();
        var rowMax = ImGui.GetItemRectMax();
        var rowSize = rowMax - rowMin;
        var iconText = icon.ToIconString();
        Vector2 iconSize;
        var drawList = ImGui.GetWindowDrawList();
        if (selected)
            drawList.AddRectFilled(rowMin + new Vector2(0, 8f * scale),
                new Vector2(rowMin.X + 3f * scale, rowMax.Y - 8f * scale), ColorU32(Teal), 1.5f * scale);
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            iconSize = ImGui.CalcTextSize(iconText);
            drawList.AddText(
                SidebarIconPosition(rowMin, rowSize, iconSize, compact, scale),
                ColorU32(selected ? Teal : Muted),
                iconText);
        }

        if (!compact)
        {
            var labelSize = ImGui.CalcTextSize(label);
            var labelPosition = new Vector2(
                rowMin.X + SidebarTextOffset(scale),
                rowMin.Y + ((rowSize.Y - labelSize.Y) / 2f));
            drawList.AddText(
                labelPosition,
                ColorU32(ImGui.GetStyle().Colors[(int)ImGuiCol.Text]),
                label);
        }

        if (compact)
            Tooltip(label);

        ImGui.PopStyleColor();
        return clicked;
    }

    private static Vector2 SidebarIconPosition(
        Vector2 rowMin,
        Vector2 rowSize,
        Vector2 iconSize,
        bool compact,
        float scale)
    {
        const float horizontalPadding = 8f;
        const float iconColumnWidth = 20f;
        var x = compact
            ? rowMin.X + ((rowSize.X - iconSize.X) / 2f)
            : rowMin.X + ((horizontalPadding + (iconColumnWidth / 2f)) * scale) - (iconSize.X / 2f);
        var y = rowMin.Y + ((rowSize.Y - iconSize.Y) / 2f);
        return new Vector2(x, y);
    }

    private static float SidebarTextOffset(float scale)
    {
        const float horizontalPadding = 8f;
        const float iconColumnWidth = 20f;
        const float iconLabelGap = 8f;
        return (horizontalPadding + iconColumnWidth + iconLabelGap) * scale;
    }

    internal static void SectionLabel(string label)
    {
        ImGui.TextColored(Muted, label);
        ImGui.Spacing();
    }

    internal static void MetricCard(PlannerTypography typography, string id, FontAwesomeIcon icon, string value, string label, Vector4 accent)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var style = ImGui.GetStyle();
        var width = Math.Max(1f, ImGui.GetContentRegionAvail().X - style.WindowPadding.X * 2);
        float iconWidth;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            iconWidth = ImGui.CalcTextSize(icon.ToIconString()).X;
        var valueWidth = Math.Max(1f, width - iconWidth - style.ItemSpacing.X);
        float valueHeight;
        using (typography.Value()) valueHeight = ImGui.CalcTextSize(value, false, valueWidth).Y;
        var height = Math.Max(78f * scale, style.WindowPadding.Y * 2 + Math.Max(valueHeight, ImGui.GetTextLineHeight())
            + style.ItemSpacing.Y + ImGui.CalcTextSize(label, false, width).Y);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, PanelBackground);
        ImGui.PushStyleColor(ImGuiCol.Border, Border);
        if (ImGui.BeginChild(id, new Vector2(-1, height), true, ImGuiWindowFlags.NoScrollbar))
        {
            var origin = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(origin + new Vector2(0f, Math.Max(0, (valueHeight - ImGui.GetTextLineHeight()) / 2)));
            Icon(icon, accent);
            ImGui.SetCursorScreenPos(origin + new Vector2(iconWidth + style.ItemSpacing.X, 0));
            using (typography.Value()) WrappedText(value);
            ImGui.SetCursorScreenPos(origin + new Vector2(0, Math.Max(valueHeight, ImGui.GetTextLineHeight()) + style.ItemSpacing.Y));
            WrappedText(label, Muted);
        }
        ImGui.EndChild();
        ImGui.PopStyleColor(2);
    }

    internal static void DrawStatusPill(string text, Vector4 color)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var position = ImGui.GetCursorScreenPos();
        var textSize = ImGui.CalcTextSize(text);
        var padding = new Vector2(8f, 3f) * scale;
        var size = textSize + padding * 2;
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(position, position + size, ColorU32(PlannerTheme.WithAlpha(color, 0.10f)), size.Y / 2f);
        drawList.AddRect(position, position + size, ColorU32(PlannerTheme.WithAlpha(color, 0.3f)), size.Y / 2f);
        drawList.AddText(position + padding, ColorU32(color), text);
        ImGui.Dummy(size);
    }

    internal static void DrawProgressBackground(
        Vector2 position,
        Vector2 size,
        float? fraction,
        Vector4 accent,
        Vector4? baseColor = null,
        float rounding = 0f)
    {
        if (size.X <= 0f || size.Y <= 0f)
            return;

        var drawList = ImGui.GetWindowDrawList();
        if (drawList.ClipRectStack.Size == 0)
            return;

        var end = position + size;
        var currentClip = drawList.ClipRectStack[drawList.ClipRectStack.Size - 1];
        var clippedTop = Math.Max(position.Y, currentClip.Y);
        var clippedBottom = Math.Min(end.Y, currentClip.W);
        if (clippedTop >= clippedBottom)
            return;

        drawList.PushClipRect(
            new Vector2(position.X, clippedTop),
            new Vector2(end.X, clippedBottom),
            true);
        if (baseColor is { } background)
            drawList.AddRectFilled(position, end, ColorU32(background), rounding);

        if (fraction is null || fraction.Value <= 0f)
        {
            drawList.PopClipRect();
            return;
        }

        var clamped = Math.Clamp(fraction.Value, 0f, 1f);
        var fillColor = baseColor is { } baseValue
            ? Vector4.Lerp(baseValue, accent, 0.065f)
            : PlannerTheme.WithAlpha(accent, 0.065f);
        var fillEnd = new Vector2(position.X + (size.X * clamped), end.Y);
        drawList.PushClipRect(position, fillEnd, true);
        drawList.AddRectFilled(position, end, ColorU32(fillColor), rounding);
        drawList.AddRectFilled(new Vector2(position.X, end.Y - 2f * ImGuiHelpers.GlobalScale), end,
            ColorU32(PlannerTheme.WithAlpha(accent, 0.55f)), rounding);
        drawList.PopClipRect();
        drawList.PopClipRect();
    }

    internal static void Callout(string id, FontAwesomeIcon icon, string title, string body, Vector4 accent)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var padding = ImGui.GetStyle().WindowPadding;
        var wrapWidth = Math.Max(1f, ImGui.GetContentRegionAvail().X - padding.X * 2);
        float iconWidth;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            iconWidth = ImGui.CalcTextSize(icon.ToIconString()).X;
        var titleHeight = ImGui.CalcTextSize(title, false, Math.Max(1f, wrapWidth - iconWidth - ImGui.GetStyle().ItemSpacing.X)).Y;
        var bodyHeight = ImGui.CalcTextSize(body, false, wrapWidth).Y;
        var height = Math.Max(68f * scale, padding.Y * 2 + titleHeight + ImGui.GetStyle().ItemSpacing.Y + bodyHeight);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Lerp(PanelBackground, accent, 0.035f));
        ImGui.PushStyleColor(ImGuiCol.Border, PlannerTheme.WithAlpha(accent, 0.3f));
        if (ImGui.BeginChild(id, new Vector2(-1, height), true, ImGuiWindowFlags.NoScrollbar))
        {
            Icon(icon, accent);
            ImGui.SameLine();
            WrappedText(title, accent);
            WrappedText(body);
        }
        ImGui.EndChild();
        ImGui.PopStyleColor(2);
    }

    internal static bool IconButton(string id, FontAwesomeIcon icon, string tooltip)
    {
        var clicked = ImGuiComponents.IconButton(id, icon);
        Tooltip(tooltip);
        return clicked;
    }

    internal static bool IconButtonWithText(string id, FontAwesomeIcon icon, string label)
        => ImGuiComponents.IconButtonWithText(icon, $"{label}##{id}", Vector2.Zero);

    internal static bool PrimaryIconButtonWithText(string id, FontAwesomeIcon icon, string label)
    {
        using var style = PlannerTheme.PrimaryButton();
        return IconButtonWithText(id, icon, label);
    }

    internal static void Icon(FontAwesomeIcon icon, Vector4 color)
    {
        using var font = Plugin.PluginInterface.UiBuilder.IconFontHandle.Push();
        ImGui.TextColored(color, icon.ToIconString());
    }

    internal static void IconText(FontAwesomeIcon icon, string text, Vector4 color)
    {
        Icon(icon, color);
        ImGui.SameLine();
        ImGui.TextColored(color, text);
    }

    internal static bool SegmentedButton(string id, string label, bool selected, string? emphasis = null)
    {
        using var selection = PlannerTheme.Selection(selected);
        if (emphasis is not null) ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
        var clicked = ImGui.Button($"{label}##{id}");
        if (emphasis is not null)
        {
            ImGui.PopStyleColor();
            var start = label.IndexOf(emphasis, StringComparison.Ordinal);
            var position = ImGui.GetItemRectMin() + ImGui.GetStyle().FramePadding;
            var draw = ImGui.GetWindowDrawList();
            var textColor = ImGui.GetColorU32(ImGuiCol.Text);
            if (start < 0)
                draw.AddText(position, textColor, label);
            else
            {
                var prefix = label[..start];
                draw.AddText(position, textColor, prefix);
                position.X += ImGui.CalcTextSize(prefix).X;
                draw.AddText(position, ImGui.GetColorU32(Teal), emphasis);
                position.X += ImGui.CalcTextSize(emphasis).X;
                draw.AddText(position, textColor, label[(start + emphasis.Length)..]);
            }
        }
        if (selected)
        {
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            ImGui.GetWindowDrawList().AddRect(min, max, ColorU32(PlannerTheme.WithAlpha(Teal, 0.65f)),
                PlannerTheme.ControlRounding * ImGuiHelpers.GlobalScale);
        }
        return clicked;
    }

    internal static bool PrimaryButton(string label)
    {
        using var style = PlannerTheme.PrimaryButton();
        return ImGui.Button(label);
    }

    internal static SettingRowScope SettingRow(string label, string help)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var width = Math.Max(1f, ImGui.GetContentRegionAvail().X - 12f * scale);
        var gap = 16f * scale;
        var beside = width >= 720f * scale;
        var labelWidth = beside ? (width - gap) * 0.46f : width;
        ImGui.BeginGroup();
        ImGui.BeginGroup();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + labelWidth);
        ImGui.TextUnformatted(label);
        ImGui.PushStyleColor(ImGuiCol.Text, Muted);
        ImGui.TextUnformatted(help);
        ImGui.PopStyleColor();
        ImGui.PopTextWrapPos();
        // Reserve a consistent label column even when the help text is short.
        ImGui.Dummy(new Vector2(labelWidth, 0));
        ImGui.EndGroup();
        if (beside) ImGui.SameLine(0, gap);
        ImGui.BeginGroup();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + (beside ? width - labelWidth - gap : width));
        return new SettingRowScope();
    }

    internal readonly struct SettingRowScope : IDisposable
    {
        public void Dispose()
        {
            ImGui.PopTextWrapPos();
            ImGui.EndGroup();
            ImGui.EndGroup();
            ImGui.Spacing();
        }
    }

    internal static ActionRowScope ActionRow(string label, string first, string second, string third)
    {
        var style = ImGui.GetStyle();
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var actionsWidth = ImGui.CalcTextSize(first).X + ImGui.CalcTextSize(second).X + ImGui.CalcTextSize(third).X
            + style.FramePadding.X * 6 + style.ItemSpacing.X * 2;
        var labelWidth = width - actionsWidth - 16f * scale;
        var beside = labelWidth >= 160f * scale;
        ImGui.BeginGroup();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Max(1f, beside ? labelWidth : width));
        ImGui.TextUnformatted(label);
        ImGui.PopTextWrapPos();
        if (beside) ImGui.SetCursorScreenPos(new Vector2(origin.X + width - actionsWidth, origin.Y));
        ImGui.BeginGroup();
        return new ActionRowScope();
    }

    internal readonly struct ActionRowScope : IDisposable
    {
        public void Dispose()
        {
            ImGui.EndGroup();
            ImGui.EndGroup();
        }
    }

    internal static void BeginTooltip(float? preferredWidth = null)
    {
        var viewportWidth = ImGui.GetWindowViewport().WorkSize.X;
        var width = Math.Min(preferredWidth ?? 380f * ImGuiHelpers.GlobalScale,
            Math.Max(1f, viewportWidth - 24f * ImGuiHelpers.GlobalScale));
        ImGui.SetNextWindowSizeConstraints(new(width, 0f), new(width, float.MaxValue));
        ImGui.PushStyleColor(ImGuiCol.Text, PlannerTheme.Text);
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(0);
    }

    internal static void EndTooltip()
    {
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
        ImGui.PopStyleColor();
    }

    internal static void Tooltip(string text)
    {
        if (!ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) return;
        var preferredWidth = Math.Min(380f * ImGuiHelpers.GlobalScale,
            ImGui.CalcTextSize(text).X + ImGui.GetStyle().WindowPadding.X * 2);
        BeginTooltip(preferredWidth);
        WrappedText(text);
        EndTooltip();
    }

    private static uint ColorU32(Vector4 color) => ImGui.ColorConvertFloat4ToU32(color);

}
