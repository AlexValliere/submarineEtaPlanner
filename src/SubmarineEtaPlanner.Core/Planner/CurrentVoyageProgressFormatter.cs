namespace SubmarineEtaPlanner.Planner;

internal enum CurrentVoyageProgressState
{
    Idle,
    Underway,
    ReadyToCollect,
    Syncing,
}

internal sealed record CurrentVoyageProgressPresentation(
    long SubmarineId,
    string SubmarineName,
    CurrentVoyageProgressState State,
    DateTimeOffset? ReturnAtUtc,
    DateTimeOffset? DepartedAtUtc,
    TimeSpan? Duration,
    float? Fraction,
    string Countdown,
    string? ProgressUnavailableReason)
{
    public bool IsActive => State != CurrentVoyageProgressState.Idle;
}

internal sealed record FcCurrentVoyageProgressPresentation(
    CurrentVoyageProgressPresentation? Primary,
    IReadOnlyList<CurrentVoyageProgressPresentation> Voyages,
    int ReadyCount)
{
    public bool HasActiveVoyages => Primary is not null;

    public string HeaderLabel => ReadyCount switch
    {
        > 1 => $"{ReadyCount} ready to collect",
        1 => "1 ready to collect",
        _ when Primary is not null => $"Next return in {Primary.Countdown}",
        _ => string.Empty,
    };
}

internal static class CurrentVoyageProgressFormatter
{
    public static CurrentVoyageProgressPresentation Create(
        SubmarineState submarine,
        ISubmarineCatalog catalog,
        DateTimeOffset now)
    {
        if (!submarine.CurrentVoyageKnown)
        {
            var readyWithoutRoute = submarine.ReturnAtUtc <= now;
            return new CurrentVoyageProgressPresentation(
                submarine.SubmarineId,
                submarine.Name,
                readyWithoutRoute ? CurrentVoyageProgressState.ReadyToCollect : CurrentVoyageProgressState.Syncing,
                submarine.ReturnAtUtc,
                null,
                null,
                readyWithoutRoute ? 1f : null,
                FormatCountdown(submarine.ReturnAtUtc - now),
                readyWithoutRoute
                    ? "The voyage has reached its return time, but the current route never synced."
                    : "The return time is known, but the current route has not synced yet.");
        }

        if (submarine.CurrentRoute.Count == 0)
        {
            return new CurrentVoyageProgressPresentation(
                submarine.SubmarineId,
                submarine.Name,
                CurrentVoyageProgressState.Idle,
                null,
                null,
                null,
                null,
                "—",
                null);
        }

        var readyToCollect = submarine.ReturnAtUtc <= now;
        var build = catalog.ResolveBuild(submarine.BuildParts, submarine.Rank);
        if (readyToCollect)
        {
            var readyDuration = build is null
                ? TimeSpan.Zero
                : catalog.CalculateDuration(submarine.CurrentRoute, build);
            return new CurrentVoyageProgressPresentation(
                submarine.SubmarineId,
                submarine.Name,
                CurrentVoyageProgressState.ReadyToCollect,
                submarine.ReturnAtUtc,
                readyDuration > TimeSpan.Zero ? submarine.ReturnAtUtc - readyDuration : null,
                readyDuration > TimeSpan.Zero ? readyDuration : null,
                1f,
                "Ready to collect",
                build is null
                    ? "The recorded build is incomplete; departure and total duration are unavailable."
                    : readyDuration <= TimeSpan.Zero
                        ? "The current route duration could not be calculated; departure and total duration are unavailable."
                        : null);
        }

        if (build is null)
        {
            return CreateUnderwayWithoutProgress(
                submarine,
                now,
                "The recorded build is incomplete, so voyage percentage is unavailable.");
        }

        var duration = catalog.CalculateDuration(submarine.CurrentRoute, build);
        if (duration <= TimeSpan.Zero)
        {
            return CreateUnderwayWithoutProgress(
                submarine,
                now,
                "The current route duration could not be calculated, so voyage percentage is unavailable.");
        }

        var departedAtUtc = submarine.ReturnAtUtc - duration;
        var elapsedTicks = (now - departedAtUtc).Ticks;
        var fraction = Math.Clamp(elapsedTicks / (double)duration.Ticks, 0d, 1d);
        return new CurrentVoyageProgressPresentation(
            submarine.SubmarineId,
            submarine.Name,
            CurrentVoyageProgressState.Underway,
            submarine.ReturnAtUtc,
            departedAtUtc,
            duration,
            (float)fraction,
            FormatCountdown(submarine.ReturnAtUtc - now),
            now < departedAtUtc
                ? "The inferred departure is in the future; progress is clamped to 0%. Check the system clock or tracker data."
                : null);
    }

    public static FcCurrentVoyageProgressPresentation CreateForFc(
        IEnumerable<SubmarineState> submarines,
        ISubmarineCatalog catalog,
        DateTimeOffset now)
    {
        var active = submarines
            .Select(submarine => Create(submarine, catalog, now))
            .Where(progress => progress.IsActive)
            .OrderBy(progress => progress.ReturnAtUtc)
            .ThenBy(progress => progress.SubmarineName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(progress => progress.SubmarineId)
            .ToArray();
        var ready = active
            .Where(progress => progress.State == CurrentVoyageProgressState.ReadyToCollect)
            .ToArray();
        var primary = ready.FirstOrDefault() ?? active.FirstOrDefault();
        return new FcCurrentVoyageProgressPresentation(primary, active, ready.Length);
    }

    public static string FormatCountdown(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
            return "Ready to collect";

        var totalSeconds = checked((long)Math.Ceiling(remaining.TotalSeconds));
        var rounded = TimeSpan.FromSeconds(totalSeconds);
        if (rounded.Days > 0)
            return $"{rounded.Days}d {rounded.Hours}h {rounded.Minutes}m";
        if (rounded.Hours > 0)
            return $"{rounded.Hours}h {rounded.Minutes}m";
        if (rounded.Minutes > 0)
            return $"{rounded.Minutes}m {rounded.Seconds}s";
        return $"{rounded.Seconds}s";
    }

    private static CurrentVoyageProgressPresentation CreateUnderwayWithoutProgress(
        SubmarineState submarine,
        DateTimeOffset now,
        string reason)
        => new(
            submarine.SubmarineId,
            submarine.Name,
            CurrentVoyageProgressState.Underway,
            submarine.ReturnAtUtc,
            null,
            null,
            null,
            FormatCountdown(submarine.ReturnAtUtc - now),
            reason);
}
