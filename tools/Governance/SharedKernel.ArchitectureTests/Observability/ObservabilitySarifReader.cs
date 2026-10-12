using System.Text.Json;

namespace SharedKernel.ArchitectureTests.Observability;

/// <summary>
/// Reads the per-project SARIF files the kernel self-analysis writes (<c>sharedkernel-observability.sarif</c>) and turns the analyzer
/// diagnostics in them into findings, so the platform report can show analyzer counts beside the assertion counts.
/// </summary>
/// <remarks>
/// Only results whose rule id the caller lists are read (the SK ids and <c>CA2200</c>), and suppressed results are skipped. A finding's
/// rule is <c>analyzer.{ruleId}</c>, its site <c>{file}:{line}</c>. A file that is absent is simply not read: with the self-analysis
/// import not yet in the root build there are none, and the report then carries assertion findings only.
/// </remarks>
public static class ObservabilitySarifReader
{
    /// <summary>The file name the self-analysis writes next to each assembly.</summary>
    public const string FileName = "sharedkernel-observability.sarif";

    /// <summary>Reads the findings of the given SARIF files.</summary>
    /// <param name="files">Each project (assembly) name with the path of its SARIF file; a path that does not exist is skipped.</param>
    /// <param name="domainOfProject">Maps a project name to its domain id.</param>
    /// <param name="ruleIds">The rule ids to keep (for example every SK id and <c>CA2200</c>).</param>
    /// <returns>The findings, one per unsuppressed result.</returns>
    public static IReadOnlyList<ObservabilityFinding> Read(
        IEnumerable<(string Project, string Path)> files,
        Func<string, string> domainOfProject,
        IReadOnlySet<string> ruleIds)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(domainOfProject);
        ArgumentNullException.ThrowIfNull(ruleIds);

        var findings = new List<ObservabilityFinding>();
        foreach (var (project, path) in files)
        {
            if (!File.Exists(path))
                continue;

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("runs", out var runs))
                continue;

            foreach (var run in runs.EnumerateArray())
            {
                if (!run.TryGetProperty("results", out var results))
                    continue;

                foreach (var result in results.EnumerateArray())
                {
                    if (!result.TryGetProperty("ruleId", out var ruleElement) || ruleElement.GetString() is not { } ruleId || !ruleIds.Contains(ruleId))
                        continue;

                    if (result.TryGetProperty("suppressions", out var suppressions) && suppressions.GetArrayLength() > 0)
                        continue;

                    findings.Add(new ObservabilityFinding($"analyzer.{ruleId}", domainOfProject(project), project, SiteOf(result), MessageOf(result)));
                }
            }
        }

        return findings;
    }

    private static string SiteOf(JsonElement result)
    {
        if (result.TryGetProperty("locations", out var locations) && locations.GetArrayLength() > 0)
        {
            var location = locations[0];
            if (location.TryGetProperty("physicalLocation", out var physical))
            {
                var uri = physical.TryGetProperty("artifactLocation", out var artifact) && artifact.TryGetProperty("uri", out var uriElement)
                    ? uriElement.GetString()
                    : null;
                var line = physical.TryGetProperty("region", out var region) && region.TryGetProperty("startLine", out var lineElement)
                    ? lineElement.GetInt32()
                    : 0;
                return $"{System.IO.Path.GetFileName(uri ?? "?")}:{line}";
            }
        }

        return "-";
    }

    private static string MessageOf(JsonElement result) =>
        result.TryGetProperty("message", out var message) && message.TryGetProperty("text", out var text) ? text.GetString() ?? string.Empty : string.Empty;
}
