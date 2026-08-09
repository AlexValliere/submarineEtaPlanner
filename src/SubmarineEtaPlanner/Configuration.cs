using Dalamud.Configuration;
using SubmarineEtaPlanner.Planner;

namespace SubmarineEtaPlanner;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = EtaSettingsMigration.CurrentVersion;

    public EtaSettings Settings { get; set; } = null!;

    public bool WindowOpen { get; set; } = true;

    public FcResultFilter ResultsFilter { get; set; } = FcResultFilter.Leveling;

    public Dictionary<string, FcPreferences> FreeCompanyPreferences { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public OperationsView OperationsView { get; set; } = OperationsView.AllFleets;

    public OperationsSort OperationsSort { get; set; } = OperationsSort.NextReturnActionsFirst;

    public LevelingSort LevelingSort { get; set; } = LevelingSort.FarmReadyEta;

    public LevelingFilter LevelingFilter { get; set; } = LevelingFilter.All;

    public IncomePeriod IncomePeriod { get; set; } = IncomePeriod.Days30;

    public IncomeSort IncomeSort { get; set; } = IncomeSort.GrossGil;

    public IncomeView IncomeView { get; set; } = IncomeViewPreferences.Default;

    public bool Migrate()
    {
        var changed = false;
        if (Settings is null)
        {
            Settings = EtaSettings.CreateDefault();
            changed = true;
        }
        if (FreeCompanyPreferences is null)
        {
            FreeCompanyPreferences = new Dictionary<string, FcPreferences>(StringComparer.OrdinalIgnoreCase);
            changed = true;
        }
        else if (!FreeCompanyPreferences.Comparer.Equals(StringComparer.OrdinalIgnoreCase))
        {
            FreeCompanyPreferences = new Dictionary<string, FcPreferences>(FreeCompanyPreferences, StringComparer.OrdinalIgnoreCase);
            changed = true;
        }
        var version = Version;
        if (version < 11 && OperationsView == OperationsView.ReturningSoon)
        {
            OperationsView = OperationsView.AllFleets;
            changed = true;
        }
        changed |= EtaSettingsMigration.Migrate(Settings, ref version);
        Version = version;
        changed |= NormalizeViewPreferences();
        return changed;
    }

    public FcPreferences GetFcPreferences(string fcIdKey)
    {
        if (!FreeCompanyPreferences.TryGetValue(fcIdKey, out var preferences))
        {
            preferences = new FcPreferences();
            FreeCompanyPreferences[fcIdKey] = preferences;
        }

        return preferences;
    }

    public IReadOnlyDictionary<string, FcSimulationOverride> GetSimulationOverrides()
        => FreeCompanyPreferences
            .Where(pair => pair.Value.TargetRankOverride is not null || pair.Value.StrategyOverride is not null)
            .ToDictionary(
                pair => pair.Key,
                pair => new FcSimulationOverride(pair.Value.TargetRankOverride, pair.Value.StrategyOverride),
                StringComparer.OrdinalIgnoreCase);

    private bool NormalizeViewPreferences()
    {
        var changed = false;
        if (!Enum.IsDefined(OperationsView) || OperationsView == global::SubmarineEtaPlanner.OperationsView.ReturningSoon) { OperationsView = global::SubmarineEtaPlanner.OperationsView.AllFleets; changed = true; }
        if (!Enum.IsDefined(OperationsSort)) { OperationsSort = global::SubmarineEtaPlanner.OperationsSort.NextReturnActionsFirst; changed = true; }
        if (!Enum.IsDefined(LevelingSort)) { LevelingSort = global::SubmarineEtaPlanner.LevelingSort.FarmReadyEta; changed = true; }
        if (!Enum.IsDefined(LevelingFilter)) { LevelingFilter = global::SubmarineEtaPlanner.LevelingFilter.All; changed = true; }
        if (!Enum.IsDefined(IncomePeriod)) { IncomePeriod = global::SubmarineEtaPlanner.IncomePeriod.Days30; changed = true; }
        if (!Enum.IsDefined(IncomeSort)) { IncomeSort = global::SubmarineEtaPlanner.Planner.IncomeSort.GrossGil; changed = true; }
        var normalizedIncomeView = IncomeViewPreferences.Normalize(IncomeView);
        if (IncomeView != normalizedIncomeView) { IncomeView = normalizedIncomeView; changed = true; }
        if (!Enum.IsDefined(ResultsFilter)) { ResultsFilter = FcResultFilter.Leveling; changed = true; }
        foreach (var key in FreeCompanyPreferences.Keys.ToArray())
        {
            var preferences = FreeCompanyPreferences[key];
            if (preferences is null)
            {
                FreeCompanyPreferences[key] = new FcPreferences();
                changed = true;
                continue;
            }
            if (preferences.StrategyOverride is { } strategy && !Enum.IsDefined(strategy))
            {
                preferences.StrategyOverride = null;
                changed = true;
            }
            if (preferences.TargetRankOverride is <= 0)
            {
                preferences.TargetRankOverride = 1;
                changed = true;
            }
            changed |= FcPreferencesMigration.Normalize(preferences);
        }

        return changed;
    }
}

public enum OperationsView { ReturningSoon = 0, AllFleets = 1, Leveling = 2, Farming = 3 }
public enum OperationsSort { NextReturnActionsFirst, FarmReadyEta, FcName }
public enum LevelingSort { FarmReadyEta, LowestRank, NextAction, FcName }
public enum LevelingFilter { All, Actionable, Favorites }
public enum IncomePeriod { Days7 = 0, Days30 = 1, Days90 = 2, Lifetime = 3, Days365 = 4 }
