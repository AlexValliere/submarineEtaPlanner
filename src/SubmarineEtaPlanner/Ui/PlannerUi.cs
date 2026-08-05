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
    internal static readonly Vector4 Border = new(0.12f, 0.28f, 0.32f, 0.90f);
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
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(7f, 6f) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(7f, 7f) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 7f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 5f * scale);
        return new ThemeScope(20, 7);
    }

    internal static bool DrawHeader(
        string title,
        string subtitle,
        int targetRank,
        string etaModel,
        bool showRefresh,
        bool refreshing)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var size = new Vector2(ImGui.GetContentRegionAvail().X, 96f * scale);
        var drawList = ImGui.GetWindowDrawList();
        var end = origin + size;
        drawList.AddRectFilledMultiColor(
            origin,
            end,
            ColorU32(new Vector4(0.035f, 0.16f, 0.20f, 1f)),
            ColorU32(new Vector4(0.035f, 0.28f, 0.30f, 1f)),
            ColorU32(new Vector4(0.025f, 0.10f, 0.15f, 1f)),
            ColorU32(new Vector4(0.025f, 0.07f, 0.11f, 1f)));
        drawList.AddRect(origin, end, ColorU32(Border), 8f * scale, ImDrawFlags.None, 1.5f * scale);
        drawList.AddCircle(end - new Vector2(35f, 28f) * scale, 44f * scale, ColorU32(new Vector4(Teal.X, Teal.Y, Teal.Z, 0.14f)), 48, 1.5f * scale);
        drawList.AddCircle(end - new Vector2(35f, 28f) * scale, 25f * scale, ColorU32(new Vector4(Cyan.X, Cyan.Y, Cyan.Z, 0.12f)), 40, 1f * scale);

        ImGui.SetCursorScreenPos(origin + new Vector2(18f, 14f) * scale);
        IconText(FontAwesomeIcon.Ship, title, Teal);
        ImGui.SetCursorScreenPos(origin + new Vector2(18f, 41f) * scale);
        ImGui.TextColored(Muted, subtitle);
        ImGui.SetCursorScreenPos(origin + new Vector2(18f, 67f) * scale);
        DrawStatusPill($"Target {targetRank}", Cyan);
        ImGui.SameLine();
        DrawStatusPill(etaModel, Teal);

        var clicked = false;
        if (showRefresh)
        {
            var buttonIcon = refreshing ? FontAwesomeIcon.Times : FontAwesomeIcon.SyncAlt;
            var buttonLabel = refreshing ? "Cancel" : "Refresh";
            var buttonSize = ImGui.CalcTextSize(buttonLabel) + new Vector2(22f, 12f) * scale;
            ImGui.SetCursorScreenPos(new Vector2(end.X - buttonSize.X - 18f * scale, origin.Y + 18f * scale));
            clicked = ImGuiComponents.IconButtonWithText(buttonIcon, $"{buttonLabel}##header-refresh", buttonSize);
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, end.Y));
        ImGui.Dummy(Vector2.Zero);
        return clicked;
    }

    internal static void DrawBrandMark(bool compact)
    {
        Icon(FontAwesomeIcon.Ship, Teal);
        if (!compact)
        {
            ImGui.SameLine();
            ImGui.TextUnformatted("SUB ETA");
            ImGui.TextColored(Muted, "Command deck");
        }
        else
        {
            Tooltip("Submarine ETA Planner");
        }
    }

    internal static bool NavigationButton(string id, FontAwesomeIcon icon, string label, bool compact, bool selected)
    {
        if (selected)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.08f, 0.38f, 0.39f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.10f, 0.45f, 0.45f, 1f));
        }

        var size = new Vector2(-1, 36f * ImGuiHelpers.GlobalScale);
        var clicked = compact
            ? ImGuiComponents.IconButton(id, icon, size)
            : ImGuiComponents.IconButtonWithText(icon, $"{label}##{id}", size);
        if (compact)
            Tooltip(label);

        if (selected)
            ImGui.PopStyleColor(2);
        return clicked;
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
        var end = position + size;
        if (baseColor is { } background)
            drawList.AddRectFilled(position, end, ColorU32(background), rounding);

        if (fraction is null || fraction.Value <= 0f)
            return;

        var clamped = Math.Clamp(fraction.Value, 0f, 1f);
        var fillColor = baseColor is { } baseValue
            ? Vector4.Lerp(baseValue, accent, accent == Amber ? 0.24f : 0.18f)
            : new Vector4(accent.X, accent.Y, accent.Z, accent == Amber ? 0.16f : 0.12f);
        var fillEnd = new Vector2(position.X + (size.X * clamped), end.Y);
        drawList.PushClipRect(position, fillEnd, false);
        drawList.AddRectFilled(position, end, ColorU32(fillColor), rounding);
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
