namespace SharedKernel.Security.Oidc.Diagnostics;

// The alg header of a rejected token is attacker-chosen, so it is logged only when it is short and identifier-shaped.
internal static class AlgorithmText
{
    internal const string Unrecognized = "unrecognized";

    private const int MaxLength = 16;

    public static string Bound(string algorithm)
    {
        if (algorithm.Length > MaxLength)
        {
            return Unrecognized;
        }

        foreach (char c in algorithm)
        {
            bool allowed = c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-';
            if (!allowed)
            {
                return Unrecognized;
            }
        }

        return algorithm;
    }
}
