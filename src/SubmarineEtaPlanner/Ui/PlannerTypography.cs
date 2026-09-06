using Dalamud.Interface.ManagedFontAtlas;

namespace SubmarineEtaPlanner.Ui;

/// <summary>Two managed roles based on the user's font. Body text remains untouched.</summary>
internal sealed class PlannerTypography : IDisposable
{
    private readonly IFontHandle heading;
    private readonly IFontHandle value;

    internal PlannerTypography(IFontAtlas atlas)
    {
        // Negative sizes are factors of Dalamud's current default font size. The atlas
        // rebuilds these with the user font and handles global scaling and extra glyphs.
        this.heading = atlas.NewDelegateFontHandle(step => step.OnPreBuild(build => build.AddDalamudDefaultFont(-1.25f)));
        this.value = atlas.NewDelegateFontHandle(step => step.OnPreBuild(build => build.AddDalamudDefaultFont(-1.5f)));
    }

    // Push keeps the current font while a handle is building or unavailable.
    internal IDisposable Heading() => this.heading.Push();
    internal IDisposable Value() => this.value.Push();

    public void Dispose()
    {
        this.value.Dispose();
        this.heading.Dispose();
    }
}
