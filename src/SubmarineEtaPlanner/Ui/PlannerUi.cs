using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace SubmarineEtaPlanner.Ui;

internal static class PlannerUi
{
    internal static readonly Vector4 WindowBackground = new(0.035f, 0.065f, 0.090f, 0.97f);
    internal static readonly Vector4 SidebarBackground = new(0.025f, 0.050f, 0.072f, 0.98f);
    internal static readonly Vector4 PanelBackground = new(0.055f, 0.100f, 0.130f, 0.96f);
    internal static readonly Vector4 PanelBackgroundAlt = new(0.065f, 0.125f, 0.155f, 0.96f);
    internal static readonly Vector4 Border = new(0.16f, 0.23f, 0.27f, 0.60f);
    internal static readonly Vector4 Teal = new(0.18f, 0.78f, 0.78f, 1f);
    internal static readonly Vector4 Cyan = new(0.25f, 0.70f, 0.92f, 1f);
    internal static readonly Vector4 Amber = new(0.96f, 0.65f, 0.22f, 1f);
    internal static readonly Vector4 Green = new(0.30f, 0.82f, 0.58f, 1f);
    internal static readonly Vector4 Red = new(0.96f, 0.34f, 0.34f, 1f);
    internal static readonly Vector4 Muted = new(0.62f, 0.72f, 0.76f, 1f);

    internal static ThemeScope PushTheme()
    {
        ImGui.PushStyleColor(ImGuiCol.WindowBg, WindowBackground);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.035f, 0.070f, 0.095f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Border, Border);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.065f, 0.13f, 0.16f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.09f, 0.20f, 0.23f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.10f, 0.25f, 0.28f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.08f, 0.20f, 0.23f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.10f, 0.34f, 0.36f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.10f, 0.43f, 0.43f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.07f, 0.20f, 0.23f, 1f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.09f, 0.31f, 0.34f, 1f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.10f, 0.39f, 0.39f, 1f));
        ImGui.PushStyleColor(ImGuiCol.CheckMark, Teal);
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, Cyan);
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, Teal);
        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, new Vector4(0.075f, 0.17f, 0.20f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, new Vector4(0.10f, 0.22f, 0.24f, 0.22f));
        ImGui.PushStyleColor(ImGuiCol.Separator, Border);
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, Muted);

        var scale = ImGuiHelpers.GlobalScale;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8f, 8f) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(7f, 5f) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(7f, 4f) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 8f) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 7f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 5f * scale);
        return new ThemeScope(20, 7);
    }

    internal static bool DrawHeader(string title, string status, bool showRefresh, bool refreshing)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var buttonLabel = refreshing ? "Cancel" : "Refresh";
        var buttonWidth = showRefresh ? ImGui.CalcTextSize(buttonLabel).X + 50f * scale : 0;
        var textWidth = Math.Max(1f, width - buttonWidth - 24f * scale);
        var titleHeight = ImGui.CalcTextSize(title, false, textWidth).Y;
        var statusHeight = ImGui.CalcTextSize(status, false, Math.Max(1f, width - 16f * scale)).Y;
        var height = Math.Max(52f * scale, titleHeight + statusHeight + 20f * scale);
        var end = origin + new Vector2(width, height);
        ImGui.GetWindowDrawList().AddRectFilled(origin, end, ColorU32(PanelBackground), 5f * scale);
        ImGui.SetCursorScreenPos(origin + new Vector2(8f, 7f) * scale);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + textWidth);
        ImGui.TextUnformatted(title);
        ImGui.PopTextWrapPos();
        var clicked = false;
        if (showRefresh)
        {
            ImGui.SetCursorScreenPos(new Vector2(end.X - buttonWidth, origin.Y + 3f * scale));
            clicked = ImGuiComponents.IconButtonWithText(
                refreshing ? FontAwesomeIcon.Times : FontAwesomeIcon.SyncAlt,
                $"{buttonLabel}##header-refresh", Vector2.Zero);
        }
        ImGui.SetCursorScreenPos(origin + new Vector2(8f * scale, titleHeight + 12f * scale));
        ImGui.PushStyleColor(ImGuiCol.Text, Muted);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Max(1f, width - 16f * scale));
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
        if (selected)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.08f, 0.38f, 0.39f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.10f, 0.45f, 0.45f, 1f));
        }

        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(Math.Max(1f, ImGui.GetContentRegionAvail().X), 36f * scale);
        var clicked = ImGui.Button($"##{id}", size);
        var rowMin = ImGui.GetItemRectMin();
        var rowMax = ImGui.GetItemRectMax();
        var rowSize = rowMax - rowMin;
        var iconText = icon.ToIconString();
        Vector2 iconSize;
        var drawList = ImGui.GetWindowDrawList();
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            iconSize = ImGui.CalcTextSize(iconText);
            drawList.AddText(
                SidebarIconPosition(rowMin, rowSize, iconSize, compact, scale),
                ColorU32(ImGui.GetStyle().Colors[(int)ImGuiCol.Text]),
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

        if (selected)
            ImGui.PopStyleColor(2);
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

    internal static void MetricCard(string id, FontAwesomeIcon icon, string value, string label, Vector4 accent)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, PanelBackground);
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(accent.X, accent.Y, accent.Z, 0.55f));
        if (ImGui.BeginChild(id, new Vector2(-1, 78f * ImGuiHelpers.GlobalScale), true, ImGuiWindowFlags.NoScrollbar))
        {
            Icon(icon, accent);
            ImGui.SameLine();
            ImGui.TextUnformatted(value);
            ImGui.TextColored(Muted, label);
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
        drawList.AddRectFilled(position, position + size, ColorU32(new Vector4(color.X, color.Y, color.Z, 0.18f)), size.Y / 2f);
        drawList.AddRect(position, position + size, ColorU32(new Vector4(color.X, color.Y, color.Z, 0.7f)), size.Y / 2f);
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
            false);
        if (baseColor is { } background)
            drawList.AddRectFilled(position, end, ColorU32(background), rounding);

        if (fraction is null || fraction.Value <= 0f)
        {
            drawList.PopClipRect();
            return;
        }

        var clamped = Math.Clamp(fraction.Value, 0f, 1f);
        var fillColor = baseColor is { } baseValue
            ? Vector4.Lerp(baseValue, accent, accent == Amber ? 0.14f : 0.10f)
            : new Vector4(accent.X, accent.Y, accent.Z, accent == Amber ? 0.16f : 0.12f);
        var fillEnd = new Vector2(position.X + (size.X * clamped), end.Y);
        drawList.PushClipRect(position, fillEnd, true);
        drawList.AddRectFilled(position, end, ColorU32(fillColor), rounding);
        drawList.PopClipRect();
        drawList.PopClipRect();
    }

    internal static void Callout(string id, FontAwesomeIcon icon, string title, string body, Vector4 accent)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var wrapWidth = Math.Max(140f * scale, ImGui.GetContentRegionAvail().X - 28f * scale);
        var bodyHeight = ImGui.CalcTextSize(body, false, wrapWidth).Y;
        var height = Math.Max(68f * scale, bodyHeight + 43f * scale);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(accent.X, accent.Y, accent.Z, 0.09f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(accent.X, accent.Y, accent.Z, 0.65f));
        if (ImGui.BeginChild(id, new Vector2(-1, height), true, ImGuiWindowFlags.NoScrollbar))
        {
            IconText(icon, title, accent);
            ImGui.TextWrapped(body);
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

    internal static bool SegmentedButton(string id, string label, bool selected)
    {
        if (selected)
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.08f, 0.42f, 0.43f, 1f));
        var clicked = ImGui.Button($"{label}##{id}");
        if (selected)
            ImGui.PopStyleColor();
        return clicked;
    }

    internal static void Tooltip(string text)
    {
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(text);
    }

    private static uint ColorU32(Vector4 color) => ImGui.ColorConvertFloat4ToU32(color);

    internal readonly struct ThemeScope(int colorCount, int styleVarCount) : IDisposable
    {
        public void Dispose()
        {
            ImGui.PopStyleVar(styleVarCount);
            ImGui.PopStyleColor(colorCount);
        }
    }
}
