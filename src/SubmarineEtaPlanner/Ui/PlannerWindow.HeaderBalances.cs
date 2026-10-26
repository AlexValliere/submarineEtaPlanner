using SubmarineEtaPlanner.Planner;

namespace SubmarineEtaPlanner.Ui;

public sealed partial class PlannerWindow
{
    private GilBalanceSnapshot? headerGilSource;
    private EtaPlannerSnapshot? headerGilFleetSource;
    private string[] headerGilVisibleFcIds = [];
    private GilBalanceOverview? headerGilOverview;

    private GilBalanceOverview GetHeaderBalanceOverview()
    {
        // Settings and FC Setup can be the first page opened; obtain their FC scope
        // through the same progressive refresh rather than displaying a loading state forever.
        if (this.snapshot is null && this.refreshTask is null && string.IsNullOrWhiteSpace(this.lastError)
            && this.getSubmarineTrackerState().IsAvailable)
            StartRefresh();
        var observations = this.getGilBalances();
        var scopeKnown = this.snapshot is { } current
            && (current.Status != CalculationStatus.Failed || current.FreeCompanies.Count > 0);
        FcState[] fleets = scopeKnown ? VisibleFreeCompanies(this.snapshot!) : [];
        if (this.headerGilOverview is null ||
            !ReferenceEquals(this.headerGilSource, observations) ||
            !ReferenceEquals(this.headerGilFleetSource, this.snapshot) ||
            !this.headerGilVisibleFcIds.SequenceEqual(fleets.Select(fc => fc.FcIdKey), StringComparer.Ordinal))
        {
            this.headerGilSource = observations;
            this.headerGilFleetSource = this.snapshot;
            this.headerGilVisibleFcIds = fleets.Select(fc => fc.FcIdKey).ToArray();
            this.headerGilOverview = GilBalanceOverview.Create(
                fleets.Select(fc => fc.GameFreeCompanyId), observations, scopeKnown);
        }
        return this.headerGilOverview;
    }

    private void OpenGlobalBalances()
    {
        this.incomeFcScope = null;
        this.expandIncomeFc = null;
        this.currentPage = PlannerPage.Income;
        if (this.configuration.IncomeDisplayMode == IncomeDisplayMode.Balances) return;
        this.configuration.IncomeDisplayMode = IncomeDisplayMode.Balances;
        this.saveConfiguration();
    }
}
