using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using SubmarineEtaPlanner.Planner;
using System.Numerics;

namespace SubmarineEtaPlanner.Ui;

public sealed partial class PlannerWindow
{
    private readonly FcNavigationGuard<PlannerPage> fcNavigation = new();
    private FcNavigationRequest<PlannerPage>? requestedNavigation;
    private bool openNavigationDialog;
    private bool focusSetupFuel;
    private string? incomeFcScope;
    private string? expandIncomeFc;

    private void RequestFcNavigation(string fcId, PlannerPage page, bool fuel = false)
    {
        var request = new FcNavigationRequest<PlannerPage>(fcId, page, fuel);
        if (this.fcNavigation.Request(request, this.selectedSetupFcId, this.setupDraftDirty, page == PlannerPage.FcSetup))
            this.requestedNavigation = request;
        else
            this.openNavigationDialog = true;
    }

    // Draw at the window level, outside page child windows, so requests from every page share a popup ID.
    private void DrawNavigationDialog()
    {
        if (this.openNavigationDialog)
        {
            ImGui.OpenPopup("Unsaved FC changes###fc-navigation");
            this.openNavigationDialog = false;
        }
        ImGui.SetNextWindowSize(new Vector2(440f * ImGuiHelpers.GlobalScale, 0), ImGuiCond.Appearing);
        if (ImGui.BeginPopupModal("Unsaved FC changes###fc-navigation", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextWrapped("This opens another FC's setup. Save or discard the current FC's staged changes first.");
            ImGui.Spacing();
            foreach (var (choice, label) in new[]
            {
                (DraftNavigationChoice.Save, "Save changes"),
                (DraftNavigationChoice.Discard, "Discard changes"),
                (DraftNavigationChoice.Cancel, "Cancel"),
            })
            {
                if (choice != DraftNavigationChoice.Save) PlannerUi.SameLineIfFits(label);
                if (!ImGui.Button(label)) continue;
                this.requestedNavigation = this.fcNavigation.Resolve(choice, SaveSetupDraft);
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        if (this.requestedNavigation is not { } request) return;
        this.requestedNavigation = null;
        if (this.snapshot?.FreeCompanies.Any(fc => fc.FcIdKey == request.FcId) != true) return;
        switch (request.Destination)
        {
            case PlannerPage.FcSetup:
                if (this.selectedSetupFcId != request.FcId) SelectSetupFc(request.FcId);
                this.focusSetupFuel = request.FocusFuel;
                break;
            case PlannerPage.Unlocks:
                if (this.selectedUnlockFcId != request.FcId)
                {
                    this.selectedUnlockFcId = request.FcId;
                    this.selectedUnlockMapId = null;
                    this.selectedUnlockSectorId = null;
                    this.unlockSearch = string.Empty;
                }
                break;
            case PlannerPage.Income:
                this.incomeFcScope = request.FcId;
                this.expandIncomeFc = request.FcId;
                break;
        }
        this.currentPage = request.Destination;
    }

    private void DrawFcShortcuts(string fcId)
    {
        ImGui.PushID($"shortcuts-{fcId}");
        var first = true;
        foreach (var (page, label) in new[]
        {
            (PlannerPage.FcSetup, "Setup"), (PlannerPage.Unlocks, "Unlock map"), (PlannerPage.Income, "Income"),
        })
        {
            if (this.currentPage == page) continue;
            if (!first) PlannerUi.SameLineIfFits(label);
            if (ImGui.SmallButton(label)) RequestFcNavigation(fcId, page);
            first = false;
        }
        ImGui.PopID();
    }

    private static float FavoriteControlWidth => ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.X;

    private void DrawFavoriteControl(string fcId)
    {
        var preference = this.configuration.GetFcPreferences(fcId);
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.Text, preference.Favorite ? PlannerUi.Teal : PlannerUi.Muted);
        var size = ImGui.GetFrameHeight();
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            if (ImGui.Button($"{FontAwesomeIcon.Star.ToIconString()}##favorite-{this.currentPage}-{fcId}",
                    new Vector2(size, size)))
            {
                preference.Favorite = !preference.Favorite;
                this.saveConfiguration();
            }
        }
        PlannerUi.Tooltip(preference.Favorite ? "Unpin FC · saved automatically" : "Pin FC · saved automatically");
        ImGui.PopStyleColor(2);
        ImGui.SameLine();
    }

    private static float GetSaveBarHeight(bool fc)
    {
        var style = ImGui.GetStyle();
        var width = Math.Max(1f, ImGui.GetContentRegionAvail().X - style.WindowPadding.X * 2);
        var labels = new[] { "Save changes", "Discard changes", fc ? "Use global target and strategy" : "Reset defaults" };
        var used = 0f;
        var rows = 1;
        foreach (var label in labels)
        {
            var item = ImGui.CalcTextSize(label).X + style.FramePadding.X * 2;
            if (used > 0 && used + style.ItemSpacing.X + item > width) { rows++; used = 0; }
            used += item + (used > 0 ? style.ItemSpacing.X : 0);
        }
        var hint = fc ? "Saving target, strategy or assignment changes recalculates affected forecasts."
            : "Saving global calculation settings refreshes forecasts.";
        return style.WindowPadding.Y * 2 + ImGui.GetTextLineHeight() +
            rows * ImGui.GetFrameHeightWithSpacing() + ImGui.CalcTextSize(hint, false, width).Y +
            style.ItemSpacing.Y * 2 + 8f * ImGuiHelpers.GlobalScale;
    }

    private string GetForecastStatus()
    {
        if (!this.getSubmarineTrackerState().IsAvailable) return "Submarine Tracker unavailable";
        if (!string.IsNullOrWhiteSpace(this.lastError)) return "Calculation notice · see details below";
        if (this.snapshot is not { } current) return "Loading fleet data…";
        if (this.refreshTask is { IsCompleted: false })
        {
            var done = current.FcProgress.Count(fc => fc.Status is not (FcCalculationStatus.Queued or FcCalculationStatus.Calculating));
            return $"Calculating · {done}/{current.FreeCompanies.Count} FCs processed";
        }
        if (this.trackerDataChanged) return "New tracker data available · refresh to update";
        var waiting = current.FcProgress.Count(fc => fc.Status == FcCalculationStatus.AwaitingTrackerUpdate);
        if (waiting > 0) return $"Waiting for SubmarineTracker · {waiting} FCs";
        var age = DateTimeOffset.UtcNow - current.GeneratedAtUtc;
        return $"{(current.IsComplete ? "Forecast snapshot" : "Partial forecast")} · {FormatDuration(age < TimeSpan.Zero ? TimeSpan.Zero : age)} ago";
    }
}
