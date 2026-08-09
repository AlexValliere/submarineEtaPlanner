namespace SubmarineEtaPlanner.Planner;

internal static class CurrentBuildCodeFormatter
{
    private const int PartCount = 4;

    public static string Format(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        if (code.Length != PartCount * 2)
            return code;

        Span<char> parts = stackalloc char[PartCount];
        for (var partIndex = 0; partIndex < PartCount; partIndex++)
        {
            var codeIndex = partIndex * 2;
            var identifier = code[codeIndex];
            if (!IsPartIdentifier(identifier) || code[codeIndex + 1] != '+')
                return code;

            parts[partIndex] = identifier;
        }

        return string.Concat(new string(parts), "++");
    }

    private static bool IsPartIdentifier(char identifier)
        => identifier is 'S' or 'U' or 'W' or 'C' or 'Y';
}
