using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace SubmarineEtaPlanner.Ui;

/// <summary>The plugin's presentation only; never modifies the user's Dalamud style.</summary>
internal static class PlannerTheme
{
    internal static readonly Vector4 Window = Rgb(0x12171B, 0.98f);
    internal static readonly Vector4 Sidebar = Rgb(0x0E1317);
    internal static readonly Vector4 Panel = Rgb(0x1A2228);
    internal static readonly Vector4 Input = Rgb(0x202A31);
    internal static readonly Vector4 Hover = Rgb(0x2B3941);
    internal static readonly Vector4 Selected = Rgb(0x24433F);
    internal static readonly Vector4 SelectedHover = Rgb(0x30574F);
    internal static readonly Vector4 Text = Rgb(0xE7EEF2);
    internal static readonly Vector4 Muted = Rgb(0xA4B5BF);
    internal static readonly Vector4 Border = Rgb(0x303D45);
    internal static readonly Vector4 Teal = Rgb(0x66D9CA);
    internal static readonly Vector4 Cyan = Rgb(0x73CCEB);
    internal static readonly Vector4 Amber = Rgb(0xEFBD6D);
    internal static readonly Vector4 Green = Rgb(0x8DD6AA);
    internal static readonly Vector4 Red = Rgb(0xF49196);

    internal const float ControlRounding = 5f;
    internal const float PanelRounding = 7f;

    // Built once. The scope counts come from these definitions, not a second list to maintain.
    private static readonly (ImGuiCol Target, Vector4 Value)[] Colors =
    [
        (ImGuiCol.Text, Text), (ImGuiCol.TextDisabled, Muted),
        (ImGuiCol.WindowBg, Window), (ImGuiCol.ChildBg, Vector4.Zero),
        (ImGuiCol.PopupBg, Panel), (ImGuiCol.Border, Border), (ImGuiCol.BorderShadow, Vector4.Zero),
        (ImGuiCol.TitleBg, Sidebar), (ImGuiCol.TitleBgActive, Panel), (ImGuiCol.TitleBgCollapsed, Sidebar),
        (ImGuiCol.MenuBarBg, Panel),
        (ImGuiCol.FrameBg, Input), (ImGuiCol.FrameBgHovered, Hover), (ImGuiCol.FrameBgActive, Selected),
        (ImGuiCol.Button, Input), (ImGuiCol.ButtonHovered, Hover), (ImGuiCol.ButtonActive, SelectedHover),
        (ImGuiCol.Header, Input), (ImGuiCol.HeaderHovered, Hover), (ImGuiCol.HeaderActive, Selected),
        (ImGuiCol.CheckMark, Teal), (ImGuiCol.SliderGrab, Muted), (ImGuiCol.SliderGrabActive, Teal),
        (ImGuiCol.ScrollbarBg, Vector4.Zero), (ImGuiCol.ScrollbarGrab, Border),
        (ImGuiCol.ScrollbarGrabHovered, Muted), (ImGuiCol.ScrollbarGrabActive, Teal),
        (ImGuiCol.Separator, Border), (ImGuiCol.SeparatorHovered, Muted), (ImGuiCol.SeparatorActive, Teal),
        (ImGuiCol.ResizeGrip, Border), (ImGuiCol.ResizeGripHovered, Muted), (ImGuiCol.ResizeGripActive, Teal),
        (ImGuiCol.Tab, Input), (ImGuiCol.TabHovered, SelectedHover), (ImGuiCol.TabActive, Selected),
        (ImGuiCol.TabUnfocused, Panel), (ImGuiCol.TabUnfocusedActive, Selected),
        (ImGuiCol.TableHeaderBg, Input), (ImGuiCol.TableBorderStrong, Border),
        (ImGuiCol.TableBorderLight, WithAlpha(Border, 0.6f)),
        (ImGuiCol.TableRowBg, Vector4.Zero), (ImGuiCol.TableRowBgAlt, WithAlpha(Text, 0.025f)),
        (ImGuiCol.TextSelectedBg, WithAlpha(Teal, 0.28f)), (ImGuiCol.DragDropTarget, Teal),
        (ImGuiCol.NavHighlight, Teal), (ImGuiCol.NavWindowingHighlight, WithAlpha(Teal, 0.6f)),
        (ImGuiCol.NavWindowingDimBg, WithAlpha(Sidebar, 0.6f)), (ImGuiCol.ModalWindowDimBg, WithAlpha(Sidebar, 0.65f)),
        (ImGuiCol.PlotLines, Cyan), (ImGuiCol.PlotLinesHovered, Teal),
        (ImGuiCol.PlotHistogram, Green), (ImGuiCol.PlotHistogramHovered, Teal),
    ];

    private static readonly (ImGuiStyleVar Target, Vector2 Value)[] Spacing =
    [
        (ImGuiStyleVar.WindowPadding, new(8f, 8f)),
        (ImGuiStyleVar.FramePadding, new(7f, 5f)),
        // Keep in step with the existing responsive table overhead calculation.
        (ImGuiStyleVar.CellPadding, new(7f, 4f)),
        (ImGuiStyleVar.ItemSpacing, new(8f, 8f)),
        (ImGuiStyleVar.ItemInnerSpacing, new(4f, 4f)),
    ];

    private static readonly (ImGuiStyleVar Target, float Value)[] Geometry =
    [
        (ImGuiStyleVar.WindowRounding, PanelRounding), (ImGuiStyleVar.ChildRounding, PanelRounding),
        (ImGuiStyleVar.PopupRounding, PanelRounding), (ImGuiStyleVar.FrameRounding, ControlRounding),
        (ImGuiStyleVar.ScrollbarRounding, ControlRounding), (ImGuiStyleVar.GrabRounding, ControlRounding),
        (ImGuiStyleVar.TabRounding, ControlRounding),
        (ImGuiStyleVar.WindowBorderSize, 1f), (ImGuiStyleVar.ChildBorderSize, 1f),
        (ImGuiStyleVar.PopupBorderSize, 1f), (ImGuiStyleVar.FrameBorderSize, 0f),
    ];

    internal static Scope Push()
    {
        foreach (var (target, color) in Colors) ImGui.PushStyleColor(target, color);
        var scale = ImGuiHelpers.GlobalScale;
        foreach (var (target, value) in Spacing) ImGui.PushStyleVar(target, value * scale);
        foreach (var (target, value) in Geometry) ImGui.PushStyleVar(target, value * scale);
        return new Scope(Colors.Length, Spacing.Length + Geometry.Length);
    }

    internal static Scope PrimaryButton()
    {
        ImGui.PushStyleColor(ImGuiCol.Button, Teal);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Rgb(0x91E7DC));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Rgb(0x50BEB0));
        ImGui.PushStyleColor(ImGuiCol.Text, Sidebar);
        return new Scope(4, 0);
    }

    internal static Scope Selection(bool selected)
    {
        if (!selected) return new Scope(0, 0);
        ImGui.PushStyleColor(ImGuiCol.Button, Selected);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, SelectedHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, SelectedHover);
        ImGui.PushStyleColor(ImGuiCol.Text, Teal);
        return new Scope(4, 0);
    }

    internal static Vector4 WithAlpha(Vector4 color, float alpha) => new(color.X, color.Y, color.Z, alpha);

    private static Vector4 Rgb(uint rgb, float alpha = 1f)
        => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, alpha);

    internal readonly struct Scope(int colors, int variables) : IDisposable
    {
        public void Dispose()
        {
            if (variables > 0) ImGui.PopStyleVar(variables);
            if (colors > 0) ImGui.PopStyleColor(colors);
        }
    }
}
