namespace SubmarineEtaPlanner.Planner;

public static class RecordedVoyageMetricsCalculator
{
    public static RecordedVoyageMetrics Calculate(
        VoyageObservation observation,
        IRouteOperationalCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(catalog);

        var signature = SectorSetSignature.Create(observation.SectorIds);
        var fuel = catalog.CalculateFuel(observation.SectorIds);
        return new RecordedVoyageMetrics(
            observation,
            signature,
            fuel.IsComplete ? fuel.CeruleumTanks : null,
            fuel.IsComplete);
    }
}

public static class IncomeMetricsCalculator
{
    public static IncomeFcMetrics Calculate(FcState fc, DateTimeOffset now, TimeSpan? period)
        => CalculateCore(fc, now, period, catalog: null, operationalCatalog: null);

    public static IncomeFcMetrics Calculate(
        FcState fc,
        DateTimeOffset now,
        TimeSpan? period,
        ISubmarineCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return CalculateCore(fc, now, period, catalog, catalog as IRouteOperationalCatalog);
    }

    public static IncomeFcMetrics Calculate(
        FcState fc,
        DateTimeOffset now,
        TimeSpan? period,
        ISubmarineCatalog catalog,
        IRouteOperationalCatalog operationalCatalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(operationalCatalog);
        return CalculateCore(fc, now, period, catalog, operationalCatalog);
    }

    private static IncomeFcMetrics CalculateCore(
        FcState fc,
        DateTimeOffset now,
        TimeSpan? period,
        ISubmarineCatalog? catalog,
        IRouteOperationalCatalog? operationalCatalog)
    {
        var windowStart = period is null ? (DateTimeOffset?)null : now - period.Value;
        var submarines = fc.Submarines.Select(submarine =>
        {
            var currentBuild = catalog is null
                ? CurrentBuildPresentation.NotResolved
                : CurrentBuildPresentation.Create(catalog.ResolveBuild(submarine.BuildParts, submarine.Rank));
            var voyages = submarine.Salvage.Voyages
                .Where(voyage => voyage.Items.Count > 0 &&
                                 voyage.ReturnAtUtc <= now &&
                                 (windowStart is null || voyage.ReturnAtUtc >= windowStart))
                .OrderBy(voyage => voyage.ReturnAtUtc)
                .ToArray();
            var recordedVoyages = submarine.VoyageHistory
                .Where(observation => observation.ReturnAtUtc <= now &&
                                      (windowStart is null || observation.ReturnAtUtc >= windowStart))
                .OrderBy(observation => observation.ReturnAtUtc)
                .Select(observation => operationalCatalog is null
                    ? new RecordedVoyageMetrics(
                        observation,
                        SectorSetSignature.Create(observation.SectorIds),
                        CeruleumTanks: null,
                        FuelKnown: false)
                    : RecordedVoyageMetricsCalculator.Calculate(observation, operationalCatalog))
                .ToArray();
            var recordedResources = AggregateRecordedResources(recordedVoyages);
            var first = voyages.FirstOrDefault()?.ReturnAtUtc;
            var last = voyages.LastOrDefault()?.ReturnAtUtc;
            var coveredStart = first is null ? (DateTimeOffset?)null : windowStart is null ? first : Max(first.Value, windowStart.Value);
            var coveredDays = coveredStart is null ? 0d : Math.Max((now - coveredStart.Value).TotalDays, 1d / 24d);
            var gil = voyages.Sum(voyage => voyage.GrossNpcGil);
            var recordedAverageGilPerDay = coveredDays <= 0 ? 0 : gil / coveredDays;
            return new IncomeSubmarineMetrics(
                submarine.SubmarineId,
                submarine.Name,
                gil,
                voyages.Length,
                voyages.Length == 0 ? 0 : gil / (double)voyages.Length,
                coveredDays,
                recordedAverageGilPerDay,
                recordedAverageGilPerDay,
                first,
                last)
            {
                Rank = submarine.Rank,
                CurrentBuild = currentBuild,
                RecordedVoyages = recordedVoyages,
                KnownFuelVoyageCount = recordedResources.KnownFuelVoyageCount,
                UnknownFuelVoyageCount = recordedResources.UnknownFuelVoyageCount,
                TotalRecordedTanks = recordedResources.TotalRecordedTanks,
                AverageTanksPerVoyage = recordedResources.AverageTanksPerVoyage,
                GrossGilPerTank = recordedResources.GrossGilPerTank,
                GrossGilByRouteSignature = recordedResources.GrossGilByRouteSignature,
            };
        }).ToArray();
        var fcFirst = submarines.Where(item => item.FirstReturnAtUtc is not null).Select(item => item.FirstReturnAtUtc).Min();
        var fcLast = submarines.Where(item => item.LastReturnAtUtc is not null).Select(item => item.LastReturnAtUtc).Max();
        var fcCoveredStart = fcFirst is null ? (DateTimeOffset?)null : windowStart is null ? fcFirst : Max(fcFirst.Value, windowStart.Value);
        var fcCoveredDays = fcCoveredStart is null ? 0d : Math.Max((now - fcCoveredStart.Value).TotalDays, 1d / 24d);
        var gross = submarines.Sum(item => item.GrossGil);
        var voyageCount = submarines.Sum(item => item.VoyageCount);
        var recordedResources = AggregateRecordedResources(
            submarines.SelectMany(submarine => submarine.RecordedVoyages));
        return new IncomeFcMetrics(
            fc.FcIdKey,
            fc.DisplayName,
            gross,
            voyageCount,
            voyageCount == 0 ? 0 : gross / (double)voyageCount,
            fcCoveredDays,
            fcCoveredDays <= 0 ? 0 : gross / fcCoveredDays,
            submarines.Sum(item => item.ObservedRunRateGilPerDay),
            fcFirst,
            fcLast,
            submarines)
        {
            KnownFuelVoyageCount = recordedResources.KnownFuelVoyageCount,
            UnknownFuelVoyageCount = recordedResources.UnknownFuelVoyageCount,
            TotalRecordedTanks = recordedResources.TotalRecordedTanks,
            AverageTanksPerVoyage = recordedResources.AverageTanksPerVoyage,
            GrossGilPerTank = recordedResources.GrossGilPerTank,
            GrossGilByRouteSignature = recordedResources.GrossGilByRouteSignature,
        };
    }

    private static RecordedResourceAggregate AggregateRecordedResources(
        IEnumerable<RecordedVoyageMetrics> voyages)
    {
        var recordedVoyages = voyages.ToArray();
        var knownFuelVoyages = recordedVoyages
            .Where(voyage => voyage.FuelKnown && voyage.CeruleumTanks is not null)
            .ToArray();
        var totalRecordedTanks = knownFuelVoyages.Aggregate(
            0,
            (total, voyage) => checked(total + voyage.CeruleumTanks!.Value));
        var knownFuelGrossGil = knownFuelVoyages.Sum(voyage => voyage.Observation.GrossNpcGil);
        var grossGilByRouteSignature = new System.Collections.ObjectModel.ReadOnlyDictionary<SectorSetSignature, long>(
            recordedVoyages
                .GroupBy(voyage => voyage.SectorSignature)
                .OrderBy(group => group.Key.Value, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Sum(voyage => voyage.Observation.GrossNpcGil)));

        return new RecordedResourceAggregate(
            knownFuelVoyages.Length,
            recordedVoyages.Length - knownFuelVoyages.Length,
            totalRecordedTanks,
            knownFuelVoyages.Length == 0
                ? null
                : totalRecordedTanks / (double)knownFuelVoyages.Length,
            totalRecordedTanks == 0
                ? null
                : knownFuelGrossGil / (double)totalRecordedTanks,
            grossGilByRouteSignature);
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left > right ? left : right;

    public static IncomeSummaryMetrics Summarize(
        IReadOnlyList<IncomeFcMetrics> metrics,
        DateTimeOffset now,
        TimeSpan? period)
    {
        var gross = metrics.Sum(item => item.GrossGil);
        var voyages = metrics.Sum(item => item.VoyageCount);
        var first = metrics
            .Where(item => item.FirstReturnAtUtc is not null)
            .Select(item => item.FirstReturnAtUtc)
            .Min();
        var start = first is null
            ? (DateTimeOffset?)null
            : period is null
                ? first
                : first > now - period ? first : now - period;
        var days = start is null ? 0 : Math.Max((now - start.Value).TotalDays, 1d / 24d);
        return new IncomeSummaryMetrics(
            gross,
            voyages,
            voyages == 0 ? 0 : gross / (double)voyages,
            days,
            days == 0 ? 0 : gross / days,
            metrics.Sum(item => item.ObservedRunRateGilPerDay),
            metrics.Count);
    }

    private sealed record RecordedResourceAggregate(
        int KnownFuelVoyageCount,
        int UnknownFuelVoyageCount,
        int TotalRecordedTanks,
        double? AverageTanksPerVoyage,
        double? GrossGilPerTank,
        IReadOnlyDictionary<SectorSetSignature, long> GrossGilByRouteSignature);
}
