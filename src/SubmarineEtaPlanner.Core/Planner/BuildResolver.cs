namespace SubmarineEtaPlanner.Planner;

public sealed class BuildResolver(ISubmarineCatalog catalog)
{
    public string GetBuildCodeForRank(int rank, EtaSettings settings)
    {
        var profile = settings.EtaModel == EtaModel.PracticalLeveling
            ? EtaSettings.CreateDefault().BuildProfile
            : settings.BuildProfile;

        var step = profile
            .OrderBy(s => s.MinRank)
            .FirstOrDefault(s => s.Contains(rank));

        return step?.BuildCode ?? EtaSettings.CreateDefault().BuildProfile.First(s => s.Contains(Math.Clamp(rank, 1, 999))).BuildCode;
    }

    public SubmarineBuild ResolveBuildForRank(int rank, EtaSettings settings)
    {
        var code = GetBuildCodeForRank(rank, settings);
        return catalog.ResolveBuild(code, rank);
    }

    public IReadOnlyList<string> Validate(EtaSettings settings)
    {
        var warnings = new List<string>();
        if (settings.BuildProfile.Count == 0)
        {
            warnings.Add("Build profile is empty; defaults will be used.");
            return warnings;
        }

        foreach (var step in settings.BuildProfile)
        {
            if (step.MinRank <= 0)
                warnings.Add($"Build step {step.BuildCode} starts below rank 1.");
            if (step.MaxRank < step.MinRank)
                warnings.Add($"Build step {step.BuildCode} has an invalid rank range.");
            if (string.IsNullOrWhiteSpace(step.BuildCode))
                warnings.Add($"Build step {step.MinRank}-{step.MaxRank} has no build code.");
        }

        var ordered = settings.BuildProfile.OrderBy(s => s.MinRank).ToArray();
        for (var i = 1; i < ordered.Length; i++)
        {
            if (ordered[i].MinRank <= ordered[i - 1].MaxRank)
                warnings.Add($"Build steps {ordered[i - 1].BuildCode} and {ordered[i].BuildCode} overlap.");
        }

        return warnings;
    }
}
