using SubmarineEtaPlanner.Planner;
using Xunit;

namespace SubmarineEtaPlanner.Tests;

public sealed class FleetReworkTests
{
    [Theory]
    [InlineData(FcStrategyPreset.Recommended, EtaModel.PracticalLeveling, RouteGoal.UnlockLevelingRoutesThenLevel, true)]
    [InlineData(FcStrategyPreset.ImmediateExpOnly, EtaModel.ExactRouteSearch, RouteGoal.FastestLevelingOnly, false)]
    [InlineData(FcStrategyPreset.SlotsFirstThenImmediateExp, EtaModel.ExactRouteSearch, RouteGoal.UnlockSubSlotsThenLevel, true)]
    [InlineData(FcStrategyPreset.UnlockEverythingThenLevel, EtaModel.ExactRouteSearch, RouteGoal.UnlockEverythingThenLevel, true)]
    public void EffectiveSettingsApplyEveryStrategyPreset(
        FcStrategyPreset preset,
        EtaModel expectedModel,
        RouteGoal expectedGoal,
        bool expectedSlotPriority)
    {
        var global = EtaSettings.CreateDefault() with { TargetRank = 90 };

        var effective = EffectiveEtaSettingsResolver.Resolve(
            global,
            new FcSimulationOverride(999, preset),
            maximumRank: 120);

        Assert.Equal(120, effective.TargetRank);
        Assert.Equal(expectedModel, effective.EtaModel);
        Assert.Equal(expectedGoal, effective.RouteGoal);
        Assert.Equal(expectedSlotPriority, effective.PrioritizeSubSlots);
        Assert.NotSame(global, effective);
    }

    [Fact]
    public void EffectiveSettingsInheritWithoutMutatingGlobal()
    {
        var global = EtaSettings.CreateDefault() with { TargetRank = 85 };
        var effective = EffectiveEtaSettingsResolver.Resolve(global, null, 120);

        effective.TargetRank = 100;

        Assert.Equal(85, global.TargetRank);
        Assert.Equal(global.RouteGoal, effective.RouteGoal);
    }

    [Theory]
    [InlineData(50, true, true, "Collect now; send the modeled route after synchronization", "To collect")]
    [InlineData(50, false, false, "Send recommended leveling route now", "Idle")]
    [InlineData(100, true, true, "Collect and resend farming route now", "To collect")]
    [InlineData(100, false, true, "Resend farming route after collection", "Underway")]
    public void ActionProjectionUsesRankAndVoyageState(
        int rank,
        bool returned,
        bool hasRoute,
        string expectedAction,
        string expectedCompactState)
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(1);
        var route = hasRoute ? new uint[] { 1 } : [];
        var returnAt = returned ? now.AddMinutes(-1) : hasRoute ? now.AddHours(2) : DateTimeOffset.MinValue;
        var fc = CreateFc(rank, returnAt, route, currentKnown: true);
        var projection = FleetPresentationBuilder.Create(fc, CreateResult(fc, 90, now),
            EtaSettings.CreateDefault() with { TargetRank = 90 }, new StubCatalog(), now);

        var submarine = Assert.Single(projection.Submarines);
        Assert.Equal(expectedAction, submarine.ActionLabel);
        var compactState = CompactOperationalStatePresentation.Create(submarine);
        Assert.Equal(expectedCompactState, compactState.Label);
        Assert.Contains(expectedAction, compactState.Tooltip);
        Assert.Equal(rank >= 90 ? FleetMode.Farming : FleetMode.Leveling, projection.Mode);
    }

    [Fact]
    public void SyncingVoyageExplainsTrackerDependency()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(1);
        var fc = CreateFc(50, now.AddHours(2), [], currentKnown: false);

        var submarine = Assert.Single(FleetPresentationBuilder.Create(
            fc,
            CreateResult(fc, 90, now),
            EtaSettings.CreateDefault() with { TargetRank = 90 },
            new StubCatalog(),
            now).Submarines);

        Assert.Equal(OperationalState.Syncing, submarine.State);
        Assert.Contains("SubmarineTracker", submarine.ActionLabel);
        Assert.Equal("Syncing", CompactOperationalStatePresentation.Create(submarine).Label);
    }

    [Fact]
    public void TargetReadySubmarineWithoutKnownRouteRequiresAChoice()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(1);
        var fc = CreateFc(90, DateTimeOffset.MinValue, [], currentKnown: true);

        var submarine = Assert.Single(FleetPresentationBuilder.Create(
            fc,
            CreateResult(fc, 90, now),
            EtaSettings.CreateDefault() with { TargetRank = 90 },
            new StubCatalog(),
            now).Submarines);

        Assert.Equal("Choose farming route", submarine.ActionLabel);
        Assert.Empty(submarine.DisplayedRoute);
        Assert.Equal("Idle", CompactOperationalStatePresentation.Create(submarine).Label);
    }

    [Theory]
    [InlineData(64, 64, "R64")]
    [InlineData(66, 67, "R66 → R67")]
    public void OperationsRankCombinesCurrentAndProjectedRank(int rank, int projectedRank, string expected)
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(1);
        var fc = CreateFc(rank, now.AddHours(1), [1], currentKnown: true);
        var submarine = Assert.Single(FleetPresentationBuilder.Create(
            fc,
            CreateResult(fc, 90, now) with
            {
                PerSubResults = [CreateResult(fc, 90, now).PerSubResults[0] with { FinalRank = projectedRank }],
            },
            EtaSettings.CreateDefault() with { TargetRank = 90 },
            new StubCatalog(),
            now).Submarines);
        submarine = submarine with { ProjectedRank = projectedRank };

        Assert.Equal(expected, OperationsRankPresentation.Create(submarine).Label);
    }

    [Fact]
    public void OperationsRankMarksUnavailableProjection()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(1);
        var fc = CreateFc(64, DateTimeOffset.MinValue, [], currentKnown: true);
        var submarine = Assert.Single(FleetPresentationBuilder.Create(
            fc, null, EtaSettings.CreateDefault() with { TargetRank = 90 }, new StubCatalog(), now).Submarines);

        var rank = OperationsRankPresentation.Create(submarine);

        Assert.Equal("R64 → ?", rank.Label);
        Assert.NotNull(rank.Tooltip);
    }

    [Fact]
    public void OperationsCompletionUsesPlainLanguageInsteadOfPercentileNames()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(10);
        var fc = CreateFc(50, DateTimeOffset.MinValue, [], currentKnown: true);
        var projection = FleetPresentationBuilder.Create(
            fc, CreateResult(fc, 90, now), EtaSettings.CreateDefault() with { TargetRank = 90 }, new StubCatalog(), now) with
        {
            CompletionP10AtUtc = now.AddDays(2),
            CompletionP50AtUtc = now.AddDays(4),
            CompletionP90AtUtc = now.AddDays(7),
        };

        var completion = OperationsCompletionPresentation.Create(projection);

        Assert.Contains("Expected ready around", completion.Label);
        Assert.Contains("Likely between", completion.Label);
        Assert.DoesNotContain("P10", completion.Label);
        Assert.DoesNotContain("P90", completion.Label);
    }

    [Fact]
    public void OperationsModeFilterIncludesOnlyRequestedFleetType()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(10);
        var levelingFc = CreateFc(50, DateTimeOffset.MinValue, [], currentKnown: true);
        var farmingFc = CreateFc(100, DateTimeOffset.MinValue, [], currentKnown: true);
        var settings = EtaSettings.CreateDefault() with { TargetRank = 90 };
        var catalog = new StubCatalog();
        var leveling = FleetPresentationBuilder.Create(levelingFc, CreateResult(levelingFc, 90, now), settings, catalog, now);
        var farming = FleetPresentationBuilder.Create(farmingFc, CreateResult(farmingFc, 90, now), settings, catalog, now);

        Assert.True(FleetPresentationFiltering.Includes(leveling, null));
        Assert.True(FleetPresentationFiltering.Includes(leveling, FleetMode.Leveling));
        Assert.False(FleetPresentationFiltering.Includes(leveling, FleetMode.Farming));
        Assert.True(FleetPresentationFiltering.Includes(farming, FleetMode.Farming));
    }

    [Fact]
    public void OperationsHeaderUsesAlignedValuesAndRankOnlyRoster()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(10);
        var original = CreateFc(142, now.AddHours(2), [1], currentKnown: true);
        var submarines = new[] { 142, 143, 141, 140 }
            .Select((rank, index) => original.Submarines[0] with
            {
                SubmarineId = index + 1,
                Name = $"Named submarine {index + 1}",
                Rank = rank,
            })
            .ToArray();
        var fc = original with { FreeCompanyTag = "TEST", World = "Cerberus", Submarines = submarines };
        var projection = FleetPresentationBuilder.Create(
            fc,
            null,
            EtaSettings.CreateDefault() with { TargetRank = 114 },
            new StubCatalog { SupportedMaximumRank = 150 },
            now);

        var header = OperationsFcHeaderPresentation.Create(projection, favorite: true, now);

        Assert.Equal("★ TEST", header.FreeCompany);
        Assert.Equal("Cerberus", header.World);
        Assert.Equal("4 farming", header.Mode);
        Assert.Equal("In 2h 0m", header.Attention);
        Assert.Equal("Ready", header.FarmReady);
        Assert.Equal("R142 · R143 · R141 · R140", header.Ranks);
        Assert.DoesNotContain("Named submarine", header.Ranks);
        Assert.True(header.IsFarming);
    }

    [Fact]
    public void OperationsHeaderShowsImmediateActionsAndFarmReadyEta()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(10);
        var fc = CreateFc(50, DateTimeOffset.MinValue, [], currentKnown: true);
        var projection = FleetPresentationBuilder.Create(
            fc,
            CreateResult(fc, 90, now),
            EtaSettings.CreateDefault() with { TargetRank = 90 },
            new StubCatalog(),
            now);

        var header = OperationsFcHeaderPresentation.Create(projection, favorite: false, now);

        Assert.Equal("1 action now", header.Attention);
        Assert.Equal("1h 0m", header.FarmReady);
        Assert.Equal("R50", header.Ranks);
        Assert.True(header.HasImmediateActions);
        Assert.False(header.IsFarming);
    }

    [Fact]
    public void FleetProjectionUsesCurrentTrackedBuildAndExplainsMissingBuildData()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(10);
        var fc = CreateFc(115, now.AddHours(2), [1], currentKnown: true);
        var settings = EtaSettings.CreateDefault() with { TargetRank = 120 };

        var resolved = Assert.Single(FleetPresentationBuilder.Create(
            fc,
            CreateResult(fc, 120, now),
            settings,
            new StubCatalog { BuildCode = "WSCU++" },
            now).Submarines);

        Assert.Equal("WSCU++", resolved.CurrentBuild.Code);
        Assert.Null(resolved.CurrentBuild.UnavailableReason);

        var incompleteFc = fc with
        {
            Submarines = [fc.Submarines[0] with { BuildParts = SubmarineBuildParts.Empty }],
        };
        var incomplete = Assert.Single(FleetPresentationBuilder.Create(
            incompleteFc,
            CreateResult(incompleteFc, 120, now),
            settings,
            new StubCatalog(),
            now).Submarines);

        Assert.Equal("—", incomplete.CurrentBuild.Code);
        Assert.NotNull(incomplete.CurrentBuild.UnavailableReason);
    }

    [Fact]
    public void FarmingVoyageProjectsPastTheFcTargetToCatalogMaximum()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(1);
        var fc = CreateFc(90, now.AddHours(2), [1], currentKnown: true);

        var submarine = Assert.Single(FleetPresentationBuilder.Create(
            fc,
            CreateResult(fc, 90, now),
            EtaSettings.CreateDefault() with { TargetRank = 90 },
            new StubCatalog(),
            now).Submarines);

        Assert.Equal((uint)1_000, submarine.ExpectedExp);
        Assert.Equal(100, submarine.ProjectedRank);
    }

    [Fact]
    public void IncomeIncludesZeroGilVoyagesInDenominatorAndHonorsWindow()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(100);
        var fc = CreateFc(90, DateTimeOffset.MinValue, [], currentKnown: true);
        var submarine = fc.Submarines[0] with
        {
            Salvage = new SubmarineSalvageSummary(3, now.AddDays(-40), now.AddDays(-1), [])
            {
                Voyages =
                [
                    new(fc.FcIdKey, 1, now.AddDays(-40), [new SalvageItemTotal(1, "Old", 100, 10)]),
                    new(fc.FcIdKey, 1, now.AddDays(-2), [new SalvageItemTotal(1, "Salvage", 100, 10)]),
                    new(fc.FcIdKey, 1, now.AddDays(-1), []),
                ],
            },
        };
        fc = fc with { Submarines = [submarine] };

        var metrics = IncomeMetricsCalculator.Calculate(fc, now, TimeSpan.FromDays(30));

        Assert.Equal(1_000, metrics.GrossGil);
        Assert.Equal(2, metrics.ValidVoyages);
        Assert.Equal(500, metrics.GilPerVoyage);
        Assert.Equal(now.AddDays(-2), metrics.FirstReturnAtUtc);
    }

    [Fact]
    public void OperationsOrderingKeepsFavoritesFirstThenActionsAndReturns()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(10);
        var catalog = new StubCatalog();
        var settings = EtaSettings.CreateDefault() with { TargetRank = 90 };
        FcOperationalProjection Projection(string tag, DateTimeOffset returnAt, IReadOnlyList<uint> route)
        {
            var fc = CreateFc(50, returnAt, route, currentKnown: true) with { FreeCompanyTag = tag };
            return FleetPresentationBuilder.Create(fc, CreateResult(fc, 90, now), settings, catalog, now);
        }
        var futureLate = Projection("Future late", now.AddHours(5), [1]);
        var immediate = Projection("Immediate", DateTimeOffset.MinValue, []);
        var futureEarly = Projection("Future early", now.AddHours(1), [1]);
        var favoriteLate = Projection("Favorite", now.AddHours(10), [1]);

        var ordered = FleetPresentationOrdering.ActionsFirst(
            [futureLate, immediate, favoriteLate, futureEarly],
            projection => projection.State.FreeCompanyTag == "Favorite");

        Assert.Equal(["Favorite", "Immediate", "Future early", "Future late"],
            ordered.Select(projection => projection.State.FreeCompanyTag).ToArray());
    }

    [Fact]
    public void FarmReadyOrderingIgnoresImmediateActions()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(10);
        var settings = EtaSettings.CreateDefault() with { TargetRank = 90 };
        var catalog = new StubCatalog();
        FcOperationalProjection Projection(string tag, int rank, DateTimeOffset completion, bool immediate)
        {
            var fc = CreateFc(rank, immediate ? DateTimeOffset.MinValue : now.AddHours(2), immediate ? [] : [1], currentKnown: true) with
            {
                FreeCompanyTag = tag,
            };
            return FleetPresentationBuilder.Create(fc, CreateResult(fc, 90, now), settings, catalog, now) with
            {
                CompletionP50AtUtc = completion,
            };
        }

        var actionLate = Projection("Action late", 50, now.AddDays(8), immediate: true);
        var early = Projection("Early", 50, now.AddDays(2), immediate: false);
        var unavailable = Projection("Unavailable", 50, DateTimeOffset.MaxValue, immediate: false) with { CompletionP50AtUtc = null };
        var ready = Projection("Ready", 100, now, immediate: false);

        var ordered = FleetPresentationOrdering.FarmReadyEta(
            [actionLate, unavailable, early, ready],
            _ => false);

        Assert.Equal(["Ready", "Early", "Action late", "Unavailable"],
            ordered.Select(projection => projection.State.FreeCompanyTag).ToArray());
    }

    [Fact]
    public void NameOrderingIgnoresImmediateActionsAndKeepsFavoritesFirst()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(10);
        var settings = EtaSettings.CreateDefault() with { TargetRank = 90 };
        var catalog = new StubCatalog();
        FcOperationalProjection Projection(string tag, bool immediate)
        {
            var fc = CreateFc(50, immediate ? DateTimeOffset.MinValue : now.AddHours(2), immediate ? [] : [1], currentKnown: true) with
            {
                FreeCompanyTag = tag,
            };
            return FleetPresentationBuilder.Create(fc, CreateResult(fc, 90, now), settings, catalog, now);
        }

        var ordered = FleetPresentationOrdering.ByName(
            [Projection("Zulu action", true), Projection("Alpha", false), Projection("Middle favorite", false)],
            projection => projection.State.FreeCompanyTag == "Middle favorite");

        Assert.Equal(["Middle favorite", "Alpha", "Zulu action"],
            ordered.Select(projection => projection.State.FreeCompanyTag).ToArray());
    }

    [Theory]
    [InlineData(null, "Best available EXP/hour")]
    [InlineData(UnlockObjectiveKind.SectorUnlock, "Unlock Point 3")]
    [InlineData(UnlockObjectiveKind.ExploreSubmarineSlot, "Unlock submarine slot")]
    [InlineData(UnlockObjectiveKind.MainProgression, "Continue map progression")]
    public void VoyagePurposeExplainsWhyRouteWasSelected(UnlockObjectiveKind? kind, string expected)
    {
        var plan = new VoyagePlan(
            1, "Sub", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1), "TEST", [2], 100,
            10, 11, 0, 0, [], [], TimeSpan.FromHours(1), 100, EtaModel.PracticalLeveling, false)
        {
            UnlockObjective = kind is null ? null : new UnlockObjective(2, 3, kind.Value),
        };

        var purpose = VoyageRoutePurposePresentation.Create(plan, point => $"Point {point}");

        Assert.Equal(expected, purpose.Label);
        Assert.NotEmpty(purpose.Tooltip);
    }

    [Fact]
    public void IncomeHeaderKeepsStableIdWhenLiveMetricsChange()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(10);
        var fc = CreateFc(50, now.AddHours(2), [1], currentKnown: true) with
        {
            FreeCompanyTag = "INCOME",
            World = "Cerberus",
        };
        var projection = FleetPresentationBuilder.Create(
            fc,
            CreateResult(fc, 90, now),
            EtaSettings.CreateDefault() with { TargetRank = 90 },
            new StubCatalog(),
            now);
        IncomeFcMetrics Metrics(double gilPerDay) => new(
            fc.FcIdKey, fc.DisplayName, 10_000, 4, gilPerDay, 2_500, 10,
            now.AddDays(-10), now, []);

        var before = IncomeFcHeaderPresentation.Create(projection, Metrics(1_000), favorite: true);
        var after = IncomeFcHeaderPresentation.Create(projection, Metrics(999), favorite: true);

        Assert.Equal(before.WidgetId, after.WidgetId);
        Assert.Equal("★ INCOME", before.FreeCompany);
        Assert.Equal("Cerberus", before.World);
        Assert.Equal("1 leveling", before.Mode);
        Assert.Equal(10_000.ToString("N0"), before.GrossGil);
        Assert.Equal("4", before.Voyages);
        Assert.NotEqual(before.GilPerDay, after.GilPerDay);
    }

    [Fact]
    public void IncomeHeaderListsCurrentBuildsAndRanksInTrackedOrder()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(10);
        var original = CreateFc(115, now.AddHours(2), [1], currentKnown: true);
        var ranks = new[] { 115, 115, 116, 114 };
        var fc = original with
        {
            FreeCompanyTag = "INCOME",
            Submarines = ranks.Select((rank, index) => original.Submarines[0] with
            {
                SubmarineId = index + 1,
                Name = $"Sub {index + 1}",
                Rank = rank,
            }).ToArray(),
        };
        var projection = FleetPresentationBuilder.Create(
            fc,
            CreateResult(fc, 120, now),
            EtaSettings.CreateDefault() with { TargetRank = 120 },
            new StubCatalog(),
            now);
        var builds = new[] { "WSCC", "WCSS", "WCUS", "S+C+U+S+" };
        var metric = new IncomeFcMetrics(
            fc.FcIdKey,
            fc.DisplayName,
            0,
            0,
            0,
            0,
            0,
            null,
            null,
            ranks.Select((rank, index) => new IncomeSubmarineMetrics(index + 1, $"Sub {index + 1}", 0, 0, 0, 0, null, null)
            {
                Rank = rank,
                CurrentBuild = CurrentBuildPresentation.Create(new SubmarineBuild(builds[index], rank, 0, 0, 0, 0, 0)),
            }).ToArray());

        var header = IncomeFcHeaderPresentation.Create(projection, metric, favorite: false);

        Assert.Equal("[WSCC:115 | WCSS:115 | WCUS:116 | SCUS++:114]", header.BuildsAndRanks);
        Assert.Equal("R115 · R115 · R116 · R114", OperationsFcHeaderPresentation.Create(projection, false, now).Ranks);
    }

    [Fact]
    public void IncomeHeaderRetainsRepeatedCurrentBuildCodes()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(10);
        var fc = CreateFc(115, now.AddHours(2), [1], currentKnown: true);
        var projection = FleetPresentationBuilder.Create(
            fc,
            CreateResult(fc, 120, now),
            EtaSettings.CreateDefault() with { TargetRank = 120 },
            new StubCatalog(),
            now);
        var metric = new IncomeFcMetrics(
            fc.FcIdKey,
            fc.DisplayName,
            0,
            0,
            0,
            0,
            0,
            null,
            null,
            [
                new IncomeSubmarineMetrics(1, "One", 0, 0, 0, 0, null, null)
                {
                    Rank = 115,
                    CurrentBuild = new CurrentBuildPresentation("WSCC", null),
                },
                new IncomeSubmarineMetrics(2, "Two", 0, 0, 0, 0, null, null)
                {
                    Rank = 116,
                    CurrentBuild = new CurrentBuildPresentation("WSCC", null),
                },
            ]);

        var header = IncomeFcHeaderPresentation.Create(projection, metric, favorite: false);

        Assert.Equal("[WSCC:115 | WSCC:116]", header.BuildsAndRanks);
    }

    [Fact]
    public void IncomeMetricsIncludeCurrentRankAndTrackedBuild()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(10);
        var fc = CreateFc(115, DateTimeOffset.MinValue, [], currentKnown: true);

        var metric = IncomeMetricsCalculator.Calculate(
            fc,
            now,
            period: null,
            catalog: new StubCatalog { BuildCode = "WSCC" });

        var submarine = Assert.Single(metric.Submarines);
        Assert.Equal(115, submarine.Rank);
        Assert.Equal("WSCC", submarine.CurrentBuild.Code);
        Assert.Null(submarine.CurrentBuild.UnavailableReason);
    }

    [Fact]
    public void IncomeMetricsMarkMissingTrackedBuildAsUnavailable()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(10);
        var original = CreateFc(115, DateTimeOffset.MinValue, [], currentKnown: true);
        var fc = original with
        {
            Submarines = [original.Submarines[0] with { BuildParts = SubmarineBuildParts.Empty }],
        };

        var metric = IncomeMetricsCalculator.Calculate(fc, now, period: null, catalog: new StubCatalog());
        var submarine = Assert.Single(metric.Submarines);

        Assert.Equal("—", submarine.CurrentBuild.Code);
        Assert.NotNull(submarine.CurrentBuild.UnavailableReason);
    }

    [Theory]
    [InlineData(IncomeView.AllFleets, null)]
    [InlineData(IncomeView.Leveling, FleetMode.Leveling)]
    [InlineData(IncomeView.Farming, FleetMode.Farming)]
    public void IncomeViewMapsToCurrentFleetMode(IncomeView view, FleetMode? expectedMode)
    {
        Assert.Equal(expectedMode, IncomeViewPreferences.RequiredMode(view));
        Assert.Equal(IncomeView.Farming, IncomeViewPreferences.Default);
    }

    [Fact]
    public void InvalidIncomeViewNormalizesToFarming()
    {
        Assert.Equal(IncomeView.Farming, IncomeViewPreferences.Normalize((IncomeView)999));
    }

    [Fact]
    public void IncomeViewUsesEffectivePerFcTarget()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(10);
        var fc = CreateFc(100, now.AddHours(2), [1], currentKnown: true);
        var catalog = new StubCatalog { SupportedMaximumRank = 150 };
        var globalReady = FleetPresentationBuilder.Create(
            fc,
            CreateResult(fc, 90, now),
            EtaSettings.CreateDefault() with { TargetRank = 90 },
            catalog,
            now);
        var overriddenLeveling = FleetPresentationBuilder.Create(
            fc,
            CreateResult(fc, 110, now),
            EtaSettings.CreateDefault() with { TargetRank = 110 },
            catalog,
            now);

        Assert.Equal(FleetMode.Farming, globalReady.Mode);
        Assert.True(FleetPresentationFiltering.Includes(globalReady, IncomeViewPreferences.RequiredMode(IncomeView.Farming)));
        Assert.Equal(FleetMode.Leveling, overriddenLeveling.Mode);
        Assert.True(FleetPresentationFiltering.Includes(overriddenLeveling, IncomeViewPreferences.RequiredMode(IncomeView.Leveling)));
    }

    [Fact]
    public void IncomeSummaryUsesOnlyProvidedFilteredFcs()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(100);
        IncomeFcMetrics Metrics(string id, long gil, int voyages, int firstReturnDaysAgo) => new(
            id,
            id,
            gil,
            voyages,
            gil / (double)firstReturnDaysAgo,
            gil / (double)voyages,
            firstReturnDaysAgo,
            now.AddDays(-firstReturnDaysAgo),
            now,
            []);
        var farming = Metrics("farming", 10_000, 4, 10);
        var leveling = Metrics("leveling", 90_000, 6, 20);

        var farmingSummary = IncomeMetricsCalculator.Summarize([farming], now, TimeSpan.FromDays(30));
        var allSummary = IncomeMetricsCalculator.Summarize([farming, leveling], now, TimeSpan.FromDays(30));

        Assert.Equal(10_000, farmingSummary.GrossGil);
        Assert.Equal(4, farmingSummary.VoyageCount);
        Assert.Equal(1, farmingSummary.FcCount);
        Assert.Equal(1_000, farmingSummary.GilPerDay);
        Assert.Equal(2_500, farmingSummary.GilPerVoyage);
        Assert.Equal(100_000, allSummary.GrossGil);
        Assert.Equal(2, allSummary.FcCount);
    }

    [Fact]
    public void IncomeSummaryHandlesEmptyFilter()
    {
        var summary = IncomeMetricsCalculator.Summarize([], DateTimeOffset.UnixEpoch, TimeSpan.FromDays(30));

        Assert.Equal(0, summary.GrossGil);
        Assert.Equal(0, summary.VoyageCount);
        Assert.Equal(0, summary.FcCount);
        Assert.Equal(0, summary.CoveredDays);
    }

    [Fact]
    public void IncomeOrderingKeepsFavoritesFirstWithinFilteredMetrics()
    {
        IncomeFcMetrics Metrics(string id, long gil) => new(
            id, id, gil, 1, gil, gil, 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, []);
        var favoriteLow = Metrics("Favorite", 1);
        var regularHigh = Metrics("Regular", 10_000);

        var ordered = IncomeMetricsOrdering.Order(
            [regularHigh, favoriteLow],
            IncomeSort.GrossGil,
            metric => metric.FcIdKey == "Favorite");

        Assert.Equal(["Favorite", "Regular"], ordered.Select(metric => metric.FcIdKey).ToArray());
    }

    [Fact]
    public void IncomeOneYearWindowIncludesExactBoundaryAndZeroGilVoyages()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(500);
        var fc = CreateFc(100, DateTimeOffset.MinValue, [], currentKnown: true);
        var submarine = fc.Submarines[0] with
        {
            Salvage = new SubmarineSalvageSummary(3, now.AddDays(-366), now.AddDays(-1), [])
            {
                Voyages =
                [
                    new(fc.FcIdKey, 1, now.AddDays(-366), [new SalvageItemTotal(1, "Outside", 100, 10)]),
                    new(fc.FcIdKey, 1, now.AddDays(-365), [new SalvageItemTotal(1, "Boundary", 100, 10)]),
                    new(fc.FcIdKey, 1, now.AddDays(-1), []),
                ],
            },
        };
        fc = fc with { Submarines = [submarine] };

        var metrics = IncomeMetricsCalculator.Calculate(fc, now, TimeSpan.FromDays(365));

        Assert.Equal(1_000, metrics.GrossGil);
        Assert.Equal(2, metrics.ValidVoyages);
        Assert.Equal(now.AddDays(-365), metrics.FirstReturnAtUtc);
    }

    private static FcState CreateFc(int rank, DateTimeOffset returnAt, IReadOnlyList<uint> route, bool currentKnown)
    {
        byte[] id = [1];
        return new FcState(
            id,
            "TEST",
            "World",
            new HashSet<uint> { 1 },
            new HashSet<uint> { 1 },
            [new SubmarineState(id, 1, "Sub", rank, 0, 100, new SubmarineBuildParts(1, 1, 1, 1), returnAt, route, currentKnown, [])]);
    }

    private static EtaResult CreateResult(FcState fc, int target, DateTimeOffset now)
    {
        var submarine = fc.Submarines[0];
        var plan = new VoyagePlan(
            submarine.SubmarineId, submarine.Name, now, now.AddHours(1), "TEST", [1], 1_000,
            submarine.Rank, Math.Min(100, submarine.Rank + 10), 0, 0, [], [], TimeSpan.FromHours(1), 1_000,
            EtaModel.PracticalLeveling, false);
        var perSub = new PerSubEtaResult(
            submarine.SubmarineId, submarine.Name, submarine.Rank, Math.Max(target, submarine.Rank), now.AddHours(1),
            TimeSpan.FromHours(1), 1, "TEST", [1], [plan], [], [], CalculationStatus.Complete, null)
        {
            NextRouteOutcomes = [new RouteOutcome([1], 1, [])],
        };
        return new EtaResult(fc.FcId, fc.DisplayName, now, target, SimulationMode.Fleet, [perSub], now.AddHours(1), 1, [plan], [], [], CalculationStatus.Complete, null);
    }

    private sealed class StubCatalog : ISubmarineCatalog
    {
        public int SupportedMaximumRank { get; init; } = 100;
        public string BuildCode { get; init; } = "TEST";
        public int MaximumRank => SupportedMaximumRank;
        public IReadOnlyList<UnlockRule> UnlockRules => [];
        public SubmarineBuild ResolveBuild(string buildCode, int rank) => new(buildCode, rank, 0, 0, 0, 999, 100);
        public SubmarineBuild? ResolveBuild(SubmarineBuildParts buildParts, int rank) => buildParts == SubmarineBuildParts.Empty ? null : ResolveBuild(BuildCode, rank);
        public RouteSearchResult FindBestRoute(RouteSearchRequest request) => new(null, 0, false);
        public uint CalculateExp(IReadOnlyList<uint> route, SubmarineBuild build, ExpMode expMode) => 1_000;
        public TimeSpan CalculateDuration(IReadOnlyList<uint> route, SubmarineBuild build) => TimeSpan.FromHours(1);
        public (int Rank, uint CurrentExp, uint NextLevelExp) ApplyExp(int rank, uint currentExp, uint gainedExp, int targetRank)
            => (Math.Min(targetRank, rank + 10), 0, 100);
        public string PointName(uint point) => point.ToString();
        public int GetPointRequiredRank(uint point) => 1;
    }
}
