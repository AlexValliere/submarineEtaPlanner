using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using SubmarineEtaPlanner.Planner;
using System.Numerics;

namespace SubmarineEtaPlanner.Ui;

public sealed partial class PlannerWindow
{
    private string? selectedUnlockFcId;
    private uint? selectedUnlockMapId;
    private uint? selectedUnlockSectorId;
    private string unlockSearch = string.Empty;
    private bool remainingUnlocksOnly;
    private readonly Dictionary<uint, UnlockMapLayoutCacheEntry> unlockMapLayoutCache = [];

    private void DrawUnlocksPage()
    {
        var currentSnapshot = EnsureFleetSnapshot();
        if (currentSnapshot is null)
            return;

        DrawFleetNotices(currentSnapshot);
        var orderedFcs = currentSnapshot.FreeCompanies
            .OrderByDescending(fc => this.configuration.GetFcPreferences(fc.FcIdKey).Favorite)
            .ThenBy(fc => fc.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (orderedFcs.Length == 0)
        {
            PlannerUi.Callout("unlock-no-fcs", FontAwesomeIcon.Building, "No free companies", "No FC unlock data is available from SubmarineTracker.", PlannerUi.Muted);
            return;
        }

        if (this.selectedUnlockFcId is null || orderedFcs.All(fc => fc.FcIdKey != this.selectedUnlockFcId))
        {
            this.selectedUnlockFcId = orderedFcs[0].FcIdKey;
            this.selectedUnlockMapId = null;
            this.selectedUnlockSectorId = null;
        }
        var selectedFc = orderedFcs.First(fc => fc.FcIdKey == this.selectedUnlockFcId);
        DrawUnlockFcSelector(orderedFcs, selectedFc);

        var presentation = UnlockMapPresentationBuilder.Build(
            selectedFc,
            this.routeSelectionCatalog.RouteDestinations,
            this.catalog.UnlockRules,
            DateTimeOffset.UtcNow);
        if (presentation.Maps.Count == 0)
        {
            PlannerUi.Callout("unlock-no-maps", FontAwesomeIcon.Map, "No map data", "No submarine destinations are available in the current game data.", PlannerUi.Amber);
            return;
        }

        DrawUnlockSearch(presentation);
        if (this.selectedUnlockMapId is null || presentation.Maps.All(map => map.MapId != this.selectedUnlockMapId))
        {
            this.selectedUnlockMapId = presentation.Maps
                .FirstOrDefault(map => map.RemainingDestinations > 0)?.MapId ??
                presentation.Maps[^1].MapId;
        }

        ImGui.Spacing();
        DrawUnlockSummary(presentation);
        if (!presentation.UnlockDataKnown)
        {
            ImGui.Spacing();
            PlannerUi.Callout(
                "unlock-data-unknown",
                FontAwesomeIcon.ExclamationTriangle,
                "Unlock state unavailable",
                "SubmarineTracker did not provide destination unlock data for this FC. The map is shown without progress counts or locked/unlocked conclusions.",
                PlannerUi.Amber);
        }

        ImGui.Spacing();
        DrawUnlockMapTabs(presentation.Maps);
        var selectedMap = presentation.Maps.First(map => map.MapId == this.selectedUnlockMapId);
        ImGui.Spacing();
        DrawUnlockLegend();
        ImGui.Spacing();
        DrawUnlockMapCanvas(presentation, selectedMap);
        DrawSelectedUnlockSector(presentation);
        ImGui.Spacing();
        DrawRemainingUnlocks(presentation, selectedMap);
    }

    private void DrawUnlockSearch(FcUnlockMapsPresentation presentation)
    {
        ImGui.SetNextItemWidth(Math.Min(360f * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X));
        ImGui.InputTextWithHint("##unlock-search", "Search sector code or name across maps…", ref this.unlockSearch, 100);
        PlannerUi.SameLineIfFits("Remaining only");
        ImGui.BeginDisabled(!presentation.UnlockDataKnown);
        ImGui.Checkbox("Remaining only", ref this.remainingUnlocksOnly);
        ImGui.EndDisabled();
        if (!presentation.UnlockDataKnown) PlannerUi.Tooltip("Tracker unlock state is required for this filter.");
        if (string.IsNullOrWhiteSpace(this.unlockSearch)) return;
        var results = UnlockMapSelection.Search(presentation, this.unlockSearch);
        if (results.Count == 0) PlannerUi.WrappedText("No matching sectors.", PlannerUi.Muted);
        else
        {
            if (ImGui.BeginChild("unlock-search-results", new Vector2(-1,
                    Math.Min(180f * ImGuiHelpers.GlobalScale, results.Count * ImGui.GetFrameHeightWithSpacing())), true))
            {
                foreach (var item in results)
                {
                    var destination = item.Destination;
                    if (ImGui.Selectable($"{destination.MapName} · {destination.Code} — {destination.Name}##search-{destination.SectorId}"))
                    {
                        this.selectedUnlockMapId = destination.MapId;
                        this.selectedUnlockSectorId = destination.SectorId;
                        this.unlockSearch = string.Empty;
                    }
                }
            }
            ImGui.EndChild();
        }
    }

    private void DrawSelectedUnlockSector(FcUnlockMapsPresentation presentation)
    {
        if (this.selectedUnlockSectorId is not { } selected) return;
        var destinations = presentation.Maps.SelectMany(map => map.Destinations).ToDictionary(item => item.Destination.SectorId);
        if (!destinations.TryGetValue(selected, out var item))
        {
            this.selectedUnlockSectorId = null;
            return;
        }
        ImGui.Spacing();
        if (ImGui.SmallButton("Clear sector selection")) this.selectedUnlockSectorId = null;
        var metadata = destinations.ToDictionary(pair => pair.Key, pair => pair.Value.Destination);
        BeginContentPanel("selected-unlock-sector");
        DrawUnlockDestinationContents(item, metadata);
        var path = item.RemainingUnlockPath
            .Concat(item.IncomingRule is { } incoming ? new[] { incoming.SourcePoint } : [])
            .Distinct().Where(point => point != selected).ToArray();
        if (path.Length > 0)
        {
            PlannerUi.WrappedText("Inspect discovery prerequisites", PlannerUi.Muted);
            foreach (var point in path)
            {
                if (!metadata.TryGetValue(point, out var prerequisite)) continue;
                if (ImGui.SmallButton($"{prerequisite.MapName} · {prerequisite.Code} — {prerequisite.Name}##inspect-{point}"))
                {
                    this.selectedUnlockMapId = prerequisite.MapId;
                    this.selectedUnlockSectorId = prerequisite.SectorId;
                }
            }
        }
        EndContentPanel();
    }

    private void DrawUnlockFcSelector(IReadOnlyList<FcState> freeCompanies, FcState selected)
    {
        ImGui.SetNextItemWidth(Math.Min(420f * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X));
        if (!ImGui.BeginCombo("Free company##unlock-fc", selected.DisplayName))
            return;

        foreach (var fc in freeCompanies)
        {
            var favoritePrefix = this.configuration.GetFcPreferences(fc.FcIdKey).Favorite ? "★ " : string.Empty;
            if (!ImGui.Selectable($"{favoritePrefix}{fc.DisplayName}##unlock-select-{fc.FcIdKey}", fc.FcIdKey == selected.FcIdKey))
                continue;
            this.selectedUnlockFcId = fc.FcIdKey;
            this.selectedUnlockMapId = null;
            this.selectedUnlockSectorId = null;
        }
        ImGui.EndCombo();
    }

    private void DrawUnlockSummary(FcUnlockMapsPresentation presentation)
    {
        var columns = ImGui.GetContentRegionAvail().X < 700f * ImGuiHelpers.GlobalScale ? 2 : 4;
        if (!ImGui.BeginTable("unlock-summary", columns, ImGuiTableFlags.SizingStretchSame))
            return;

        DrawUnlockSummaryMetric("unlock-total", presentation.TotalDestinations.ToString("N0"), "Total destinations", PlannerUi.Teal);
        DrawUnlockSummaryMetric("unlock-unlocked", FormatUnlockCount(presentation.UnlockedDestinations), "Unlocked", PlannerUi.Cyan);
        DrawUnlockSummaryMetric("unlock-explored", FormatUnlockCount(presentation.ExploredDestinations), "Explored", PlannerUi.Green);
        DrawUnlockSummaryMetric("unlock-remaining", FormatUnlockCount(presentation.RemainingDestinations), "Remaining", PlannerUi.Amber);
        ImGui.EndTable();
    }

    private void DrawUnlockSummaryMetric(string id, string value, string label, Vector4 color)
    {
        ImGui.TableNextColumn();
        PlannerUi.MetricCard(this.typography, id, FontAwesomeIcon.MapMarkerAlt, value, label, color);
    }

    private static string FormatUnlockCount(int? count) => count?.ToString("N0") ?? "Unknown";

    private void DrawUnlockMapTabs(IReadOnlyList<UnlockMapPresentation> maps)
    {
        var available = ImGui.GetContentRegionAvail().X;
        var used = 0f;
        foreach (var map in maps)
        {
            var suffix = map.RemainingDestinations is { } remaining ? $" · {remaining}" : string.Empty;
            var label = $"{map.MapName}{suffix}";
            var width = ImGui.CalcTextSize(label).X + 20f * ImGuiHelpers.GlobalScale;
            if (used > 0f && used + width > available)
                used = 0f;
            else if (used > 0f)
                ImGui.SameLine(0, 4f * ImGuiHelpers.GlobalScale);

            if (PlannerUi.SegmentedButton($"unlock-map-{map.MapId}", label, map.MapId == this.selectedUnlockMapId))
            {
                if (this.selectedUnlockMapId != map.MapId) this.selectedUnlockSectorId = null;
                this.selectedUnlockMapId = map.MapId;
            }
            used += width + 4f * ImGuiHelpers.GlobalScale;
        }
    }

    private static void DrawUnlockLegend()
    {
        PlannerUi.DrawStatusPill("Explored", PlannerUi.Green);
        PlannerUi.SameLineIfFits("Unlocked");
        PlannerUi.DrawStatusPill("Unlocked", PlannerUi.Cyan);
        PlannerUi.SameLineIfFits("Discoverable now");
        PlannerUi.DrawStatusPill("Discoverable now", PlannerUi.Amber);
        PlannerUi.SameLineIfFits("Locked");
        PlannerUi.DrawStatusPill("Locked", PlannerUi.Muted);
        PlannerUi.SameLineIfFits("Active attempt");
        PlannerUi.DrawStatusPill("Active attempt", PlannerUi.Cyan);
    }

    private void DrawUnlockMapCanvas(FcUnlockMapsPresentation presentation, UnlockMapPresentation map)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var canvasSize = new Vector2(
            Math.Max(1f, ImGui.GetContentRegionAvail().X),
            Math.Clamp(ImGui.GetContentRegionAvail().X * 0.54f, 370f * scale, 560f * scale));
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + canvasSize;
        var drawList = ImGui.GetWindowDrawList();
        var background = PlannerUi.PanelBackground;
        drawList.AddRectFilled(origin, end, ImGui.ColorConvertFloat4ToU32(background), 7f * scale);
        drawList.AddRect(origin, end, ImGui.ColorConvertFloat4ToU32(PlannerUi.Border), 7f * scale, ImDrawFlags.None, 1.2f * scale);

        var nodeRadius = 18f * scale;
        var positions = GetUnlockMapPositions(
            map,
            canvasSize.X,
            canvasSize.Y,
            50f * scale,
            nodeRadius);
        var allDestinations = presentation.Maps
            .SelectMany(candidate => candidate.Destinations)
            .ToDictionary(destination => destination.Destination.SectorId);
        var allMetadata = allDestinations.ToDictionary(pair => pair.Key, pair => pair.Value.Destination);
        var visible = UnlockMapSelection.Visible(presentation, this.remainingUnlocksOnly, this.selectedUnlockSectorId);
        var selectedPath = UnlockMapSelection.Path(presentation, this.selectedUnlockSectorId);

        drawList.PushClipRect(origin, end, true);
        void DrawArrow(Vector2 from, Vector2 to, Vector4 color, bool trimStart, bool trimEnd)
        {
            var delta = to - from;
            var length = delta.Length();
            if (length < 1f)
                return;
            var direction = delta / length;
            var start = trimStart ? from + direction * nodeRadius : from;
            var finish = trimEnd ? to - direction * nodeRadius : to;
            var packed = ImGui.ColorConvertFloat4ToU32(color);
            drawList.AddLine(start, finish, packed, 1.6f * scale);
            var perpendicular = new Vector2(-direction.Y, direction.X);
            var arrowLength = 7f * scale;
            var arrowWidth = 4f * scale;
            drawList.AddLine(finish, finish - direction * arrowLength + perpendicular * arrowWidth, packed, 1.6f * scale);
            drawList.AddLine(finish, finish - direction * arrowLength - perpendicular * arrowWidth, packed, 1.6f * scale);
        }

        foreach (var connection in map.Connections)
        {
            if (!visible.Contains(connection.SourcePoint) || !visible.Contains(connection.TargetPoint)) continue;
            var sourceLocal = positions.TryGetValue(connection.SourcePoint, out var sourcePoint);
            var targetLocal = positions.TryGetValue(connection.TargetPoint, out var targetPoint);
            var targetState = allDestinations.GetValueOrDefault(connection.TargetPoint)?.State ?? UnlockDestinationState.Unknown;
            var color = selectedPath.Contains(connection.SourcePoint) && selectedPath.Contains(connection.TargetPoint)
                ? PlannerUi.Teal : UnlockStateColor(targetState) with { W = this.selectedUnlockSectorId is null ? 0.5f : 0.22f };
            if (sourceLocal && targetLocal)
            {
                DrawArrow(
                    origin + new Vector2(sourcePoint.X, sourcePoint.Y),
                    origin + new Vector2(targetPoint.X, targetPoint.Y),
                    color,
                    trimStart: true,
                    trimEnd: true);
                continue;
            }

            if (targetLocal && allMetadata.TryGetValue(connection.SourcePoint, out var sourceMetadata))
            {
                var target = origin + new Vector2(targetPoint.X, targetPoint.Y);
                var entry = new Vector2(origin.X + 10f * scale, target.Y);
                DrawArrow(entry, target, color, trimStart: false, trimEnd: true);
                drawList.AddText(
                    entry + new Vector2(4f, -18f) * scale,
                    ImGui.ColorConvertFloat4ToU32(PlannerUi.Muted),
                    $"{sourceMetadata.MapName} →");
            }
            else if (sourceLocal && allMetadata.TryGetValue(connection.TargetPoint, out var targetMetadata))
            {
                var source = origin + new Vector2(sourcePoint.X, sourcePoint.Y);
                var exit = new Vector2(end.X - 10f * scale, source.Y);
                DrawArrow(source, exit, color, trimStart: true, trimEnd: false);
                var label = $"→ {targetMetadata.MapName}";
                var labelSize = ImGui.CalcTextSize(label);
                drawList.AddText(
                    exit - new Vector2(labelSize.X + 4f * scale, 18f * scale),
                    ImGui.ColorConvertFloat4ToU32(PlannerUi.Muted),
                    label);
            }
        }

        foreach (var destination in map.Destinations)
        {
            if (!visible.Contains(destination.Destination.SectorId)) continue;
            var relative = positions[destination.Destination.SectorId];
            var center = origin + new Vector2(relative.X, relative.Y);
            var color = UnlockStateColor(destination.State);
            if (this.selectedUnlockSectorId is not null && !selectedPath.Contains(destination.Destination.SectorId))
                color.W = .45f;
            if (this.selectedUnlockSectorId == destination.Destination.SectorId)
                drawList.AddCircle(center, nodeRadius + 8f * scale, ImGui.ColorConvertFloat4ToU32(PlannerUi.Teal), 32, 2.5f * scale);
            if (destination.HasActiveAttempt)
                drawList.AddCircle(center, nodeRadius + 5f * scale, ImGui.ColorConvertFloat4ToU32(PlannerUi.Cyan), 32, 2.2f * scale);
            drawList.AddCircleFilled(
                center,
                nodeRadius,
                ImGui.ColorConvertFloat4ToU32(PlannerTheme.WithAlpha(color, color.W * (destination.State == UnlockDestinationState.Locked ? 0.10f : 0.22f))),
                32);
            drawList.AddCircle(center, nodeRadius, ImGui.ColorConvertFloat4ToU32(color), 32, 1.7f * scale);
            var labelSize = ImGui.CalcTextSize(destination.Destination.Code);
            drawList.AddText(center - (labelSize / 2f), ImGui.ColorConvertFloat4ToU32(PlannerTheme.Text), destination.Destination.Code);
        }
        drawList.PopClipRect();

        ImGui.InvisibleButton($"##unlock-canvas-{map.MapId}", canvasSize);
        if (!ImGui.IsItemHovered())
            return;

        var mouse = ImGui.GetMousePos();
        var hovered = map.Destinations
            .Where(destination => visible.Contains(destination.Destination.SectorId))
            .Select(destination => (Destination: destination, Point: positions[destination.Destination.SectorId]))
            .Where(item => Vector2.Distance(mouse, origin + new Vector2(item.Point.X, item.Point.Y)) <= nodeRadius + 3f * scale)
            .OrderBy(item => Vector2.Distance(mouse, origin + new Vector2(item.Point.X, item.Point.Y)))
            .Select(item => item.Destination)
            .FirstOrDefault();
        if (hovered is not null)
        {
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                this.selectedUnlockSectorId = hovered.Destination.SectorId;
            DrawUnlockDestinationTooltip(hovered, allMetadata);
        }
    }

    private IReadOnlyDictionary<uint, UnlockMapCanvasPoint> GetUnlockMapPositions(
        UnlockMapPresentation map,
        float width,
        float height,
        float padding,
        float nodeRadius)
    {
        var destinations = map.Destinations.Select(destination => destination.Destination).ToArray();
        var fingerprint = CalculateUnlockMapLayoutFingerprint(destinations, map.Connections);
        if (this.unlockMapLayoutCache.TryGetValue(map.MapId, out var cached) &&
            Math.Abs(cached.Width - width) < 0.5f &&
            Math.Abs(cached.Height - height) < 0.5f &&
            Math.Abs(cached.Padding - padding) < 0.5f &&
            Math.Abs(cached.NodeRadius - nodeRadius) < 0.1f &&
            cached.Fingerprint == fingerprint)
        {
            return cached.Positions;
        }

        var positions = UnlockMapLayoutCalculator.Calculate(
            destinations,
            map.Connections,
            width,
            height,
            padding,
            nodeRadius);
        this.unlockMapLayoutCache[map.MapId] = new UnlockMapLayoutCacheEntry(
            width,
            height,
            padding,
            nodeRadius,
            fingerprint,
            positions);
        return positions;
    }

    private static ulong CalculateUnlockMapLayoutFingerprint(
        IReadOnlyList<RouteDestination> destinations,
        IReadOnlyList<UnlockMapConnection> connections)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var value = offset;
        void Add(uint part)
        {
            value ^= part;
            value *= prime;
        }

        foreach (var destination in destinations.OrderBy(destination => destination.SectorId))
        {
            Add(destination.SectorId);
            Add(destination.MapPosition is { } position ? BitConverter.SingleToUInt32Bits(position.X) : uint.MaxValue);
            Add(destination.MapPosition is { } mapPosition ? BitConverter.SingleToUInt32Bits(mapPosition.Z) : uint.MaxValue);
        }
        foreach (var connection in connections
                     .Where(connection => !connection.CrossesMaps)
                     .OrderBy(connection => connection.SourcePoint)
                     .ThenBy(connection => connection.TargetPoint))
        {
            Add(connection.SourcePoint);
            Add(connection.TargetPoint);
        }
        return value;
    }

    private void DrawRemainingUnlocks(FcUnlockMapsPresentation presentation, UnlockMapPresentation map)
    {
        BeginSettingsCard(
            $"unlock-remaining-{map.MapId}",
            "Remaining to unlock",
            $"Locked destinations and their remaining discovery path on {map.MapName}.");
        if (!presentation.UnlockDataKnown)
        {
            ImGui.TextColored(PlannerUi.Muted, "Remaining destinations cannot be determined until SubmarineTracker provides this FC's unlock state.");
            EndSettingsCard();
            return;
        }

        var remaining = map.Destinations.Where(destination => destination.IsRemaining).ToArray();
        if (remaining.Length == 0)
        {
            PlannerUi.IconText(FontAwesomeIcon.CheckCircle, "Every destination on this map is unlocked.", PlannerUi.Green);
            EndSettingsCard();
            return;
        }

        var metadata = presentation.Maps.SelectMany(candidate => candidate.Destinations)
            .ToDictionary(destination => destination.Destination.SectorId, destination => destination.Destination);
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable |
                    ImGuiTableFlags.ScrollX | ImGuiTableFlags.SizingFixedFit;
        var tableHeight = Math.Min(330f, 34f + remaining.Length * 29f) * ImGuiHelpers.GlobalScale;
        if (ImGui.BeginTable($"unlock-remaining-table-{map.MapId}", 6, flags, new Vector2(-1, tableHeight), 920f * ImGuiHelpers.GlobalScale))
        {
            ImGui.TableSetupColumn("Destination", ImGuiTableColumnFlags.WidthFixed, 180f * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("State", ImGuiTableColumnFlags.WidthFixed, 155f * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Unlock from", ImGuiTableColumnFlags.WidthFixed, 150f * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Required rank", ImGuiTableColumnFlags.WidthFixed, 105f * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Remaining path", ImGuiTableColumnFlags.WidthStretch, 250f * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Special / attempt", ImGuiTableColumnFlags.WidthFixed, 180f * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupScrollFreeze(1, 1);
            ImGui.TableHeadersRow();
            foreach (var destination in remaining)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if (ImGui.Selectable($"{destination.Destination.Code} — {destination.Destination.Name}##remaining-{destination.Destination.SectorId}"))
                    this.selectedUnlockSectorId = destination.Destination.SectorId;
                DrawTableText(UnlockStateLabel(destination.State, destination.HasActiveAttempt));
                DrawTableText(destination.IncomingRule is { } rule && metadata.TryGetValue(rule.SourcePoint, out var source)
                    ? $"{source.Code} — {source.Name}"
                    : "Initial destination");
                DrawTableText($"R{destination.Destination.RequiredRank}");
                DrawTableText(FormatUnlockPath(destination.RemainingUnlockPath, metadata));
                DrawTableText(FormatUnlockSpecial(destination));
            }
            ImGui.EndTable();
        }
        EndSettingsCard();
    }

    private void DrawUnlockDestinationTooltip(
        UnlockMapDestinationPresentation destination,
        IReadOnlyDictionary<uint, RouteDestination> metadata)
    {
        PlannerUi.BeginTooltip();
        DrawUnlockDestinationContents(destination, metadata);
        PlannerUi.EndTooltip();
    }

    private void DrawUnlockDestinationContents(UnlockMapDestinationPresentation destination,
        IReadOnlyDictionary<uint, RouteDestination> metadata)
    {
        PlannerUi.WrappedText($"{destination.Destination.Code} — {destination.Destination.Name}", UnlockStateColor(destination.State));
        ImGui.TextUnformatted($"{destination.Destination.MapName} · R{destination.Destination.RequiredRank}");
        ImGui.Separator();
        ImGui.TextUnformatted($"State: {UnlockStateLabel(destination.State, destination.HasActiveAttempt)}");
        if (destination.IncomingRule is { } rule)
        {
            var source = metadata.GetValueOrDefault(rule.SourcePoint);
            PlannerUi.WrappedText($"Unlock from: {source?.Name ?? this.catalog.PointName(rule.SourcePoint)}");
        }
        else
        {
            ImGui.TextColored(PlannerUi.Muted, "Initial destination; no discovery source is recorded.");
        }

        if (destination.State is UnlockDestinationState.Discoverable or UnlockDestinationState.Locked &&
            destination.RemainingUnlockPath.Count > 0)
        {
            ImGui.TextWrapped($"Remaining path: {FormatUnlockPath(destination.RemainingUnlockPath, metadata)}");
        }
        else if (destination.State == UnlockDestinationState.Unlocked)
        {
            ImGui.TextColored(
                PlannerUi.Teal,
                $"Next step: Explore {destination.Destination.Code} — {destination.Destination.Name}.");
        }

        var blocked = FormatUnlockBlockReason(destination, metadata);
        if (blocked is not null)
            PlannerUi.WrappedText(blocked, PlannerUi.Amber);
        if (destination.IncomingRule?.UnlocksSubSlot == true)
            ImGui.TextColored(PlannerUi.Teal, "Exploring this destination unlocks a submarine slot.");
        if (destination.IncomingRule?.UnlocksMap == true)
            ImGui.TextColored(PlannerUi.Teal, "Exploring this destination unlocks the next map.");
        foreach (var attempt in destination.ActiveAttempts)
        {
            PlannerUi.WrappedText(
                $"{attempt.SubmarineName} is attempting this unlock · returns {attempt.ReturnAtUtc.LocalDateTime:g} ({FormatRelative(attempt.ReturnAtUtc, DateTimeOffset.UtcNow)})", PlannerUi.Cyan);
        }
    }

    private static string? FormatUnlockBlockReason(
        UnlockMapDestinationPresentation destination,
        IReadOnlyDictionary<uint, RouteDestination> metadata)
        => destination.BlockReason switch
        {
            UnlockDestinationBlockReason.InitialDestination => "SubmarineTracker does not report this initial destination as unlocked.",
            UnlockDestinationBlockReason.SourceLocked => $"Unlock {PointLabel(destination.BlockingPoint, metadata)} first.",
            UnlockDestinationBlockReason.EarlierSibling => $"Discover {PointLabel(destination.BlockingPoint, metadata)} first; this source unlocks its missing destinations in order.",
            UnlockDestinationBlockReason.FleetRank => $"Requires a submarine able to visit the source at R{destination.IncomingRule?.SourceRequiredRank}.",
            _ => null,
        };

    private static string PointLabel(uint? point, IReadOnlyDictionary<uint, RouteDestination> metadata)
        => point is { } value && metadata.TryGetValue(value, out var destination)
            ? $"{destination.Code} — {destination.Name}"
            : point?.ToString() ?? "the preceding destination";

    private static string FormatUnlockPath(
        IReadOnlyList<uint> path,
        IReadOnlyDictionary<uint, RouteDestination> metadata)
    {
        var mapCount = path
            .Select(point => metadata.GetValueOrDefault(point)?.MapId)
            .Where(mapId => mapId is not null)
            .Distinct()
            .Count();
        return string.Join(" → ", path.Select(point =>
        {
            if (!metadata.TryGetValue(point, out var destination))
                return point.ToString();
            return mapCount > 1
                ? $"{destination.MapName}:{destination.Code}"
                : destination.Code;
        }));
    }

    private static string FormatUnlockSpecial(UnlockMapDestinationPresentation destination)
    {
        var values = new List<string>();
        if (destination.IncomingRule?.UnlocksSubSlot == true)
            values.Add("Sub slot");
        if (destination.IncomingRule?.UnlocksMap == true)
            values.Add("Next map");
        if (destination.HasActiveAttempt)
            values.Add($"{destination.ActiveAttempts.Count} active");
        return values.Count == 0 ? "—" : string.Join(" · ", values);
    }

    private static string UnlockStateLabel(UnlockDestinationState state, bool activeAttempt)
    {
        var label = state switch
        {
            UnlockDestinationState.Unknown => "Unknown",
            UnlockDestinationState.Explored => "Explored",
            UnlockDestinationState.Unlocked => "Unlocked · not explored",
            UnlockDestinationState.Discoverable => "Discoverable now",
            _ => "Locked",
        };
        return activeAttempt ? $"{label} · attempt underway" : label;
    }

    private static Vector4 UnlockStateColor(UnlockDestinationState state)
        => state switch
        {
            UnlockDestinationState.Explored => PlannerUi.Green,
            UnlockDestinationState.Unlocked => PlannerUi.Cyan,
            UnlockDestinationState.Discoverable => PlannerUi.Amber,
            UnlockDestinationState.Locked => PlannerUi.Muted,
            _ => PlannerUi.Muted,
        };

    private sealed record UnlockMapLayoutCacheEntry(
        float Width,
        float Height,
        float Padding,
        float NodeRadius,
        ulong Fingerprint,
        IReadOnlyDictionary<uint, UnlockMapCanvasPoint> Positions);
}
