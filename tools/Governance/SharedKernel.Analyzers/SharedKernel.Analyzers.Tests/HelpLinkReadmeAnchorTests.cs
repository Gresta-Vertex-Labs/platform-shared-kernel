using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.Diagnostics;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>
/// Proves every diagnostic descriptor's <see cref="DiagnosticDescriptor.HelpLinkUri"/> anchor has
/// a matching explicit <c>&lt;a id="..."&gt;</c> anchor in <c>tools/Governance/README.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this exists.</strong> Every descriptor's <c>HelpLinkUri</c> is built by
/// <see cref="AnalyzerBase.CreateDescriptor"/> as
/// <c>{HelpLinkBase}#{readmeAnchor}</c>. Before this test was added, 25 of the 41 shipped
/// analyzers had no corresponding README section at all, and GitHub's own
/// heading-derived slug algorithm produced a double-hyphen anchor for every "Text — Text" heading
/// in this file, while the hand-written TOC links used a single hyphen — so even the 16
/// documented rules' help links were very likely broken. Explicit <c>&lt;a id="..."&gt;</c>
/// anchors immediately above each rule heading make the anchor byte-for-byte deterministic and
/// independent of GitHub's slug algorithm; this test is the mechanical guarantee that a future
/// 42nd analyzer shipped without an explicit anchor fails the build instead of shipping a dead
/// "learn more" link.
/// </para>
/// <para>
/// <strong>No hardcoded rule list.</strong> The set of descriptors under test is discovered by
/// reflecting over every concrete, non-abstract <see cref="DiagnosticAnalyzer"/> type in the
/// <c>SharedKernel.Analyzers</c> assembly (the same assembly <see cref="AnalyzerBase"/> lives in)
/// and instantiating each via its public parameterless constructor — every shipped analyzer has
/// one. This intentionally means a future analyzer added to this project is picked up
/// automatically, with no companion edit required to this test file.
/// </para>
/// <para>
/// <strong>Fail, never silently skip.</strong> <see cref="FindGovernanceReadmePath"/> walks up
/// from <see cref="AppContext.BaseDirectory"/> looking for <c>tools/Governance/README.md</c> and
/// throws <see cref="FileNotFoundException"/> if it cannot be resolved within a bounded number of
/// levels — mirroring the walk-up-and-throw pattern already established by
/// <c>src/Foundation/SharedKernel.Consumer.Tests</c>'s <c>FindNupkgsDirectory</c> (P-26/WO-068 and
/// successors). A vacuous "if the file isn't there, just pass" guard here would repeat the exact
/// class of bug this phase's own brief called out from a prior session's
/// <c>GuardPurityRules</c> mistake — a test that can never meaningfully fail is worse than no
/// test.
/// </para>
/// </remarks>
public sealed class HelpLinkReadmeAnchorTests
{
    private static readonly Regex ExplicitAnchorPattern = new(
        """<a\s+id="([a-zA-Z0-9\-_]+)"\s*></a>""",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Every real, shipped analyzer's <c>HelpLinkUri</c> anchor fragment must have a
    /// matching explicit <c>&lt;a id="..."&gt;</c> anchor in <c>tools/Governance/README.md</c>.
    /// </summary>
    [Fact]
    public void EveryAnalyzerHelpLinkAnchor_HasMatchingReadmeAnchor()
    {
        List<(string DiagnosticId, string Anchor)> descriptors = CollectRealAnalyzerHelpLinkAnchors();

        // Non-vacuity guard: if reflection ever discovers zero analyzers (e.g. a broken assembly
        // load), this test must fail loudly rather than pass on an empty set. 41 analyzers ship
        // as of this phase; the floor is set comfortably below that so the test tolerates future
        // additions/retractions without needing to track the exact count.
        Assert.True(
            descriptors.Count >= 35,
            $"Expected to discover at least 35 analyzer diagnostic descriptors via reflection, " +
                $"but found {descriptors.Count}. This usually means the discovery logic itself is " +
                "broken, not that analyzers were removed."
        );

        string readmePath = FindGovernanceReadmePath();
        string readmeText = File.ReadAllText(readmePath);

        HashSet<string> readmeAnchors = ExplicitAnchorPattern
            .Matches(readmeText)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(
            readmeAnchors.Count > 0,
            $"Found zero explicit <a id=\"...\"> anchors in '{readmePath}' — the regex or the " +
                "file contents are unexpectedly empty."
        );

        List<string> missing = descriptors
            .Where(d => !readmeAnchors.Contains(d.Anchor))
            .Select(d => $"{d.DiagnosticId} -> #{d.Anchor}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "The following analyzer HelpLinkUri anchors have no matching "
                + $"<a id=\"...\"> in '{readmePath}':\n"
                + string.Join('\n', missing)
        );
    }

    /// <summary>
    /// Reflects over every concrete <see cref="DiagnosticAnalyzer"/> in the assembly containing
    /// <see cref="AnalyzerBase"/>, instantiates each, and returns every
    /// (<see cref="Microsoft.CodeAnalysis.DiagnosticDescriptor.Id"/>, anchor-fragment) pair from
    /// its <see cref="DiagnosticAnalyzer.SupportedDiagnostics"/>. The anchor fragment is the
    /// substring of <see cref="Microsoft.CodeAnalysis.DiagnosticDescriptor.HelpLinkUri"/> after
    /// the '#'.
    /// </summary>
    private static List<(string DiagnosticId, string Anchor)> CollectRealAnalyzerHelpLinkAnchors()
    {
        Assembly analyzersAssembly = typeof(AnalyzerBase).Assembly;

        var results = new List<(string, string)>();

        foreach (
            Type analyzerType in analyzersAssembly
                .GetTypes()
                .Where(t =>
                    !t.IsAbstract
                    && !t.IsInterface
                    && typeof(DiagnosticAnalyzer).IsAssignableFrom(t)
                    && t.GetConstructor(Type.EmptyTypes) is not null
                )
        )
        {
            var analyzer = (DiagnosticAnalyzer)Activator.CreateInstance(analyzerType)!;

            foreach (var descriptor in analyzer.SupportedDiagnostics)
            {
                if (string.IsNullOrEmpty(descriptor.HelpLinkUri))
                    continue;

                int hashIndex = descriptor.HelpLinkUri.IndexOf('#');
                if (hashIndex < 0 || hashIndex == descriptor.HelpLinkUri.Length - 1)
                    continue;

                string anchor = descriptor.HelpLinkUri[(hashIndex + 1)..];
                results.Add((descriptor.Id, anchor));
            }
        }

        return results;
    }

    /// <summary>
    /// Walks up from <see cref="AppContext.BaseDirectory"/> looking for
    /// <c>tools/Governance/README.md</c>. Throws <see cref="FileNotFoundException"/> rather than
    /// returning a sentinel/skipping — a test that cannot resolve its own fixture must fail, not
    /// silently pass.
    /// </summary>
    private static string FindGovernanceReadmePath()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        for (int i = 0; i < 12 && current is not null; i++, current = current.Parent)
        {
            string candidate = Path.Combine(current.FullName, "tools", "Governance", "README.md");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            $"Could not locate 'tools/Governance/README.md' by walking up from "
                + $"'{AppContext.BaseDirectory}'."
        );
    }
}
