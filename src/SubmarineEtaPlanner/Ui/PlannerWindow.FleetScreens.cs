using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using SubmarineEtaPlanner.Planner;
using System.Numerics;

namespace SubmarineEtaPlanner.Ui;

public sealed partial class PlannerWindow
{
    private sealed record OperationsHeaderRenderContext(
        OperationsFcHeaderPresentation Presentation,
        FcCurrentVoyageProgressPresentation CurrentVoyages);

    private readonly record struct OperationsHeaderColumn(float Offset, float Width, int Line);

    private sealed record OperationsHeaderLayout(
        bool TwoLine,
        float HeaderHeight,
        float LegendHeight,
        OperationsHeaderColumn FreeCompany,
        OperationsHeaderColumn World,
        OperationsHeaderColumn Mode,
        OperationsHeaderColumn Attention,
        OperationsHeaderColumn FarmReady,
        OperationsHeaderColumn Ranks);

    private readonly record struct IncomeHeaderColumn(float Offset, float Width, int Line);

    private sealed record IncomeHeaderLayout(
        bool TwoLine,
        float HeaderHeight,
        float LegendHeight,
        IncomeHeaderColumn FreeCompany,
        IncomeHeaderColumn World,
        IncomeHeaderColumn Mode,
        IncomeHeaderColumn GrossGil,
        IncomeHeaderColumn GilPerDay,
        IncomeHeaderColumn GilPerVoyage,
        IncomeHeaderColumn Voyages);

    private string? selectedSetupFcId;
    private string? pendingSetupFcId;
    private bool setupDraftDirty;
    private bool setupUseGlobalTarget = true;
    private int setupTargetRank;
    private FcStrategyPreset? setupStrategy;

    private void DrawOperationsPage()
    {
        var currentSnapshot = EnsureFleetSnapshot();
        if (currentSnapshot is null)
            return;

        DrawFleetNotices(currentSnapshot);
        DrawSearch("Search FC, world, or submarine…");
        ImGui.SameLine();
        DrawOperationsViewButton("All fleets", OperationsView.AllFleets);
        ImGui.SameLine(0, 3f * ImGuiHelpers.GlobalScale);
        DrawOperationsViewButton("Leveling", OperationsView.Leveling);
        ImGui.SameLine(0, 3f * ImGuiHelpers.GlobalScale);
        DrawOperationsViewButton("Farming", OperationsView.Farming);
        ImGui.SameLine();
        DrawOperationsSortCombo();

        var now = DateTimeOffset.UtcNow;
        var allProjections = CreateProjections(currentSnapshot, now);
        var requiredMode = this.configuration.OperationsView switch
        {
            OperationsView.Leveling => FleetMode.Leveling,
            OperationsView.Farming => FleetMode.Farming,
            _ => (FleetMode?)null,
        };
        var filteredProjections = allProjections
            .Where(projection => MatchesSearch(projection.State))
            .Where(projection => FleetPresentationFiltering.Includes(projection, requiredMode))
            .ToArray();
        var projections = this.configuration.OperationsSort switch
        {
            OperationsSort.FarmReadyEta => FleetPresentationOrdering.FarmReadyEta(filteredProjections, IsFavorite),
            OperationsSort.FcName => FleetPresentationOrdering.ByName(filteredProjections, IsFavorite),
            _ => FleetPresentationOrdering.ActionsFirst(filteredProjections, IsFavorite),
        };

        ImGui.Spacing();
        ImGui.TextColored(PlannerUi.Muted, $"{projections.Count} fleet{(projections.Count == 1 ? string.Empty : "s")} shown of {allProjections.Count} tracked");
        ImGui.Spacing();
        var headerContexts = projections.ToDictionary(
            projection => projection.State.FcIdKey,
            projection => new OperationsHeaderRenderContext(
                OperationsFcHeaderPresentation.Create(projection, IsFavorite(projection), now),
                CurrentVoyageProgressFormatter.CreateForFc(projection.State.Submarines, this.catalog, now)));
        var headerLayout = CalculateOperationsHeaderLayout(
            headerContexts.Values.Select(context => context.Presentation),
            ImGui.GetContentRegionAvail().X);
        DrawOperationsHeaderLegend(headerLayout);
        foreach (var projection in projections)
            DrawOperationsFleetGroup(projection, now, headerContexts[projection.State.FcIdKey], headerLayout);
    }

    private void DrawLevelingPage()
    {
        var currentSnapshot = EnsureFleetSnapshot();
        if (currentSnapshot is null)
            return;

        DrawFleetNotices(currentSnapshot);
        DrawSearch("Search all leveling fleets…");
        ImGui.SameLine();
        DrawLevelingFilterCombo();
        ImGui.SameLine();
        DrawLevelingSortCombo();

        var now = DateTimeOffset.UtcNow;
        var projections = CreateProjections(currentSnapshot, now)
            .Where(projection => projection.Mode == FleetMode.Leveling)
            .Where(projection => MatchesSearch(projection.State))
            .Where(projection => this.configuration.LevelingFilter switch
            {
                LevelingFilter.Actionable => projection.ImmediateActionCount > 0,
                LevelingFilter.Favorites => IsFavorite(projection),
                _ => true,
            })
            .OrderByDescending(IsFavorite)
            .ThenBy(projection => this.configuration.LevelingSort switch
            {
                LevelingSort.LowestRank => projection.Submarines.Min(submarine => submarine.Rank),
                _ => 0,
            })
            .ThenBy(projection => this.configuration.LevelingSort switch
            {
                LevelingSort.FarmReadyEta => projection.CompletionP50AtUtc ?? DateTimeOffset.MaxValue,
                LevelingSort.NextAction => projection.Submarines.Select(submarine => submarine.NextActionAtUtc).Where(value => value is not null).Min() ?? DateTimeOffset.MaxValue,
                _ => DateTimeOffset.MinValue,
            })
            .ThenBy(projection => projection.State.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ImGui.Spacing();
        ImGui.TextColored(PlannerUi.Muted, $"{projections.Length} leveling fleet{(projections.Length == 1 ? string.Empty : "s")} · every submarine remains visible when expanded");
        ImGui.Spacing();
        var headerContexts = projections.ToDictionary(
            projection => projection.State.FcIdKey,
            projection => new OperationsHeaderRenderContext(
                OperationsFcHeaderPresentation.Create(projection, IsFavorite(projection), now),
                CurrentVoyageProgressFormatter.CreateForFc(projection.State.Submarines, this.catalog, now)));
        var headerLayout = CalculateOperationsHeaderLayout(
            headerContexts.Values.Select(context => context.Presentation),
            ImGui.GetContentRegionAvail().X);
        DrawOperationsHeaderLegend(headerLayout);
        foreach (var projection in projections)
            DrawLevelingFleetGroup(projection, now, headerContexts[projection.State.FcIdKey], headerLayout);
    }

    private void DrawIncomePage()
    {
        var currentSnapshot = EnsureFleetSnapshot();
        if (currentSnapshot is null)
            return;

        PlannerUi.Callout(
            "income-definition",
            FontAwesomeIcon.InfoCircle,
            "Recorded gross NPC salvage value",
            "Values use all valid tracked returns in the selected period and may include voyages from before an FC reached its target rank.",
            PlannerUi.Teal);
        ImGui.Spacing();
        DrawIncomePeriodButtons();
        ImGui.SameLine();
        DrawIncomeSortCombo();

        var now = DateTimeOffset.UtcNow;
        var period = GetIncomePeriod();
        var projections = CreateProjections(currentSnapshot, now).ToDictionary(item => item.State.FcIdKey);
        var metrics = currentSnapshot.FreeCompanies
            .Select(fc => IncomeMetricsCalculator.Calculate(fc, now, period))
            .OrderByDescending(metric => this.configuration.GetFcPreferences(metric.FcIdKey).Favorite)
            .ThenByDescending(metric => this.configuration.IncomeSort switch
            {
                IncomeSort.GilPerDay => metric.GilPerDay,
                IncomeSort.GilPerVoyage => metric.GilPerVoyage,
                IncomeSort.FcName => 0,
                _ => metric.GrossGil,
            })
            .ThenBy(metric => metric.FcDisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        DrawIncomeSummary(metrics, now, period);
        ImGui.Spacing();
        var incomeHeaders = metrics.ToDictionary(
            metric => metric.FcIdKey,
            metric => IncomeFcHeaderPresentation.Create(
                projections[metric.FcIdKey],
                metric,
                this.configuration.GetFcPreferences(metric.FcIdKey).Favorite));
        var incomeLayout = CalculateIncomeHeaderLayout(incomeHeaders.Values, ImGui.GetContentRegionAvail().X);
        DrawIncomeHeaderLegend(incomeLayout);
        foreach (var metric in metrics)
        {
            var projection = projections[metric.FcIdKey];
            DrawIncomeFleetGroup(projection, metric, incomeHeaders[metric.FcIdKey], incomeLayout);
        }
    }

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
        BeginSettingsCard("fc-preference-card", selected.DisplayName, "Favorites save immediately. Target and strategy changes remain staged until Save.");
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
        EndSettingsCard();

        if (this.setupDraftDirty)
            PlannerUi.DrawStatusPill("Unsaved FC changes", PlannerUi.Amber);
        else
            PlannerUi.DrawStatusPill("FC settings up to date", PlannerUi.Green);
        ImGui.SameLine();
        if (PlannerUi.IconButtonWithText("save-fc-settings", FontAwesomeIcon.Check, "Save"))
            SaveSetupDraft();
        ImGui.SameLine();
        if (PlannerUi.IconButtonWithText("reset-fc-settings", FontAwesomeIcon.Undo, "Reset to global"))
        {
            this.setupUseGlobalTarget = true;
            this.setupStrategy = null;
            this.setupDraftDirty = true;
        }
        ImGui.SameLine();
        if (PlannerUi.IconButtonWithText("revert-fc-settings", FontAwesomeIcon.Times, "Revert"))
            SelectSetupFc(selected.FcIdKey);

        DrawUnsavedSetupModal();
    }

    private EtaPlannerSnapshot? EnsureFleetSnapshot()
    {
        if (this.snapshot is null && this.refreshTask is null)
            StartRefresh();
        if (this.snapshot is null)
        {
            PlannerUi.Callout("loading-fleet-data", FontAwesomeIcon.SyncAlt, "Loading fleet data", "Reading SubmarineTracker and calculating fleet forecasts…", PlannerUi.Cyan);
            return null;
        }
        CheckForTrackerDataChanges();
        return this.snapshot;
    }

    private void DrawFleetNotices(EtaPlannerSnapshot currentSnapshot)
    {
        if (this.trackerDataChanged && this.refreshTask is not { IsCompleted: false })
        {
            PlannerUi.Callout("tracker-change", FontAwesomeIcon.Database, "New tracker data is available", "Refresh to synchronize ranks, returns, routes, and unlock state.", PlannerUi.Amber);
            ImGui.Spacing();
        }
        if (!currentSnapshot.IsRunning && !currentSnapshot.IsComplete)
        {
            PlannerUi.Callout("forecast-warning", FontAwesomeIcon.ExclamationTriangle, "Forecast warning", currentSnapshot.IncompleteReason ?? "One or more FC forecasts are incomplete.", PlannerUi.Amber);
            ImGui.Spacing();
        }
    }

    private IReadOnlyList<FcOperationalProjection> CreateProjections(EtaPlannerSnapshot currentSnapshot, DateTimeOffset now)
    {
        var results = currentSnapshot.Results.ToDictionary(result => Convert.ToHexString(result.FcId));
        return currentSnapshot.FreeCompanies.Select(fc =>
        {
            var preferences = this.configuration.GetFcPreferences(fc.FcIdKey);
            var effective = EffectiveEtaSettingsResolver.Resolve(
                this.configuration.Settings,
                new FcSimulationOverride(preferences.TargetRankOverride, preferences.StrategyOverride),
                this.catalog.MaximumRank);
            return FleetPresentationBuilder.Create(fc, results.GetValueOrDefault(fc.FcIdKey), effective, this.catalog, now);
        }).ToArray();
    }

    private void DrawOperationsFleetGroup(
        FcOperationalProjection projection,
        DateTimeOffset now,
        OperationsHeaderRenderContext headerContext,
        OperationsHeaderLayout layout)
    {
        if (this.viewState.ExpansionOverride is { } expansion)
            ImGui.SetNextItemOpen(expansion, ImGuiCond.Always);

        ImGui.Spacing();
        var open = DrawAlignedOperationsHeader(
            $"operations-fc-{projection.State.FcIdKey}",
            headerContext,
            layout);
        DrawOperationsHeaderTooltip(projection, headerContext, now);
        if (!open)
            return;

        var completion = OperationsCompletionPresentation.Create(projection);
        ImGui.TextColored(PlannerUi.Muted, completion.Label);
        PlannerUi.Tooltip(completion.Tooltip);
        ImGui.Spacing();
        DrawOperationsSubmarineTable(projection, now);
    }

    private static OperationsHeaderLayout CalculateOperationsHeaderLayout(
        IEnumerable<OperationsFcHeaderPresentation> presentations,
        float availableWidth)
    {
        var values = presentations.ToArray();
        var scale = ImGuiHelpers.GlobalScale;
        var gap = 10f * scale;
        var gutter = 40f * scale;
        var fcWidth = MeasureHeaderColumn(values.Select(value => value.FreeCompany), "FC", 90f, 155f);
        var worldWidth = MeasureHeaderColumn(values.Select(value => value.World), "World", 90f, 150f);
        var modeWidth = MeasureHeaderColumn(values.Select(value => value.Mode), "Mode", 82f, 105f);
        var attentionWidth = MeasureHeaderColumn(values.Select(value => value.Attention), "Next action / return", 125f, 185f);
        var farmReadyWidth = MeasureHeaderColumn(values.Select(value => value.FarmReady), "Farm ready", 105f, 155f);
        var ranksWidth = MeasureHeaderColumn(values.Select(value => value.Ranks), "Ranks", 170f, 275f);
        var singleLineRequired = gutter + fcWidth + worldWidth + modeWidth + attentionWidth + farmReadyWidth + ranksWidth + (gap * 5f);
        var lineHeight = ImGui.GetTextLineHeight();

        if (availableWidth >= singleLineRequired)
        {
            var fc = new OperationsHeaderColumn(gutter, fcWidth, 0);
            var world = new OperationsHeaderColumn(fc.Offset + fc.Width + gap, worldWidth, 0);
            var mode = new OperationsHeaderColumn(world.Offset + world.Width + gap, modeWidth, 0);
            var attention = new OperationsHeaderColumn(mode.Offset + mode.Width + gap, attentionWidth, 0);
            var farmReady = new OperationsHeaderColumn(attention.Offset + attention.Width + gap, farmReadyWidth, 0);
            var ranksOffset = farmReady.Offset + farmReady.Width + gap;
            return new OperationsHeaderLayout(
                false,
                ImGui.GetFrameHeight(),
                ImGui.GetFrameHeight(),
                fc,
                world,
                mode,
                attention,
                farmReady,
                new OperationsHeaderColumn(ranksOffset, Math.Max(1f, availableWidth - ranksOffset), 0));
        }

        var contentWidth = Math.Max(1f, availableWidth - gutter);
        var firstLineWidth = Math.Max(1f, contentWidth - (gap * 3f));
        var fcTwoLine = Math.Max(82f * scale, firstLineWidth * 0.24f);
        var worldTwoLine = Math.Max(82f * scale, firstLineWidth * 0.22f);
        var modeTwoLine = Math.Max(78f * scale, firstLineWidth * 0.17f);
        var attentionTwoLine = Math.Max(1f, firstLineWidth - fcTwoLine - worldTwoLine - modeTwoLine);
        var secondLineWidth = Math.Max(1f, contentWidth - gap);
        var farmReadyTwoLine = Math.Max(110f * scale, secondLineWidth * 0.30f);
        var ranksTwoLine = Math.Max(1f, secondLineWidth - farmReadyTwoLine);
        var height = (lineHeight * 2f) + (14f * scale);
        return new OperationsHeaderLayout(
            true,
            height,
            height,
            new OperationsHeaderColumn(gutter, fcTwoLine, 0),
            new OperationsHeaderColumn(gutter + fcTwoLine + gap, worldTwoLine, 0),
            new OperationsHeaderColumn(gutter + fcTwoLine + gap + worldTwoLine + gap, modeTwoLine, 0),
            new OperationsHeaderColumn(gutter + fcTwoLine + gap + worldTwoLine + gap + modeTwoLine + gap, attentionTwoLine, 0),
            new OperationsHeaderColumn(gutter, farmReadyTwoLine, 1),
            new OperationsHeaderColumn(gutter + farmReadyTwoLine + gap, ranksTwoLine, 1));
    }

    private static void DrawOperationsHeaderLegend(OperationsHeaderLayout layout)
    {
        var origin = ImGui.GetCursorScreenPos();
        DrawOperationsHeaderFields(
            origin,
            layout,
            new OperationsFcHeaderPresentation(
                "FC",
                "World",
                "Mode",
                "Next action / return",
                "Farm ready",
                "Ranks",
                false,
                false),
            legend: true);
        ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, layout.LegendHeight));
    }

    private static bool DrawAlignedOperationsHeader(
        string id,
        OperationsHeaderRenderContext context,
        OperationsHeaderLayout layout)
    {
        var origin = ImGui.GetCursorScreenPos();
        DrawFcProgressBackground(context.CurrentVoyages, layout.HeaderHeight);
        var style = ImGui.GetStyle();
        var paddingY = Math.Max(0f, (layout.HeaderHeight - ImGui.GetTextLineHeight()) / 2f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(style.FramePadding.X, paddingY));
        ImGui.PushStyleColor(ImGuiCol.Header, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(PlannerUi.PanelBackgroundAlt.X, PlannerUi.PanelBackgroundAlt.Y, PlannerUi.PanelBackgroundAlt.Z, 0.62f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(PlannerUi.PanelBackgroundAlt.X, PlannerUi.PanelBackgroundAlt.Y, PlannerUi.PanelBackgroundAlt.Z, 0.76f));
        var open = ImGui.CollapsingHeader($"###{id}");
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar();
        DrawOperationsHeaderFields(origin, layout, context.Presentation, legend: false);
        return open;
    }

    private static void DrawOperationsHeaderFields(
        Vector2 origin,
        OperationsHeaderLayout layout,
        OperationsFcHeaderPresentation presentation,
        bool legend)
    {
        var normal = legend ? PlannerUi.Muted : ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        DrawOperationsHeaderCell(origin, layout, layout.FreeCompany, presentation.FreeCompany, normal);
        DrawOperationsHeaderCell(origin, layout, layout.World, presentation.World, normal);
        DrawOperationsHeaderCell(origin, layout, layout.Mode, presentation.Mode,
            legend ? PlannerUi.Muted : presentation.IsFarming ? PlannerUi.Green : PlannerUi.Teal);
        DrawOperationsHeaderCell(origin, layout, layout.Attention, presentation.Attention,
            legend ? PlannerUi.Muted : presentation.HasImmediateActions ? PlannerUi.Amber : PlannerUi.Cyan);
        DrawOperationsHeaderCell(origin, layout, layout.FarmReady, presentation.FarmReady,
            legend ? PlannerUi.Muted : presentation.IsFarming ? PlannerUi.Green : PlannerUi.Cyan);
        DrawOperationsHeaderCell(origin, layout, layout.Ranks, presentation.Ranks, normal);
    }

    private static void DrawOperationsHeaderCell(
        Vector2 origin,
        OperationsHeaderLayout layout,
        OperationsHeaderColumn column,
        string text,
        Vector4 color)
    {
        if (column.Width <= 1f)
            return;

        var scale = ImGuiHelpers.GlobalScale;
        var lineHeight = ImGui.GetTextLineHeight();
        var lineGap = 2f * scale;
        var contentHeight = layout.TwoLine ? (lineHeight * 2f) + lineGap : lineHeight;
        var firstLineY = origin.Y + ((layout.HeaderHeight - contentHeight) / 2f);
        var y = firstLineY + (column.Line * (lineHeight + lineGap));
        var padding = 3f * scale;
        var fitted = FitHeaderText(text, Math.Max(1f, column.Width - (padding * 2f)));
        var drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(
            new Vector2(origin.X + column.Offset, y),
            new Vector2(origin.X + column.Offset + column.Width, y + lineHeight),
            true);
        drawList.AddText(
            new Vector2(origin.X + column.Offset + padding, y),
            ImGui.ColorConvertFloat4ToU32(color),
            fitted);
        drawList.PopClipRect();
    }

    private void DrawOperationsHeaderTooltip(
        FcOperationalProjection projection,
        OperationsHeaderRenderContext context,
        DateTimeOffset now)
    {
        if (!ImGui.IsItemHovered())
            return;

        ImGui.BeginTooltip();
        ImGui.TextColored(PlannerUi.Teal, $"{projection.State.FreeCompanyTag} — {projection.State.World}");
        ImGui.TextUnformatted($"{context.Presentation.Mode} · Target R{projection.EffectiveTargetRank}");
        ImGui.TextUnformatted($"{context.Presentation.Attention} · Farm ready: {context.Presentation.FarmReady}");
        ImGui.Separator();
        foreach (var submarine in projection.Submarines)
        {
            ImGui.TextUnformatted($"{submarine.Name}: R{submarine.Rank} · {CompactOperationalStatePresentation.Create(submarine).Label}");
        }

        if (context.CurrentVoyages.Primary is { } primary)
        {
            ImGui.Separator();
            var state = projection.State.Submarines.FirstOrDefault(submarine => submarine.SubmarineId == primary.SubmarineId);
            if (state is not null)
                DrawCurrentVoyageTooltipContents(primary, state);
        }
        if (projection.CompletionP10AtUtc is { } p10 && projection.CompletionP90AtUtc is { } p90)
        {
            ImGui.Separator();
            ImGui.TextColored(PlannerUi.Muted, $"Likely ready between {FormatRelative(p10, now)} and {FormatRelative(p90, now)}");
        }
        ImGui.EndTooltip();
    }

    private void DrawOperationsSubmarineTable(FcOperationalProjection projection, DateTimeOffset now)
    {
        const float minimumWidth = 900f;
        var scaledMinimumWidth = minimumWidth * ImGuiHelpers.GlobalScale;
        var needsHorizontalScroll = ImGui.GetContentRegionAvail().X < scaledMinimumWidth;
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp;
        if (needsHorizontalScroll)
            flags |= ImGuiTableFlags.ScrollX;
        var tableHeight = CalculateTableHeight(projection.Submarines.Count, needsHorizontalScroll);
        if (!ImGui.BeginTable(
                $"operations-projection-table-{projection.State.FcIdKey}",
                7,
                flags,
                new Vector2(-1, tableHeight),
                needsHorizontalScroll ? scaledMinimumWidth : 0f))
            return;

        ImGui.TableSetupColumn("Submarine", ImGuiTableColumnFlags.WidthFixed, 150f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Rank → after voyage", ImGuiTableColumnFlags.WidthFixed, 125f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("State", ImGuiTableColumnFlags.WidthFixed, 90f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Current / next route", ImGuiTableColumnFlags.WidthStretch, 1.2f);
        ImGui.TableSetupColumn("Purpose", ImGuiTableColumnFlags.WidthFixed, 82f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Expected EXP", ImGuiTableColumnFlags.WidthFixed, 105f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Target ETA", ImGuiTableColumnFlags.WidthFixed, 105f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupScrollFreeze(1, 1);
        ImGui.TableHeadersRow();
        foreach (var submarine in projection.Submarines)
        {
            ImGui.TableNextRow();
            DrawTableText(submarine.Name);
            ImGui.TableNextColumn();
            var rankPresentation = OperationsRankPresentation.Create(submarine);
            ImGui.TextUnformatted(rankPresentation.Label);
            if (rankPresentation.Tooltip is not null)
                PlannerUi.Tooltip(rankPresentation.Tooltip);
            ImGui.TableNextColumn();
            var compactState = CompactOperationalStatePresentation.Create(submarine);
            ImGui.TextUnformatted(compactState.Label);
            PlannerUi.Tooltip(compactState.Tooltip);
            ImGui.TableNextColumn();
            DrawCompactRoute(submarine.DisplayedRoute);
            if ((submarine.State is OperationalState.Underway or OperationalState.ReadyToCollect) &&
                submarine.Rank < submarine.EffectiveTargetRank &&
                submarine.RecommendedNextRoute.Count > 0)
            {
                ImGui.SameLine();
                ImGui.TextColored(PlannerUi.Muted, "then");
                ImGui.SameLine();
                DrawCompactRoute(submarine.RecommendedNextRoute, PlannerUi.Teal);
            }
            if (submarine.AlternativeRoutes.Count > 1 && ImGui.IsItemHovered())
                PlannerUi.Tooltip("Conditional recommendation: alternative routes remain possible depending on unlock outcomes.");
            DrawTableText(submarine.RoutePurpose.ToString());
            DrawTableText(submarine.ExpectedExp is { } exp ? exp.ToString("N0") : "Unavailable");
            if (submarine.ExpectedExp is null && submarine.ProjectionUnavailableReason is not null)
                PlannerUi.Tooltip(submarine.ProjectionUnavailableReason);
            DrawTableText(submarine.Rank >= submarine.EffectiveTargetRank
                ? "Ready"
                : submarine.TargetEtaAtUtc is { } eta ? FormatRelative(eta, now) : "Unavailable");
        }
        ImGui.EndTable();
    }

    private void DrawLevelingFleetGroup(
        FcOperationalProjection projection,
        DateTimeOffset now,
        OperationsHeaderRenderContext headerContext,
        OperationsHeaderLayout layout)
    {
        if (this.viewState.ExpansionOverride is { } expansion)
            ImGui.SetNextItemOpen(expansion, ImGuiCond.Always);

        ImGui.Spacing();
        var open = DrawAlignedOperationsHeader(
            $"leveling-fc-{projection.State.FcIdKey}",
            headerContext,
            layout);
        DrawOperationsHeaderTooltip(projection, headerContext, now);
        if (!open)
            return;

        var completion = OperationsCompletionPresentation.Create(projection);
        var voyages = projection.Submarines.Sum(submarine => submarine.VoyagesRemaining);
        var bottleneck = projection.Submarines
            .Where(submarine => submarine.TargetEtaAtUtc is not null)
            .OrderByDescending(submarine => submarine.TargetEtaAtUtc)
            .FirstOrDefault();
        var levelingDetails = $"{completion.Label} · {voyages} voyage{(voyages == 1 ? string.Empty : "s")} remaining" +
                              (bottleneck is null ? string.Empty : $" · Bottleneck: {bottleneck.Name}");
        ImGui.TextColored(PlannerUi.Muted, levelingDetails);
        PlannerUi.Tooltip(completion.Tooltip);
        ImGui.Spacing();
        DrawLevelingSubmarineTable(projection, now);
        DrawExpandedLevelingForecasts(projection, now);
    }

    private void DrawLevelingSubmarineTable(FcOperationalProjection projection, DateTimeOffset now)
    {
        const float minimumWidth = 900f;
        var scaledMinimumWidth = minimumWidth * ImGuiHelpers.GlobalScale;
        var needsHorizontalScroll = ImGui.GetContentRegionAvail().X < scaledMinimumWidth;
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp;
        if (needsHorizontalScroll)
            flags |= ImGuiTableFlags.ScrollX;
        var tableHeight = CalculateTableHeight(projection.Submarines.Count, needsHorizontalScroll);
        if (!ImGui.BeginTable(
                $"leveling-projection-table-{projection.State.FcIdKey}",
                7,
                flags,
                new Vector2(-1, tableHeight),
                needsHorizontalScroll ? scaledMinimumWidth : 0f))
            return;

        ImGui.TableSetupColumn("Submarine", ImGuiTableColumnFlags.WidthFixed, 165f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Rank → after voyage", ImGuiTableColumnFlags.WidthFixed, 125f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("State", ImGuiTableColumnFlags.WidthFixed, 90f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Current / next route", ImGuiTableColumnFlags.WidthStretch, 1.2f);
        ImGui.TableSetupColumn("Purpose", ImGuiTableColumnFlags.WidthFixed, 82f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Expected EXP", ImGuiTableColumnFlags.WidthFixed, 105f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Target ETA", ImGuiTableColumnFlags.WidthFixed, 105f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupScrollFreeze(1, 1);
        ImGui.TableHeadersRow();
        foreach (var submarine in projection.Submarines)
        {
            var expansionKey = GetLevelingForecastExpansionKey(projection.State.FcIdKey, submarine.SubmarineId);
            var expanded = this.expandedSubmarines.Contains(expansionKey);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var rowStart = ImGui.GetCursorScreenPos();
            var clicked = ImGui.Selectable(
                $"##leveling-submarine-{projection.State.FcIdKey}-{submarine.SubmarineId}",
                false,
                ImGuiSelectableFlags.SpanAllColumns,
                new Vector2(0, ImGui.GetFrameHeight()));
            var rowHovered = ImGui.IsItemHovered();
            var rowEnd = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(rowStart + new Vector2(3f * ImGuiHelpers.GlobalScale, 1f * ImGuiHelpers.GlobalScale));
            PlannerUi.Icon(expanded ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronRight, PlannerUi.Teal);
            ImGui.SameLine();
            ImGui.TextUnformatted(submarine.Name);
            ImGui.SetCursorScreenPos(rowEnd);
            if (clicked)
            {
                if (!this.expandedSubmarines.Add(expansionKey))
                    this.expandedSubmarines.Remove(expansionKey);
            }
            if (rowHovered)
                ImGui.SetTooltip(expanded ? "Hide voyage forecast" : "Show complete voyage forecast");

            ImGui.TableNextColumn();
            var rankPresentation = OperationsRankPresentation.Create(submarine);
            ImGui.TextUnformatted(rankPresentation.Label);
            if (rankPresentation.Tooltip is not null)
                PlannerUi.Tooltip(rankPresentation.Tooltip);
            ImGui.TableNextColumn();
            var compactState = CompactOperationalStatePresentation.Create(submarine);
            ImGui.TextUnformatted(compactState.Label);
            PlannerUi.Tooltip(compactState.Tooltip);
            ImGui.TableNextColumn();
            DrawCompactRoute(submarine.DisplayedRoute);
            if ((submarine.State is OperationalState.Underway or OperationalState.ReadyToCollect) &&
                submarine.Rank < submarine.EffectiveTargetRank &&
                submarine.RecommendedNextRoute.Count > 0)
            {
                ImGui.SameLine();
                ImGui.TextColored(PlannerUi.Muted, "then");
                ImGui.SameLine();
                DrawCompactRoute(submarine.RecommendedNextRoute, PlannerUi.Teal);
            }
            if (submarine.AlternativeRoutes.Count > 1 && ImGui.IsItemHovered())
                PlannerUi.Tooltip("Conditional recommendation: alternative routes remain possible depending on unlock outcomes.");
            DrawTableText(submarine.RoutePurpose.ToString());
            DrawTableText(submarine.ExpectedExp is { } exp ? exp.ToString("N0") : "Unavailable");
            if (submarine.ExpectedExp is null && submarine.ProjectionUnavailableReason is not null)
                PlannerUi.Tooltip(submarine.ProjectionUnavailableReason);
            DrawTableText(submarine.Rank >= submarine.EffectiveTargetRank
                ? "Ready"
                : submarine.TargetEtaAtUtc is { } eta ? FormatRelative(eta, now) : "Unavailable");
        }
        ImGui.EndTable();
    }

    private void DrawExpandedLevelingForecasts(FcOperationalProjection projection, DateTimeOffset now)
    {
        foreach (var submarine in projection.Submarines)
        {
            var expansionKey = GetLevelingForecastExpansionKey(projection.State.FcIdKey, submarine.SubmarineId);
            if (!this.expandedSubmarines.Contains(expansionKey))
                continue;

            ImGui.PushID(expansionKey);
            ImGui.Indent(12f * ImGuiHelpers.GlobalScale);
            ImGui.Spacing();
            PlannerUi.IconText(FontAwesomeIcon.Ship, $"{submarine.Name} voyage forecast", PlannerUi.Teal);
            var result = projection.Result?.PerSubResults.FirstOrDefault(item => item.SubmarineId == submarine.SubmarineId);
            if (result is null)
            {
                PlannerUi.Callout(
                    "forecast-unavailable",
                    FontAwesomeIcon.ExclamationTriangle,
                    "Forecast unavailable",
                    submarine.ProjectionUnavailableReason ?? "No modeled voyage forecast is available for this submarine.",
                    PlannerUi.Amber);
            }
            else
            {
                DrawSubDetails(result, this.configuration.Settings.ShowRouteDiagnostics, now);
            }
            ImGui.Unindent(12f * ImGuiHelpers.GlobalScale);
            ImGui.PopID();
        }
    }

    private static string GetLevelingForecastExpansionKey(string fcIdKey, long submarineId)
        => $"leveling:{fcIdKey}:{submarineId}";

    private static IncomeHeaderLayout CalculateIncomeHeaderLayout(
        IEnumerable<IncomeFcHeaderPresentation> presentations,
        float availableWidth)
    {
        var values = presentations.ToArray();
        var scale = ImGuiHelpers.GlobalScale;
        var gap = 8f * scale;
        var gutter = 40f * scale;
        var fcWidth = MeasureHeaderColumn(values.Select(value => value.FreeCompany), "FC", 90f, 155f);
        var worldWidth = MeasureHeaderColumn(values.Select(value => value.World), "World", 85f, 145f);
        var modeWidth = MeasureHeaderColumn(values.Select(value => value.Mode), "Mode", 78f, 105f);
        var grossWidth = MeasureHeaderColumn(values.Select(value => value.GrossGil), "Gross gil", 95f, 145f);
        var dayWidth = MeasureHeaderColumn(values.Select(value => value.GilPerDay), "Gil / day", 88f, 125f);
        var voyageWidth = MeasureHeaderColumn(values.Select(value => value.GilPerVoyage), "Gil / voyage", 100f, 140f);
        var countWidth = MeasureHeaderColumn(values.Select(value => value.Voyages), "Voyages", 72f, 100f);
        var singleLineRequired = gutter + fcWidth + worldWidth + modeWidth + grossWidth + dayWidth + voyageWidth + countWidth + (gap * 6f);
        var lineHeight = ImGui.GetTextLineHeight();

        if (availableWidth >= singleLineRequired)
        {
            var fc = new IncomeHeaderColumn(gutter, fcWidth, 0);
            var world = new IncomeHeaderColumn(fc.Offset + fc.Width + gap, worldWidth, 0);
            var mode = new IncomeHeaderColumn(world.Offset + world.Width + gap, modeWidth, 0);
            var gross = new IncomeHeaderColumn(mode.Offset + mode.Width + gap, grossWidth, 0);
            var day = new IncomeHeaderColumn(gross.Offset + gross.Width + gap, dayWidth, 0);
            var voyage = new IncomeHeaderColumn(day.Offset + day.Width + gap, voyageWidth, 0);
            var countOffset = voyage.Offset + voyage.Width + gap;
            return new IncomeHeaderLayout(
                false,
                ImGui.GetFrameHeight(),
                ImGui.GetFrameHeight(),
                fc,
                world,
                mode,
                gross,
                day,
                voyage,
                new IncomeHeaderColumn(countOffset, Math.Max(1f, availableWidth - countOffset), 0));
        }

        var contentWidth = Math.Max(1f, availableWidth - gutter);
        var firstLineWidth = Math.Max(1f, contentWidth - (gap * 3f));
        var fcTwoLine = Math.Max(82f * scale, firstLineWidth * 0.25f);
        var worldTwoLine = Math.Max(82f * scale, firstLineWidth * 0.23f);
        var modeTwoLine = Math.Max(75f * scale, firstLineWidth * 0.17f);
        var grossTwoLine = Math.Max(1f, firstLineWidth - fcTwoLine - worldTwoLine - modeTwoLine);
        var secondLineWidth = Math.Max(1f, contentWidth - (gap * 2f));
        var dayTwoLine = Math.Max(90f * scale, secondLineWidth * 0.32f);
        var voyageTwoLine = Math.Max(105f * scale, secondLineWidth * 0.38f);
        var countTwoLine = Math.Max(1f, secondLineWidth - dayTwoLine - voyageTwoLine);
        var height = (lineHeight * 2f) + (14f * scale);
        return new IncomeHeaderLayout(
            true,
            height,
            height,
            new IncomeHeaderColumn(gutter, fcTwoLine, 0),
            new IncomeHeaderColumn(gutter + fcTwoLine + gap, worldTwoLine, 0),
            new IncomeHeaderColumn(gutter + fcTwoLine + gap + worldTwoLine + gap, modeTwoLine, 0),
            new IncomeHeaderColumn(gutter + fcTwoLine + gap + worldTwoLine + gap + modeTwoLine + gap, grossTwoLine, 0),
            new IncomeHeaderColumn(gutter, dayTwoLine, 1),
            new IncomeHeaderColumn(gutter + dayTwoLine + gap, voyageTwoLine, 1),
            new IncomeHeaderColumn(gutter + dayTwoLine + gap + voyageTwoLine + gap, countTwoLine, 1));
    }

    private static void DrawIncomeHeaderLegend(IncomeHeaderLayout layout)
    {
        var origin = ImGui.GetCursorScreenPos();
        DrawIncomeHeaderFields(
            origin,
            layout,
            new IncomeFcHeaderPresentation(
                "income-legend",
                "FC",
                "World",
                "Mode",
                "Gross gil",
                "Gil / day",
                "Gil / voyage",
                "Voyages",
                false),
            legend: true);
        ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, layout.LegendHeight));
    }

    private void DrawIncomeFleetGroup(
        FcOperationalProjection projection,
        IncomeFcMetrics metric,
        IncomeFcHeaderPresentation presentation,
        IncomeHeaderLayout layout)
    {
        ImGui.Spacing();
        var origin = ImGui.GetCursorScreenPos();
        var style = ImGui.GetStyle();
        var paddingY = Math.Max(0f, (layout.HeaderHeight - ImGui.GetTextLineHeight()) / 2f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(style.FramePadding.X, paddingY));
        ImGui.PushStyleColor(ImGuiCol.Header, PlannerUi.PanelBackgroundAlt);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(PlannerUi.PanelBackgroundAlt.X + 0.02f, PlannerUi.PanelBackgroundAlt.Y + 0.05f, PlannerUi.PanelBackgroundAlt.Z + 0.05f, 1f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(PlannerUi.PanelBackgroundAlt.X + 0.03f, PlannerUi.PanelBackgroundAlt.Y + 0.08f, PlannerUi.PanelBackgroundAlt.Z + 0.08f, 1f));
        var open = ImGui.CollapsingHeader($"###{presentation.WidgetId}");
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar();
        DrawIncomeHeaderFields(origin, layout, presentation, legend: false);
        DrawIncomeHeaderTooltip(projection, metric);
        if (!open)
            return;

        ImGui.Spacing();
        DrawIncomeSubmarineTable(metric);
    }

    private static void DrawIncomeHeaderFields(
        Vector2 origin,
        IncomeHeaderLayout layout,
        IncomeFcHeaderPresentation presentation,
        bool legend)
    {
        var normal = legend ? PlannerUi.Muted : ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        DrawIncomeHeaderCell(origin, layout, layout.FreeCompany, presentation.FreeCompany, normal);
        DrawIncomeHeaderCell(origin, layout, layout.World, presentation.World, normal);
        DrawIncomeHeaderCell(origin, layout, layout.Mode, presentation.Mode,
            legend ? PlannerUi.Muted : presentation.IsFarming ? PlannerUi.Green : PlannerUi.Teal);
        DrawIncomeHeaderCell(origin, layout, layout.GrossGil, presentation.GrossGil,
            legend ? PlannerUi.Muted : PlannerUi.Green);
        DrawIncomeHeaderCell(origin, layout, layout.GilPerDay, presentation.GilPerDay, normal);
        DrawIncomeHeaderCell(origin, layout, layout.GilPerVoyage, presentation.GilPerVoyage, normal);
        DrawIncomeHeaderCell(origin, layout, layout.Voyages, presentation.Voyages, normal);
    }

    private static void DrawIncomeHeaderCell(
        Vector2 origin,
        IncomeHeaderLayout layout,
        IncomeHeaderColumn column,
        string text,
        Vector4 color)
    {
        if (column.Width <= 1f)
            return;

        var scale = ImGuiHelpers.GlobalScale;
        var lineHeight = ImGui.GetTextLineHeight();
        var lineGap = 2f * scale;
        var contentHeight = layout.TwoLine ? (lineHeight * 2f) + lineGap : lineHeight;
        var firstLineY = origin.Y + ((layout.HeaderHeight - contentHeight) / 2f);
        var y = firstLineY + (column.Line * (lineHeight + lineGap));
        var padding = 3f * scale;
        var fitted = FitHeaderText(text, Math.Max(1f, column.Width - (padding * 2f)));
        var drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(
            new Vector2(origin.X + column.Offset, y),
            new Vector2(origin.X + column.Offset + column.Width, y + lineHeight),
            true);
        drawList.AddText(
            new Vector2(origin.X + column.Offset + padding, y),
            ImGui.ColorConvertFloat4ToU32(color),
            fitted);
        drawList.PopClipRect();
    }

    private static void DrawIncomeHeaderTooltip(FcOperationalProjection projection, IncomeFcMetrics metric)
    {
        if (!ImGui.IsItemHovered())
            return;

        ImGui.BeginTooltip();
        ImGui.TextColored(PlannerUi.Teal, $"{projection.State.FreeCompanyTag} — {projection.State.World}");
        ImGui.TextUnformatted($"{projection.Mode} · {metric.ValidVoyages:N0} valid tracked voyage{(metric.ValidVoyages == 1 ? string.Empty : "s")}");
        ImGui.Separator();
        ImGui.TextColored(PlannerUi.Green, $"Gross NPC salvage value: {metric.GrossGil:N0} gil");
        ImGui.TextUnformatted($"Gil per covered day: {metric.GilPerDay:N0}");
        ImGui.TextUnformatted($"Gil per valid voyage: {metric.GilPerVoyage:N0}");
        ImGui.TextColored(PlannerUi.Muted, $"Coverage: {FormatIncomeDate(metric.FirstReturnAtUtc)} – {FormatIncomeDate(metric.LastReturnAtUtc)}");
        ImGui.EndTooltip();
    }

    private static void DrawIncomeSubmarineTable(IncomeFcMetrics metric)
    {
        const float minimumWidth = 900f;
        var scaledMinimumWidth = minimumWidth * ImGuiHelpers.GlobalScale;
        var needsHorizontalScroll = ImGui.GetContentRegionAvail().X < scaledMinimumWidth;
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp;
        if (needsHorizontalScroll)
            flags |= ImGuiTableFlags.ScrollX;
        var tableHeight = CalculateTableHeight(metric.Submarines.Count, needsHorizontalScroll);
        if (!ImGui.BeginTable(
                $"income-table-{metric.FcIdKey}",
                7,
                flags,
                new Vector2(-1, tableHeight),
                needsHorizontalScroll ? scaledMinimumWidth : 0f))
            return;

        ImGui.TableSetupColumn("Submarine", ImGuiTableColumnFlags.WidthFixed, 165f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Gross gil", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("Gil/day", ImGuiTableColumnFlags.WidthFixed, 105f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Voyages", ImGuiTableColumnFlags.WidthFixed, 82f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Gil/voyage", ImGuiTableColumnFlags.WidthFixed, 110f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("First return", ImGuiTableColumnFlags.WidthFixed, 140f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Last return", ImGuiTableColumnFlags.WidthFixed, 140f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupScrollFreeze(1, 1);
        ImGui.TableHeadersRow();
        foreach (var submarine in metric.Submarines)
        {
            ImGui.TableNextRow();
            DrawTableText(submarine.Name);
            DrawTableText($"{submarine.GrossGil:N0}");
            DrawTableText($"{submarine.GilPerDay:N0}");
            DrawTableText(submarine.ValidVoyages.ToString("N0"));
            DrawTableText($"{submarine.GilPerVoyage:N0}");
            DrawTableText(FormatIncomeDate(submarine.FirstReturnAtUtc));
            DrawTableText(FormatIncomeDate(submarine.LastReturnAtUtc));
        }
        ImGui.EndTable();
    }

    private void DrawIncomeSummary(IReadOnlyList<IncomeFcMetrics> metrics, DateTimeOffset now, TimeSpan? period)
    {
        var gross = metrics.Sum(item => item.GrossGil);
        var voyages = metrics.Sum(item => item.ValidVoyages);
        var first = metrics.Where(item => item.FirstReturnAtUtc is not null).Select(item => item.FirstReturnAtUtc).Min();
        var start = first is null ? (DateTimeOffset?)null : period is null ? first : (first > now - period ? first : now - period);
        var days = start is null ? 0 : Math.Max((now - start.Value).TotalDays, 1d / 24d);
        if (!ImGui.BeginTable("income-summary", 4, ImGuiTableFlags.SizingStretchSame))
            return;
        ImGui.TableNextColumn(); PlannerUi.MetricCard("income-gross", FontAwesomeIcon.Coins, ResultsViewState.FormatCompactGil(gross), "Gross gil", PlannerUi.Green);
        ImGui.TableNextColumn(); PlannerUi.MetricCard("income-day", FontAwesomeIcon.CalendarDay, days == 0 ? "—" : ResultsViewState.FormatCompactGil((long)(gross / days)), "Gil / covered day", PlannerUi.Teal);
        ImGui.TableNextColumn(); PlannerUi.MetricCard("income-voyage", FontAwesomeIcon.Ship, voyages == 0 ? "—" : ResultsViewState.FormatCompactGil(gross / voyages), "Gil / valid voyage", PlannerUi.Cyan);
        ImGui.TableNextColumn(); PlannerUi.MetricCard("income-fcs", FontAwesomeIcon.Building, metrics.Count.ToString(), $"Tracked FCs · {days:0.#} days", PlannerUi.Muted);
        ImGui.EndTable();
    }

    private void DrawSearch(string hint)
    {
        ImGui.SetNextItemWidth(Math.Min(290f * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X * 0.38f));
        ImGui.InputTextWithHint("##fleet-search", hint, ref this.fcSearch, 100);
    }

    private bool MatchesSearch(FcState fc) => string.IsNullOrWhiteSpace(this.fcSearch) ||
        fc.DisplayName.Contains(this.fcSearch, StringComparison.OrdinalIgnoreCase) ||
        fc.Submarines.Any(submarine => submarine.Name.Contains(this.fcSearch, StringComparison.OrdinalIgnoreCase));

    private bool IsFavorite(FcOperationalProjection projection)
        => this.configuration.GetFcPreferences(projection.State.FcIdKey).Favorite;

    private void DrawOperationsViewButton(string label, OperationsView view)
    {
        if (PlannerUi.SegmentedButton($"operations-view-{view}", label, this.configuration.OperationsView == view))
        {
            this.configuration.OperationsView = view;
            this.saveConfiguration();
        }
    }

    private void DrawOperationsSortCombo()
    {
        string[] labels = ["Next return · actions first", "Farm-ready ETA", "FC name"];
        var value = this.configuration.OperationsSort;
        if (DrawEnumCombo("##operations-sort", labels, ref value))
        {
            this.configuration.OperationsSort = value;
            this.saveConfiguration();
        }
    }

    private void DrawLevelingSortCombo()
    {
        string[] labels = ["Farm-ready ETA", "Lowest rank", "Next action", "FC name"];
        var value = this.configuration.LevelingSort;
        if (DrawEnumCombo("##leveling-sort", labels, ref value))
        {
            this.configuration.LevelingSort = value;
            this.saveConfiguration();
        }
    }

    private void DrawLevelingFilterCombo()
    {
        string[] labels = ["All leveling fleets", "Actionable submarines", "Favorites"];
        var value = this.configuration.LevelingFilter;
        if (DrawEnumCombo("##leveling-filter", labels, ref value))
        {
            this.configuration.LevelingFilter = value;
            this.saveConfiguration();
        }
    }

    private void DrawIncomePeriodButtons()
    {
        DrawIncomePeriodButton("7 days", IncomePeriod.Days7);
        ImGui.SameLine(0, 3f * ImGuiHelpers.GlobalScale);
        DrawIncomePeriodButton("30 days", IncomePeriod.Days30);
        ImGui.SameLine(0, 3f * ImGuiHelpers.GlobalScale);
        DrawIncomePeriodButton("90 days", IncomePeriod.Days90);
        ImGui.SameLine(0, 3f * ImGuiHelpers.GlobalScale);
        DrawIncomePeriodButton("Lifetime", IncomePeriod.Lifetime);
    }

    private void DrawIncomePeriodButton(string label, IncomePeriod period)
    {
        if (PlannerUi.SegmentedButton($"income-period-{period}", label, this.configuration.IncomePeriod == period))
        {
            this.configuration.IncomePeriod = period;
            this.saveConfiguration();
        }
    }

    private void DrawIncomeSortCombo()
    {
        string[] labels = ["Gross gil", "Gil / day", "Gil / voyage", "FC name"];
        var value = this.configuration.IncomeSort;
        if (DrawEnumCombo("##income-sort", labels, ref value))
        {
            this.configuration.IncomeSort = value;
            this.saveConfiguration();
        }
    }

    private TimeSpan? GetIncomePeriod() => this.configuration.IncomePeriod switch
    {
        IncomePeriod.Days7 => TimeSpan.FromDays(7),
        IncomePeriod.Days30 => TimeSpan.FromDays(30),
        IncomePeriod.Days90 => TimeSpan.FromDays(90),
        _ => null,
    };

    private static void DrawTableText(string text)
    {
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(text);
    }

    private static string FormatIncomeDate(DateTimeOffset? value)
        => value is null ? "—" : value.Value.LocalDateTime.ToString("g");

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

    private void RequestSetupFcSelection(string fcIdKey)
    {
        if (fcIdKey == this.selectedSetupFcId)
            return;
        if (!this.setupDraftDirty)
        {
            SelectSetupFc(fcIdKey);
            return;
        }
        this.pendingSetupFcId = fcIdKey;
        ImGui.OpenPopup("Unsaved FC changes###unsaved-fc-setup");
    }

    private void SelectSetupFc(string fcIdKey)
    {
        this.selectedSetupFcId = fcIdKey;
        var preferences = this.configuration.GetFcPreferences(fcIdKey);
        this.setupUseGlobalTarget = preferences.TargetRankOverride is null;
        this.setupTargetRank = preferences.TargetRankOverride ?? this.configuration.Settings.TargetRank;
        this.setupStrategy = preferences.StrategyOverride;
        this.setupDraftDirty = false;
    }

    private void SaveSetupDraft()
    {
        if (this.selectedSetupFcId is null)
            return;
        var preferences = this.configuration.GetFcPreferences(this.selectedSetupFcId);
        preferences.TargetRankOverride = this.setupUseGlobalTarget ? null : Math.Clamp(this.setupTargetRank, 1, this.catalog.MaximumRank);
        preferences.StrategyOverride = this.setupStrategy;
        this.setupDraftDirty = false;
        this.saveConfiguration();
        QueueRefresh(ForecastRefreshMode.Incremental);
    }

    private void DrawUnsavedSetupModal()
    {
        if (!ImGui.BeginPopupModal("Unsaved FC changes###unsaved-fc-setup", ImGuiWindowFlags.AlwaysAutoResize))
            return;
        ImGui.TextWrapped("Save the target and strategy changes before opening another free company?");
        ImGui.Spacing();
        if (PlannerUi.IconButtonWithText("save-switch-fc", FontAwesomeIcon.Check, "Save"))
        {
            SaveSetupDraft();
            if (this.pendingSetupFcId is { } pending)
                SelectSetupFc(pending);
            this.pendingSetupFcId = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (PlannerUi.IconButtonWithText("discard-switch-fc", FontAwesomeIcon.Trash, "Discard"))
        {
            if (this.pendingSetupFcId is { } pending)
                SelectSetupFc(pending);
            this.pendingSetupFcId = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (PlannerUi.IconButtonWithText("stay-on-fc", FontAwesomeIcon.Times, "Stay"))
        {
            this.pendingSetupFcId = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }
}
