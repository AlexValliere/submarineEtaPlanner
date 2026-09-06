using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using SubmarineEtaPlanner.Planner;
using System.Numerics;

namespace SubmarineEtaPlanner.Ui;

public sealed partial class PlannerWindow
{
    private string? selectedSetupFcId;
    private bool setupDraftDirty;
    private bool setupUseGlobalTarget = true;
    private int setupTargetRank;
    private FcStrategyPreset? setupStrategy;
    private FuelStockMode setupFuelStockMode;
    private ulong? setupFuelHolderCharacterId;
    private int setupManualCeruleumTanks;
    private bool setupAutomaticReserve = true;
    private int setupFixedReserve;
    private ulong? pendingForgottenFuelCharacterId;
    private readonly Dictionary<long, SubmarineSetupDraft> setupSubmarineDrafts = [];
    private long? setupRoutePickerSubmarineId;
    private readonly List<uint> setupRoutePickerRoute = [];
    private string setupRouteSearch = string.Empty;
    private bool setupRoutePickerOpenRequested;

    private void DrawFcSetupPage()
    {
        var currentSnapshot = EnsureFleetSnapshot();
        if (currentSnapshot is null || currentSnapshot.FreeCompanies.Count == 0)
            return;

        var ordered = currentSnapshot.FreeCompanies
            .OrderByDescending(fc => this.configuration.GetFcPreferences(fc.FcIdKey).Favorite)
            .ThenBy(fc => fc.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (this.selectedSetupFcId is null || ordered.All(fc => fc.FcIdKey != this.selectedSetupFcId))
            SelectSetupFc(ordered[0].FcIdKey);
        var selected = ordered.First(fc => fc.FcIdKey == this.selectedSetupFcId);

        ImGui.SetNextItemWidth(Math.Min(420f * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X));
        if (ImGui.BeginCombo("Free company##setup-fc", selected.DisplayName))
        {
            foreach (var fc in ordered)
            {
                var favoritePrefix = this.configuration.GetFcPreferences(fc.FcIdKey).Favorite ? "★ " : string.Empty;
                if (ImGui.Selectable($"{favoritePrefix}{fc.DisplayName}##select-{fc.FcIdKey}", fc.FcIdKey == selected.FcIdKey))
                    RequestSetupFcSelection(fc.FcIdKey);
            }
            ImGui.EndCombo();
        }

        ImGui.Spacing();
        BeginSettingsCard("fc-preference-card", selected.DisplayName, "Favorites: Saved automatically. Target, strategy, assignment, and pinned-route changes remain staged until Save changes.");
        var preferences = this.configuration.GetFcPreferences(selected.FcIdKey);
        var favorite = preferences.Favorite;
        SettingLabel("Favorite", "Favorite FCs remain above non-favorites on Operations, Leveling, and Income.");
        if (ImGui.Checkbox("Pin this free company##favorite-fc", ref favorite))
        {
            preferences.Favorite = favorite;
            this.saveConfiguration();
        }

        SettingLabel("Target rank", $"Use the global target ({this.configuration.Settings.TargetRank}) or override it for this FC.");
        var useGlobalTarget = this.setupUseGlobalTarget;
        if (ImGui.Checkbox("Use global target##setup-global-target", ref useGlobalTarget))
        {
            this.setupUseGlobalTarget = useGlobalTarget;
            this.setupDraftDirty = true;
        }
        if (!this.setupUseGlobalTarget)
        {
            ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
            var target = this.setupTargetRank;
            if (ImGui.InputInt("Rank##setup-target-rank", ref target))
            {
                this.setupTargetRank = Math.Clamp(target, 1, this.catalog.MaximumRank);
                this.setupDraftDirty = true;
            }
        }

        SettingLabel("Leveling strategy", "Recommended unlocks missing slots and required main leveling routes, then selects the best expected EXP/hour.");
        if (DrawStrategyCombo(ref this.setupStrategy))
            this.setupDraftDirty = true;

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        SettingLabel(
            "Submarine assignments",
            "Tracked routes come from SubmarineTracker. A pinned farming route is a separate saved route used by farming projections.");
        DrawSubmarineSetupTable(selected);
        EndSettingsCard();

        ImGui.Spacing();
        DrawCeruleumStockCard(selected);
        DrawPinnedRoutePicker();
        DrawForgetFuelObservationModal();
    }

    private void DrawFcSetupActionBar()
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, PlannerUi.PanelBackground);
        if (ImGui.BeginChild("fc-setup-action-bar", new Vector2(-1, -1), true))
        {
            PlannerUi.WrappedText(this.setupDraftDirty ? "Unsaved FC changes" : "FC settings saved",
                this.setupDraftDirty ? PlannerUi.Amber : PlannerUi.Muted);
            ImGui.BeginDisabled(!this.setupDraftDirty);
            if (ImGui.Button("Save changes##save-fc-settings")) SaveSetupDraft();
            PlannerUi.SameLineIfFits("Discard changes");
            if (ImGui.Button("Discard changes##revert-fc-settings") && this.selectedSetupFcId is { } fcId)
                SelectSetupFc(fcId);
            ImGui.EndDisabled();
            PlannerUi.SameLineIfFits("Use global target and strategy");
            if (ImGui.Button("Use global target and strategy##reset-fc-settings"))
            {
                this.setupUseGlobalTarget = true;
                this.setupStrategy = null;
                this.setupDraftDirty = true;
            }
            PlannerUi.WrappedText("Saving target, strategy or assignment changes recalculates affected forecasts.", PlannerUi.Muted);
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawCeruleumStockCard(FcState selected)
    {
        if (this.focusSetupFuel)
        {
            ImGui.SetScrollHereY(0f);
            this.focusSetupFuel = false;
        }
        var now = DateTimeOffset.UtcNow;
        var previewPreferences = CreateSetupFuelPreferences(selected);
        var stock = ResolveFuelStock(selected, previewPreferences);
        var effectiveTargetRank = this.setupUseGlobalTarget
            ? this.configuration.Settings.TargetRank
            : Math.Clamp(this.setupTargetRank, 1, this.catalog.MaximumRank);
        var forecast = CalculateFuelRunway(
            selected,
            effectiveTargetRank,
            previewPreferences,
            stock,
            now);

        BeginSettingsCard(
            "fc-ceruleum-stock-card",
            "Ceruleum stock",
            "Choose the local stock observation used for this FC and preview its read-only fuel forecast.");

        SettingLabel("Source", "Automatic selects a single matching local observation, or the current live character when it belongs to this FC.");
        var sourceLabels = new Dictionary<FuelStockMode, string>
        {
            [FuelStockMode.Automatic] = "Automatic",
            [FuelStockMode.Character] = "Observed character",
            [FuelStockMode.Manual] = "Manual count",
        };
        ImGui.SetNextItemWidth(Math.Min(300f * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X));
        if (ImGui.BeginCombo("##setup-fuel-source", sourceLabels[this.setupFuelStockMode]))
        {
            foreach (var mode in Enum.GetValues<FuelStockMode>())
            {
                if (ImGui.Selectable($"{sourceLabels[mode]}##setup-fuel-source-{mode}", this.setupFuelStockMode == mode))
                {
                    this.setupFuelStockMode = mode;
                    this.setupDraftDirty = true;
                }
            }
            ImGui.EndCombo();
        }

        var candidates = FuelStockPresentation.CandidatesForFreeCompany(
            selected.GameFreeCompanyId,
            this.getFuelObservations());
        switch (this.setupFuelStockMode)
        {
            case FuelStockMode.Automatic:
                DrawAutomaticFuelSource(selected, stock, candidates, now);
                break;
            case FuelStockMode.Character:
                DrawObservedCharacterSource(selected, candidates, now);
                break;
            case FuelStockMode.Manual:
                DrawManualFuelSource();
                break;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        SettingLabel("Safety stock", "Automatic safety stock keeps enough tanks for one complete resend of every active farming submarine.");
        var automaticReserve = this.setupAutomaticReserve;
        if (ImGui.Checkbox("Automatic safety stock##setup-automatic-reserve", ref automaticReserve))
        {
            this.setupAutomaticReserve = automaticReserve;
            this.setupDraftDirty = true;
        }
        if (this.setupAutomaticReserve)
        {
            ImGui.TextColored(
                PlannerUi.Muted,
                forecast.TanksPerFullResend is { } tanksPerFullResend
                    ? $"One complete fleet trip: {tanksPerFullResend:N0} tanks"
                    : "One complete fleet trip: unavailable");
        }
        else
        {
            var reserve = this.setupFixedReserve;
            ImGui.SetNextItemWidth(Math.Min(180f * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X));
            if (ImGui.InputInt("Fixed safety stock##setup-fixed-reserve", ref reserve))
            {
                this.setupFixedReserve = Math.Max(0, reserve);
                this.setupDraftDirty = true;
            }
            ImGui.SameLine();
            ImGui.TextUnformatted("tanks");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        DrawSetupRunwayPreview(stock, forecast, now, selected.FcIdKey);

        ImGui.Spacing();
        ImGui.TextWrapped(
            "Submarine ETA Planner only reads the inventory of the character you are currently playing. " +
            "It remembers the last observed value locally; it does not log into characters, move them, collect submarines, resend voyages, or purchase fuel.");
        ImGui.PushStyleColor(ImGuiCol.Text, PlannerUi.Muted);
        ImGui.TextWrapped(
            "The automatic count covers the selected character’s readable inventory. Tanks stored elsewhere are not included; use Manual count when necessary.");
        ImGui.PopStyleColor();
        EndSettingsCard();
    }

    private void DrawAutomaticFuelSource(
        FcState selected,
        ResolvedFuelStock stock,
        IReadOnlyList<CharacterFuelObservation> candidates,
        DateTimeOffset now)
    {
        if (selected.GameFreeCompanyId is null)
        {
            ImGui.TextColored(PlannerUi.Amber, "The tracker’s numeric FC ID could not be decoded, so character inventory cannot be matched automatically. Manual count remains available.");
            return;
        }

        if (candidates.Count == 0)
        {
            DrawNoFuelObservationsState();
            return;
        }

        if (!stock.IsAvailable)
        {
            ImGui.TextColored(PlannerUi.Amber, "Multiple characters have been observed in this FC.");
            ImGui.TextColored(PlannerUi.Amber, "Choose the character that carries the workshop fuel.");
            return;
        }

        DrawResolvedFuelSource(stock, now);
        if (stock.CharacterId is { } characterId)
        {
            var observation = candidates.FirstOrDefault(candidate => candidate.CharacterId == characterId);
            if (observation is { IsLive: false })
                DrawForgetFuelObservationControl(observation);
        }
    }

    private void DrawObservedCharacterSource(
        FcState selected,
        IReadOnlyList<CharacterFuelObservation> candidates,
        DateTimeOffset now)
    {
        if (selected.GameFreeCompanyId is null)
        {
            ImGui.TextColored(PlannerUi.Amber, "The tracker’s numeric FC ID could not be decoded, so character inventory cannot be matched automatically. Manual count remains available.");
            return;
        }

        var selectedObservation = candidates.FirstOrDefault(
            candidate => candidate.CharacterId == this.setupFuelHolderCharacterId);
        var preview = selectedObservation is null
            ? "Choose observed character"
            : FuelStockPresentation.FormatCandidate(selectedObservation, now);
        ImGui.SetNextItemWidth(Math.Min(560f * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X));
        if (ImGui.BeginCombo("Fuel-holder character##setup-fuel-holder", preview))
        {
            foreach (var candidate in candidates)
            {
                if (ImGui.Selectable(
                        $"{FuelStockPresentation.FormatCandidate(candidate, now)}##setup-holder-{candidate.CharacterId}",
                        candidate.CharacterId == this.setupFuelHolderCharacterId))
                {
                    this.setupFuelHolderCharacterId = candidate.CharacterId;
                    this.setupDraftDirty = true;
                }
            }
            ImGui.EndCombo();
        }

        if (candidates.Count == 0 && this.setupFuelHolderCharacterId is null)
        {
            DrawNoFuelObservationsState();
            return;
        }

        if (selectedObservation is null)
        {
            if (this.setupFuelHolderCharacterId is not null)
                ImGui.TextColored(PlannerUi.Amber, "The selected fuel-holder character is no longer associated with this FC.");
            return;
        }

        var selectedStock = FuelStockResolver.Resolve(
            selected.GameFreeCompanyId,
            FuelStockMode.Character,
            selectedObservation.CharacterId,
            0,
            candidates);
        DrawResolvedFuelSource(selectedStock, now);
        if (!selectedObservation.IsLive)
            DrawForgetFuelObservationControl(selectedObservation);
    }

    private void DrawManualFuelSource()
    {
        var tanks = this.setupManualCeruleumTanks;
        ImGui.SetNextItemWidth(Math.Min(220f * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X));
        if (ImGui.InputInt("Manual ceruleum tank count##setup-manual-tanks", ref tanks))
        {
            this.setupManualCeruleumTanks = Math.Max(0, tanks);
            this.setupDraftDirty = true;
        }
    }

    private static void DrawNoFuelObservationsState()
    {
        ImGui.TextColored(PlannerUi.Amber, "No character inventory has been observed for this FC.");
        ImGui.TextWrapped("Log into the character that carries this FC’s ceruleum tanks once, or choose Manual count.");
    }

    private static void DrawResolvedFuelSource(ResolvedFuelStock stock, DateTimeOffset now)
    {
        var presentation = FuelStockPresentation.Describe(stock, now);
        ImGui.TextWrapped(presentation.ResultLine);
        if (presentation.DetailLine is { } detail)
            ImGui.TextColored(PlannerUi.Muted, detail);
    }

    private void DrawForgetFuelObservationControl(CharacterFuelObservation observation)
    {
        if (!ImGui.SmallButton($"Forget stored observation##forget-fuel-{observation.CharacterId}"))
            return;

        this.pendingForgottenFuelCharacterId = observation.CharacterId;
        ImGui.OpenPopup("Forget stored observation?###forget-fuel-observation");
    }

    private FcPreferences CreateSetupFuelPreferences(FcState selected)
    {
        var savedPreferences = this.configuration.GetFcPreferences(selected.FcIdKey);
        var submarines = selected.Submarines.ToDictionary(
            submarine => submarine.SubmarineId,
            submarine =>
            {
                var saved = savedPreferences.Submarines.GetValueOrDefault(submarine.SubmarineId);
                var draft = this.setupSubmarineDrafts.GetValueOrDefault(
                    submarine.SubmarineId,
                    SubmarineSetupDraft.Automatic);
                return new SubmarinePreferences
                {
                    Assignment = draft.Assignment,
                    PinnedFarmingRoute = draft.PinnedFarmingRoute?.ToList(),
                    CollectionDelayMinutes = saved?.CollectionDelayMinutes,
                };
            });
        return new FcPreferences
        {
            FuelStockMode = this.setupFuelStockMode,
            FuelHolderCharacterId = this.setupFuelHolderCharacterId,
            ManualCeruleumTanks = Math.Max(0, this.setupManualCeruleumTanks),
            CeruleumReserve = this.setupAutomaticReserve ? null : Math.Max(0, this.setupFixedReserve),
            Submarines = submarines,
        };
    }

    private static void DrawSetupRunwayPreview(
        ResolvedFuelStock stock,
        FuelRunwayForecast forecast,
        DateTimeOffset now,
        string fcIdKey)
    {
        ImGui.TextColored(PlannerUi.Teal, "Fuel forecast preview");
        var source = FuelStockPresentation.Describe(stock, now);
        if (ImGui.BeginTable(
                $"setup-fuel-runway-{fcIdKey}",
                2,
                ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Measure", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch, 1.7f);
            DrawSetupRunwayRow("Stock basis", forecast.StockBasis is { } stockBasis ? $"{stockBasis:N0} tanks" : "Unavailable");
            DrawSetupRunwayRow("Stock source", source.SourceLine);
            DrawSetupRunwayRow(
                "Tanks per fleet trip",
                forecast.TanksPerFullResend is { } tanksPerFullResend
                    ? tanksPerFullResend.ToString("N0")
                    : "Unavailable");
            DrawSetupRunwayRow("Estimated tanks/day", forecast.TanksPerDay.ToString("N1"));
            DrawSetupRunwayRow(
                "Fleet trips left",
                forecast.Status == FuelRunwayStatus.Unavailable ? "Unavailable" : forecast.FullFleetSendsRemaining.ToString("N0"));
            DrawSetupRunwayRow(
                "Refill before",
                forecast.Status == FuelRunwayStatus.Unavailable
                    ? "Unavailable"
                    : FormatRefillDeadline(forecast, now));
            ImGui.EndTable();
        }

        if (forecast.Status == FuelRunwayStatus.Unavailable)
        {
            ImGui.TextColored(PlannerUi.Amber, "Fuel forecast unavailable");
            foreach (var warning in forecast.Warnings)
                ImGui.TextWrapped(warning);
        }
    }

    private static void DrawSetupRunwayRow(string label, string value)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextColored(PlannerUi.Muted, label);
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(value);
    }

    private bool DrawStrategyCombo(ref FcStrategyPreset? strategy)
    {
        var current = strategy is null ? 0 : (int)strategy.Value + 1;
        string[] labels =
        [
            $"Inherit global ({EtaModelLabels[(int)this.configuration.Settings.EtaModel]})",
            "Recommended",
            "Advanced · Immediate EXP only",
            "Advanced · Slots first, then immediate EXP",
            "Advanced · Unlock everything, then level",
        ];
        var changed = false;
        ImGui.SetNextItemWidth(Math.Min(470f * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X));
        if (ImGui.BeginCombo("##setup-strategy", labels[current]))
        {
            for (var index = 0; index < labels.Length; index++)
            {
                if (ImGui.Selectable(labels[index], index == current))
                {
                    strategy = index == 0 ? null : (FcStrategyPreset)(index - 1);
                    changed = true;
                }
                if (ImGui.IsItemHovered())
                    PlannerUi.Tooltip(index switch
                    {
                        1 => "Unlock missing submarine slots and required main leveling routes, then use best expected EXP/hour.",
                        2 => "Use the best currently available EXP route without deliberately chasing unlock objectives.",
                        3 => "Unlock missing submarine slots first, then use the best currently available EXP route.",
                        4 => "Deliberately unlock every reachable destination before pure leveling.",
                        _ => "Use the global simulation and route settings.",
                    });
            }
            ImGui.EndCombo();
        }
        return changed;
    }

    private void DrawSubmarineSetupTable(FcState selected)
    {
        if (selected.Submarines.Count == 0)
        {
            ImGui.TextColored(PlannerUi.Muted, "No submarines are recorded for this free company.");
            return;
        }

        var effectiveTargetRank = this.setupUseGlobalTarget
            ? this.configuration.Settings.TargetRank
            : Math.Clamp(this.setupTargetRank, 1, this.catalog.MaximumRank);
        var layout = CalculateResponsiveTableLayout(
            ImGui.GetContentRegionAvail().X,
            new ResponsiveTableColumn("Submarine", selected.Submarines.Select(submarine => submarine.Name), 115, 220),
            new ResponsiveTableColumn("Rank", selected.Submarines.Select(submarine => submarine.Rank.ToString()), 58, 80),
            new ResponsiveTableColumn("Tracked route", selected.Submarines.Select(submarine => FormatCompactRoute(submarine.CurrentRoute)), 135, 320),
            new ResponsiveTableColumn(
                "Assignment",
                selected.Submarines.Select(submarine =>
                {
                    var draft = this.setupSubmarineDrafts.GetValueOrDefault(submarine.SubmarineId, SubmarineSetupDraft.Automatic);
                    return AssignmentLabel(draft.Assignment, SubmarineRoleResolver.Resolve(draft.Assignment, submarine.Rank, effectiveTargetRank));
                }),
                165,
                235),
            new ResponsiveTableColumn(
                "Pinned farming route",
                selected.Submarines.Select(submarine =>
                {
                    var route = this.setupSubmarineDrafts.GetValueOrDefault(submarine.SubmarineId, SubmarineSetupDraft.Automatic).PinnedFarmingRoute;
                    return route is { Count: > 0 } ? FormatCompactRoute(route) : "No pin — tracked route is used";
                }),
                210,
                420,
                Flexible: true,
                FlexWeight: 1.35f,
                FillRemaining: true));
        var flags = ImGuiTableFlags.Borders |
                    ImGuiTableFlags.RowBg |
                    ImGuiTableFlags.Resizable |
                    ImGuiTableFlags.SizingFixedFit;
        if (layout.RequiresHorizontalScroll)
            flags |= ImGuiTableFlags.ScrollX;
        var tableHeight = layout.RequiresHorizontalScroll
            ? CalculateTableHeight(selected.Submarines.Count * 2, true)
            : 0f;

        if (!ImGui.BeginTable(
                $"setup-submarines-{selected.FcIdKey}",
                5,
                flags,
                new Vector2(-1, tableHeight),
                layout.RequiresHorizontalScroll ? layout.InnerWidth : 0f))
            return;

        SetupResponsiveTableColumns(layout);
        ImGui.TableSetupScrollFreeze(1, 1);
        ImGui.TableHeadersRow();

        foreach (var submarine in selected.Submarines.OrderBy(submarine => submarine.SubmarineId))
        {
            var draft = this.setupSubmarineDrafts.GetValueOrDefault(
                submarine.SubmarineId,
                SubmarineSetupDraft.Automatic);

            ImGui.TableNextRow();
            DrawTableText(submarine.Name);
            DrawTableText(submarine.Rank.ToString());

            ImGui.TableNextColumn();
            if (!submarine.CurrentVoyageKnown)
            {
                ImGui.TextColored(PlannerUi.Amber, "Unknown");
                PlannerUi.Tooltip("SubmarineTracker could not identify the currently tracked voyage route.");
            }
            else if (submarine.CurrentRoute.Count == 0)
            {
                ImGui.TextColored(PlannerUi.Muted, "No tracked route");
            }
            else
            {
                DrawCompactRoute(submarine.CurrentRoute);
            }

            ImGui.TableNextColumn();
            DrawAssignmentCombo(submarine, draft, effectiveTargetRank);

            ImGui.TableNextColumn();
            DrawPinnedRouteControls(selected, submarine, draft);
        }

        ImGui.EndTable();
    }

    private void DrawAssignmentCombo(
        SubmarineState submarine,
        SubmarineSetupDraft draft,
        int effectiveTargetRank)
    {
        var effectiveRole = SubmarineRoleResolver.Resolve(
            draft.Assignment,
            submarine.Rank,
            effectiveTargetRank);
        var preview = AssignmentLabel(draft.Assignment, effectiveRole);
        ImGui.SetNextItemWidth(-1);
        if (!ImGui.BeginCombo($"##setup-assignment-{submarine.SubmarineId}", preview))
            return;

        foreach (var assignment in Enum.GetValues<SubmarineAssignment>())
        {
            var optionRole = SubmarineRoleResolver.Resolve(assignment, submarine.Rank, effectiveTargetRank);
            var label = AssignmentLabel(assignment, optionRole);
            if (ImGui.Selectable(
                    $"{label}##setup-assignment-{submarine.SubmarineId}-{assignment}",
                    draft.Assignment == assignment))
            {
                this.setupSubmarineDrafts[submarine.SubmarineId] = draft with { Assignment = assignment };
                this.setupDraftDirty = true;
            }
        }
        ImGui.EndCombo();
    }

    private void DrawPinnedRouteControls(FcState freeCompany, SubmarineState submarine, SubmarineSetupDraft draft)
    {
        if (draft.PinnedFarmingRoute is { Count: > 0 } pinnedRoute)
        {
            ImGui.TextColored(PlannerUi.Muted, "Pinned:");
            ImGui.SameLine();
            DrawCompactRoute(pinnedRoute, PlannerUi.Teal);
            var validation = ValidateSetupRoute(freeCompany, submarine, pinnedRoute);
            if (!validation.IsRunnable)
            {
                ImGui.TextColored(PlannerUi.Amber, "Saved route is not runnable now");
                PlannerUi.Tooltip(string.Join('\n', validation.Errors));
            }
        }
        else
        {
            ImGui.TextColored(PlannerUi.Muted, "No pin — tracked route is used");
        }

        var canUseTrackedRoute = submarine.CurrentVoyageKnown && submarine.CurrentRoute.Count > 0;
        if (!canUseTrackedRoute)
            ImGui.BeginDisabled();
        if (ImGui.SmallButton($"Use tracked route##setup-use-tracked-{submarine.SubmarineId}"))
            SetPinnedRouteFromTracked(freeCompany, submarine);
        if (!canUseTrackedRoute)
            ImGui.EndDisabled();
        if (!canUseTrackedRoute)
            PlannerUi.Tooltip("No known tracked route is available to pin.");

        ImGui.SameLine();
        if (ImGui.SmallButton($"Edit##setup-edit-route-{submarine.SubmarineId}"))
            OpenPinnedRoutePicker(submarine, draft.PinnedFarmingRoute);

        ImGui.SameLine();
        var hasPin = draft.PinnedFarmingRoute is { Count: > 0 };
        if (!hasPin)
            ImGui.BeginDisabled();
        if (ImGui.SmallButton($"Clear pin##setup-clear-route-{submarine.SubmarineId}"))
        {
            this.setupSubmarineDrafts[submarine.SubmarineId] = draft.WithPinnedFarmingRoute(null);
            if (this.setupRoutePickerSubmarineId == submarine.SubmarineId)
                ClosePinnedRoutePicker();
            this.setupDraftDirty = true;
        }
        if (!hasPin)
            ImGui.EndDisabled();

    }

    private void SetPinnedRouteFromTracked(FcState freeCompany, SubmarineState submarine)
    {
        var validation = ValidateSetupRoute(freeCompany, submarine, submarine.CurrentRoute);
        if (!validation.IsRunnable)
        {
            OpenPinnedRoutePicker(submarine, submarine.CurrentRoute);
            return;
        }

        var draft = this.setupSubmarineDrafts.GetValueOrDefault(
            submarine.SubmarineId,
            SubmarineSetupDraft.Automatic);
        this.setupSubmarineDrafts[submarine.SubmarineId] = draft.WithPinnedFarmingRoute(validation.Route);
        ClosePinnedRoutePicker();
        this.setupDraftDirty = true;
    }

    private void OpenPinnedRoutePicker(SubmarineState submarine, IReadOnlyList<uint>? route)
    {
        this.setupRoutePickerSubmarineId = submarine.SubmarineId;
        this.setupRoutePickerRoute.Clear();
        if (route is not null)
            this.setupRoutePickerRoute.AddRange(route);
        this.setupRouteSearch = string.Empty;
        this.setupRoutePickerOpenRequested = true;
    }

    private void ClosePinnedRoutePicker()
    {
        this.setupRoutePickerSubmarineId = null;
        this.setupRoutePickerRoute.Clear();
        this.setupRouteSearch = string.Empty;
        this.setupRoutePickerOpenRequested = false;
    }

    private RouteSelectionValidation ValidateSetupRoute(
        FcState freeCompany,
        SubmarineState submarine,
        IReadOnlyList<uint> route)
    {
        var build = this.catalog.ResolveBuild(submarine.BuildParts, submarine.Rank);
        return build is null
            ? new RouteSelectionValidation(route.ToArray(), ["The current submarine build could not be resolved."], null, null)
            : this.routeSelectionCatalog.ValidateRoute(route, build, freeCompany.UnlockedPoints);
    }

    private void DrawPinnedRoutePicker()
    {
        const string popupId = "Choose farming route###setup-route-picker";
        if (this.setupRoutePickerOpenRequested)
        {
            ImGui.OpenPopup(popupId);
            this.setupRoutePickerOpenRequested = false;
        }

        ImGui.SetNextWindowSize(new Vector2(760f, 640f) * ImGuiHelpers.GlobalScale, ImGuiCond.FirstUseEver);
        if (!ImGui.BeginPopupModal(popupId, ImGuiWindowFlags.NoSavedSettings))
            return;

        var freeCompany = this.snapshot?.FreeCompanies.FirstOrDefault(fc => fc.FcIdKey == this.selectedSetupFcId);
        var submarine = freeCompany?.Submarines.FirstOrDefault(sub => sub.SubmarineId == this.setupRoutePickerSubmarineId);
        if (freeCompany is null || submarine is null)
        {
            ImGui.TextColored(PlannerUi.Amber, "The selected submarine is no longer available.");
            if (ImGui.Button("Close"))
            {
                ClosePinnedRoutePicker();
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
            return;
        }

        var build = this.catalog.ResolveBuild(submarine.BuildParts, submarine.Rank);
        PlannerUi.IconText(FontAwesomeIcon.Ship, $"{submarine.Name} · R{submarine.Rank} · {build?.Code ?? "Unknown build"}", PlannerUi.Teal);
        ImGui.TextColored(PlannerUi.Muted, "Choose up to five unlocked destinations. Their order is the order the submarine will visit them.");
        if (submarine.CurrentRoute.Count > 0)
        {
            ImGui.TextUnformatted("Tracked route:");
            ImGui.SameLine();
            DrawCompactRoute(submarine.CurrentRoute);
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Selected route");
        if (this.setupRoutePickerRoute.Count == 0)
        {
            ImGui.TextColored(PlannerUi.Muted, "No destinations selected.");
        }
        else
        {
            DrawCompactRoute(this.setupRoutePickerRoute, PlannerUi.Teal);
            for (var index = 0; index < this.setupRoutePickerRoute.Count; index++)
            {
                var sectorId = this.setupRoutePickerRoute[index];
                ImGui.PushID($"selected-route-{sectorId}-{index}");
                ImGui.TextUnformatted($"{index + 1}. {this.catalog.PointName(sectorId)}");
                ImGui.SameLine();
                if (index == 0)
                    ImGui.BeginDisabled();
                if (ImGui.SmallButton("Up"))
                    (this.setupRoutePickerRoute[index - 1], this.setupRoutePickerRoute[index]) =
                        (this.setupRoutePickerRoute[index], this.setupRoutePickerRoute[index - 1]);
                if (index == 0)
                    ImGui.EndDisabled();
                ImGui.SameLine();
                if (index == this.setupRoutePickerRoute.Count - 1)
                    ImGui.BeginDisabled();
                if (ImGui.SmallButton("Down"))
                    (this.setupRoutePickerRoute[index + 1], this.setupRoutePickerRoute[index]) =
                        (this.setupRoutePickerRoute[index], this.setupRoutePickerRoute[index + 1]);
                if (index == this.setupRoutePickerRoute.Count - 1)
                    ImGui.EndDisabled();
                ImGui.SameLine();
                if (ImGui.SmallButton("Remove"))
                {
                    this.setupRoutePickerRoute.RemoveAt(index);
                    ImGui.PopID();
                    break;
                }
                ImGui.PopID();
            }
        }

        ImGui.Separator();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##route-search", "Search destination name or letter…", ref this.setupRouteSearch, 128);
        var search = this.setupRouteSearch.Trim();
        var destinations = this.routeSelectionCatalog.RouteDestinations
            .Where(destination => freeCompany.UnlockedPoints.Contains(destination.SectorId))
            .Where(destination => destination.RequiredRank <= submarine.Rank)
            .Where(destination => string.IsNullOrEmpty(search) ||
                                  destination.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                  destination.Code.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                  destination.MapName.Contains(search, StringComparison.OrdinalIgnoreCase))
            .GroupBy(destination => (destination.MapId, destination.MapName))
            .OrderBy(group => group.Key.MapId)
            .ToArray();

        if (ImGui.BeginChild("route-destination-list", new Vector2(-1, 230f * ImGuiHelpers.GlobalScale), true))
        {
            if (destinations.Length == 0)
            {
                ImGui.TextColored(
                    PlannerUi.Muted,
                    freeCompany.UnlockDataKnown
                        ? "No unlocked destinations match this search and submarine rank."
                        : "Unlocked destination data is not available for this free company.");
            }
            foreach (var map in destinations)
            {
                ImGui.TextColored(PlannerUi.Teal, map.Key.MapName);
                foreach (var destination in map)
                {
                    var alreadySelected = this.setupRoutePickerRoute.Contains(destination.SectorId);
                    var tentative = this.setupRoutePickerRoute.Append(destination.SectorId).ToArray();
                    var tentativeValidation = build is null
                        ? new RouteSelectionValidation(tentative, ["The current submarine build could not be resolved."], null, null)
                        : this.routeSelectionCatalog.ValidateRoute(tentative, build, freeCompany.UnlockedPoints);
                    var canAdd = !alreadySelected && tentativeValidation.IsRunnable;
                    if (!canAdd)
                        ImGui.BeginDisabled();
                    if (ImGui.Selectable(
                            $"{destination.Code} — {destination.Name} · R{destination.RequiredRank}##destination-{destination.SectorId}"))
                    {
                        this.setupRoutePickerRoute.Add(destination.SectorId);
                    }
                    if (!canAdd)
                        ImGui.EndDisabled();
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    {
                        ImGui.SetTooltip(alreadySelected
                            ? "Already selected."
                            : string.Join('\n', tentativeValidation.Errors));
                    }
                }
                ImGui.Spacing();
            }
        }
        ImGui.EndChild();

        var validation = ValidateSetupRoute(freeCompany, submarine, this.setupRoutePickerRoute);
        if (this.setupRoutePickerRoute.Count > 0)
        {
            var tanksText = validation.CeruleumTanks is { } tanks ? $"{tanks:N0} tanks" : "Fuel unavailable";
            var durationText = validation.Duration is { } duration ? FormatDuration(duration) : "duration unavailable";
            ImGui.TextColored(PlannerUi.Muted, $"Preview: {tanksText} · {durationText}");
        }
        foreach (var error in validation.Errors)
            ImGui.TextColored(PlannerUi.Amber, error);

        if (!validation.IsRunnable)
            ImGui.BeginDisabled();
        if (PlannerUi.IconButtonWithText("apply-pinned-route", FontAwesomeIcon.Check, "Use route"))
        {
            var draft = this.setupSubmarineDrafts.GetValueOrDefault(submarine.SubmarineId, SubmarineSetupDraft.Automatic);
            this.setupSubmarineDrafts[submarine.SubmarineId] = draft.WithPinnedFarmingRoute(validation.Route);
            this.setupDraftDirty = true;
            ClosePinnedRoutePicker();
            ImGui.CloseCurrentPopup();
        }
        if (!validation.IsRunnable)
            ImGui.EndDisabled();
        ImGui.SameLine();
        if (PlannerUi.IconButtonWithText("cancel-pinned-route", FontAwesomeIcon.Times, "Cancel"))
        {
            ClosePinnedRoutePicker();
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private static string AssignmentLabel(
        SubmarineAssignment assignment,
        EffectiveSubmarineRole effectiveRole)
        => assignment == SubmarineAssignment.Auto
            ? $"Automatic — {effectiveRole}"
            : assignment.ToString();

    private void RequestSetupFcSelection(string fcIdKey)
        => RequestFcNavigation(fcIdKey, PlannerPage.FcSetup);

    private void SelectSetupFc(string fcIdKey)
    {
        this.selectedSetupFcId = fcIdKey;
        var preferences = this.configuration.GetFcPreferences(fcIdKey);
        this.setupUseGlobalTarget = preferences.TargetRankOverride is null;
        this.setupTargetRank = preferences.TargetRankOverride ?? this.configuration.Settings.TargetRank;
        this.setupStrategy = preferences.StrategyOverride;
        this.setupFuelStockMode = preferences.FuelStockMode;
        this.setupFuelHolderCharacterId = preferences.FuelHolderCharacterId;
        this.setupManualCeruleumTanks = Math.Max(0, preferences.ManualCeruleumTanks.GetValueOrDefault());
        this.setupAutomaticReserve = preferences.CeruleumReserve is null;
        this.setupFixedReserve = Math.Max(0, preferences.CeruleumReserve.GetValueOrDefault());
        this.setupSubmarineDrafts.Clear();
        var fleet = this.snapshot?.FreeCompanies.FirstOrDefault(fc => fc.FcIdKey == fcIdKey);
        var draft = FcSetupDraft.Capture(
            preferences,
            fleet?.Submarines.Select(submarine => submarine.SubmarineId) ?? []);
        foreach (var (submarineId, submarineDraft) in draft.Submarines)
            this.setupSubmarineDrafts[submarineId] = submarineDraft;
        ClosePinnedRoutePicker();
        this.setupDraftDirty = false;
    }

    private void SaveSetupDraft()
    {
        if (this.selectedSetupFcId is null)
            return;
        var preferences = this.configuration.GetFcPreferences(this.selectedSetupFcId);
        var draft = FcSetupDraft.Capture(preferences, this.setupSubmarineDrafts.Keys) with
        {
            TargetRankOverride = this.setupUseGlobalTarget
                ? null
                : Math.Clamp(this.setupTargetRank, 1, this.catalog.MaximumRank),
            StrategyOverride = this.setupStrategy,
            FuelStockMode = this.setupFuelStockMode,
            FuelHolderCharacterId = this.setupFuelHolderCharacterId,
            ManualCeruleumTanks = Math.Max(0, this.setupManualCeruleumTanks),
            CeruleumReserve = this.setupAutomaticReserve ? null : Math.Max(0, this.setupFixedReserve),
        };
        foreach (var (submarineId, submarineDraft) in this.setupSubmarineDrafts)
            draft = draft.WithSubmarine(submarineId, submarineDraft);
        var applyResult = draft.ApplyTo(preferences);
        this.setupDraftDirty = false;
        ClosePinnedRoutePicker();
        this.saveConfiguration();
        if (applyResult.EtaRefreshRequired)
            QueueRefresh(ForecastRefreshMode.Incremental);
    }

    private void DrawForgetFuelObservationModal()
    {
        if (!ImGui.BeginPopupModal(
                "Forget stored observation?###forget-fuel-observation",
                ImGuiWindowFlags.AlwaysAutoResize))
            return;

        var observation = this.pendingForgottenFuelCharacterId is { } characterId
            ? this.getFuelObservations().FirstOrDefault(item => item.CharacterId == characterId)
            : null;
        var character = observation is null
            ? "this character"
            : FuelStockPresentation.CharacterLabel(observation);
        ImGui.TextWrapped($"Forget the locally stored ceruleum observation for {character}?");
        ImGui.TextColored(PlannerUi.Muted, "This deletes only Submarine ETA Planner’s local snapshot and performs no game action.");
        ImGui.Spacing();
        if (PlannerUi.IconButtonWithText("confirm-forget-fuel", FontAwesomeIcon.Trash, "Forget"))
        {
            if (this.pendingForgottenFuelCharacterId is { } pending)
                this.forgetFuelObservation(pending);
            this.pendingForgottenFuelCharacterId = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (PlannerUi.IconButtonWithText("cancel-forget-fuel", FontAwesomeIcon.Times, "Cancel"))
        {
            this.pendingForgottenFuelCharacterId = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }
}
