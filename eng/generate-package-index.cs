// Generates the package views that answer "which package do I reference, and what depends on what":
//
//   dotnet run eng/generate-package-index.cs            writes the files below
//   dotnet run eng/generate-package-index.cs -- --check fails (exit 1) when any of them is out of date
//
//   docs/packages.md                                     every packable package by tier, with the service
//                                                        project that references it and its SharedKernel deps
//   docs/dependency-graph.md                             Mermaid graphs: folders, then one graph per folder
//   eng/solution-filters/Platform.SharedKernel.{Tier}.slnf   one IDE view per tier (packages + their tests)
//   consumer/skill/sharedkernel/SKILL.md                 the generated package-catalog region of the consumer skill
//   tools/Governance/SharedKernel.ArchitectureTests/Data/kernel-package-tiers.tsv
//                                                        the package-tier index embedded in SharedKernel.ArchitectureTests
//                                                        (one "{PackageId}<TAB>{Tier}" line per packable package)
//
//   dotnet run eng/generate-package-index.cs -- --list-purposes   prints each package's catalog purpose (README audit)
//
// Inputs are only what the build already enforces: each tracked .csproj's <SharedKernelTier>, <IsPackable>
// and ProjectReferences, plus the first sentence of each package README's callout for the skill catalog.
// Analyzer-only references (ReferenceOutputAssembly="false") are not dependencies.
// Output is deterministic and LF-terminated; --check ignores CR so a Windows checkout compares equal.
//
// The consumer skill is also validated, in both modes (consumer/README.md lists the rules): every
// Model/Abstractions/Adapter/Host package is routed by the needs table or a not-routed comment, every
// SharedKernel id outside code is a real package, recipes and the recipe list match, proven-by paths
// exist, and no shipped file uses maintainer vocabulary.

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

var check = args.Contains("--check");
var root = FindRepositoryRoot();

var tiers = new (string Name, string ReferenceFrom, string MayReference, string ServiceProject)[]
{
    ("Foundation", "any project", "Foundation", "any"),
    ("Model", "the Domain project", "Foundation, Model", "Domain"),
    ("Abstractions", "the Application project", "Foundation, Model, Abstractions", "Application"),
    ("Adapter", "the Infrastructure project", "Foundation, Model, Abstractions, declared adapter edges", "Infrastructure"),
    ("Host", "the Api / Worker project", "everything except Testing and Tooling", "Api / Worker"),
    ("Testing", "test projects only", "everything except Tooling", "Tests"),
    ("Tooling", "the build (analyzers, architecture tests, linter)", "nothing", "the build"),
};
var routedTiers = new[] { "Model", "Abstractions", "Adapter", "Host" };
const string SkillDirectory = "consumer/skill/sharedkernel";
const string CatalogBegin = "<!-- BEGIN GENERATED: package-catalog -->";
const string CatalogEnd = "<!-- END GENERATED: package-catalog -->";
var problems = new List<string>();

var projects = TrackedFiles(root, "*.csproj")
    .Select(path => ReadProject(root, path))
    .ToDictionary(p => p.FullPath, StringComparer.OrdinalIgnoreCase);

var packages = projects.Values
    .Where(p => p.Tier is not null && !string.Equals(p.IsPackable, "false", StringComparison.OrdinalIgnoreCase))
    .OrderBy(p => p.Name, StringComparer.Ordinal)
    .ToList();
var packageByPath = packages.ToDictionary(p => p.FullPath, StringComparer.OrdinalIgnoreCase);

List<Project> DependenciesOf(Project package) =>
    package.References
        .Where(packageByPath.ContainsKey)
        .Select(path => packageByPath[path])
        .OrderBy(p => p.Name, StringComparer.Ordinal)
        .ToList();

var outputs = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["docs/packages.md"] = PackagesMarkdown(),
    ["docs/dependency-graph.md"] = GraphMarkdown(),
    ["tools/Governance/SharedKernel.ArchitectureTests/Data/kernel-package-tiers.tsv"] = TierIndexTsv(),
};

var solutionProjects = SolutionProjects(Path.Combine(root, "Platform.SharedKernel.slnx"));
foreach (var tier in tiers)
{
    outputs[$"eng/solution-filters/Platform.SharedKernel.{tier.Name}.slnf"] = SolutionFilter(tier.Name);
}

var purposes = packages.ToDictionary(p => p.Name, Purpose, StringComparer.Ordinal);
if (args.Contains("--list-purposes"))
{
    foreach (var package in packages)
    {
        Console.WriteLine($"{package.Name}: {purposes[package.Name] ?? "(no callout)"}");
    }

    return 0;
}

var skillPath = $"{SkillDirectory}/SKILL.md";
if (File.Exists(Path.Combine(root, skillPath)))
{
    var skill = SkillWithCatalog(File.ReadAllText(Path.Combine(root, skillPath)).Replace("\r", ""));
    if (skill is not null)
    {
        outputs[skillPath] = skill;
        ValidateSkill(skill);
    }
}
else
{
    problems.Add($"{skillPath} is missing.");
}

var stale = new List<string>();
foreach (var (relative, content) in outputs)
{
    var path = Path.Combine(root, relative);
    var current = File.Exists(path) ? File.ReadAllText(path).Replace("\r", "") : null;
    if (current == content)
    {
        continue;
    }

    stale.Add(relative);
    if (!check)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}

if (problems.Count > 0)
{
    Console.Error.WriteLine("::error::The consumer skill is invalid (consumer/README.md lists the rules):");
    problems.ForEach(problem => Console.Error.WriteLine($"  {problem}"));
}

if (check && stale.Count > 0)
{
    Console.Error.WriteLine("::error::Generated package views are out of date. Run: dotnet run eng/generate-package-index.cs");
    stale.ForEach(file => Console.Error.WriteLine($"  {file}"));
    return 1;
}

if (problems.Count > 0)
{
    return 1;
}

Console.WriteLine(check
    ? $"OK: {outputs.Count} generated package views are up to date ({packages.Count} packages)."
    : $"{stale.Count} of {outputs.Count} package views updated ({packages.Count} packages).");
return 0;

string PackagesMarkdown()
{
    var md = new StringBuilder();
    md.Append("# Package index\n\n");
    md.Append("> Generated by `eng/generate-package-index.cs` from each project's `<SharedKernelTier>` and `ProjectReference`s.\n");
    md.Append("> Do not edit by hand: run `dotnet run eng/generate-package-index.cs`. CI fails when this file is out of date.\n\n");
    md.Append($"{packages.Count} packages. The tier decides which project of a consuming service references a package; the build enforces it.\n\n");
    md.Append("| Tier | Reference it from | May reference | Packages |\n|---|---|---|---:|\n");
    foreach (var tier in tiers)
    {
        md.Append($"| [{tier.Name}](#{tier.Name.ToLowerInvariant()}) | {tier.ReferenceFrom} | {tier.MayReference} | {packages.Count(p => p.Tier == tier.Name)} |\n");
    }

    md.Append("\nSee also [dependency-graph.md](dependency-graph.md) and the per-tier solution filters in `eng/solution-filters/`.\n");

    foreach (var tier in tiers)
    {
        var inTier = packages.Where(p => p.Tier == tier.Name).ToList();
        if (inTier.Count == 0)
        {
            continue;
        }

        md.Append($"\n## {tier.Name}\n\nReference from {tier.ReferenceFrom}.\n\n");
        md.Append("| Package | Folder | SharedKernel dependencies |\n|---|---|---|\n");
        foreach (var package in inTier)
        {
            var dependencies = DependenciesOf(package);
            var dependencyText = dependencies.Count == 0 ? "—" : string.Join(", ", dependencies.Select(d => $"`{d.Name}`"));
            md.Append($"| [`{package.Name}`](../{package.Directory}/README.md) | `{package.Folder}` | {dependencyText} |\n");
        }
    }

    return md.ToString();
}

// Embedded by SharedKernel.ArchitectureTests (KernelPackageIndex): packages are already ordinal-sorted by name.
string TierIndexTsv()
{
    var tsv = new StringBuilder();
    tsv.Append("# Generated by eng/generate-package-index.cs from each packable project's <SharedKernelTier>. Do not edit by hand.\n");
    foreach (var package in packages)
    {
        tsv.Append($"{package.Name}\t{package.Tier}\n");
    }

    return tsv.ToString();
}

string GraphMarkdown()
{
    var md = new StringBuilder();
    md.Append("# Dependency graph\n\n");
    md.Append("> Generated by `eng/generate-package-index.cs` from `ProjectReference`s between packable projects.\n");
    md.Append("> Do not edit by hand: run `dotnet run eng/generate-package-index.cs`. CI fails when this file is out of date.\n\n");
    md.Append("An arrow `A --> B` means A references B. Tiers are listed in [packages.md](packages.md).\n\n");

    var folders = packages.Select(p => p.Folder).Distinct().OrderBy(f => f, StringComparer.Ordinal).ToList();

    md.Append("## Between folders\n\nEach arrow is labelled with the number of package references it stands for.\n\n");
    md.Append("```mermaid\nflowchart LR\n");
    foreach (var folder in folders)
    {
        md.Append($"  {NodeId(folder)}[\"{folder}\"]\n");
    }

    var folderEdges = packages
        .SelectMany(p => DependenciesOf(p).Select(d => (From: p.Folder, To: d.Folder)))
        .Where(e => e.From != e.To)
        .GroupBy(e => e)
        .OrderBy(g => g.Key.From, StringComparer.Ordinal)
        .ThenBy(g => g.Key.To, StringComparer.Ordinal);
    foreach (var edge in folderEdges)
    {
        md.Append($"  {NodeId(edge.Key.From)} -->|{edge.Count()}| {NodeId(edge.Key.To)}\n");
    }

    md.Append("```\n");

    foreach (var folder in folders)
    {
        var own = packages.Where(p => p.Folder == folder).ToList();
        var external = own.SelectMany(DependenciesOf).Where(d => d.Folder != folder)
            .DistinctBy(d => d.FullPath).OrderBy(d => d.Name, StringComparer.Ordinal).ToList();

        md.Append($"\n## {folder}\n\n```mermaid\nflowchart LR\n");
        md.Append($"  subgraph {NodeId("folder " + folder)}[\"{folder}\"]\n");
        foreach (var package in own)
        {
            md.Append($"    {NodeId(package.Name)}[\"{ShortName(package.Name)}<br/><i>{package.Tier}</i>\"]\n");
        }

        md.Append("  end\n");
        foreach (var package in external)
        {
            md.Append($"  {NodeId(package.Name)}([\"{ShortName(package.Name)}<br/><i>{package.Folder}</i>\"])\n");
        }

        foreach (var package in own)
        {
            foreach (var dependency in DependenciesOf(package))
            {
                md.Append($"  {NodeId(package.Name)} --> {NodeId(dependency.Name)}\n");
            }
        }

        md.Append("```\n");
    }

    return md.ToString();
}

string SolutionFilter(string tier)
{
    var included = new SortedSet<string>(StringComparer.Ordinal);
    foreach (var package in packages.Where(p => p.Tier == tier))
    {
        // The package and the projects nested in its folder (its .Tests project).
        foreach (var project in solutionProjects)
        {
            if (project == package.RelativePath || project.StartsWith(package.Directory + "/", StringComparison.Ordinal))
            {
                included.Add(project);
            }
        }
    }

    var json = new StringBuilder();
    json.Append("{\n  \"solution\": {\n    \"path\": \"../../Platform.SharedKernel.slnx\",\n    \"projects\": [\n");
    json.Append(string.Join(",\n", included.Select(p => $"      \"{p}\"")));
    json.Append("\n    ]\n  }\n}\n");
    return json.ToString();
}

// The first sentence of the README callout (the first blockquote after the title), Markdown stripped.
string? Purpose(Project package)
{
    var readme = Path.Combine(root, package.Directory, "README.md");
    if (!File.Exists(readme))
    {
        return null;
    }

    var quote = File.ReadAllLines(readme)
        .SkipWhile(line => !line.StartsWith('>'))
        .TakeWhile(line => line.StartsWith('>'))
        .Select(line => line.TrimStart('>').Trim())
        .ToList();
    if (quote.Count == 0)
    {
        return null;
    }

    var text = Regex.Replace(string.Join(' ', quote), @"\[([^\]]*)\]\([^)]*\)", "$1");
    text = text.Replace("**", "").Replace("__", "");
    var sentence = FirstSentence(text).Replace("`", "").Replace("|", "\\|").Trim();
    return sentence.Length == 0 ? null : sentence;
}

// Ends at the first '.', '!' or '?' followed by whitespace or the end, ignoring code spans ("Result<T>.Value").
static string FirstSentence(string text)
{
    var inCode = false;
    for (var i = 0; i < text.Length; i++)
    {
        var c = text[i];
        if (c == '`')
        {
            inCode = !inCode;
        }
        else if (!inCode && c is '.' or '!' or '?' && (i + 1 == text.Length || char.IsWhiteSpace(text[i + 1])))
        {
            return text[..(i + 1)];
        }
    }

    return text;
}

string? SkillWithCatalog(string skill)
{
    var begin = skill.IndexOf(CatalogBegin, StringComparison.Ordinal);
    var end = skill.IndexOf(CatalogEnd, StringComparison.Ordinal);
    if (begin < 0 || end < begin)
    {
        problems.Add($"{skillPath}: the '{CatalogBegin}' / '{CatalogEnd}' markers are missing.");
        return null;
    }

    var table = new StringBuilder();
    table.Append("| Package | Tier | Reference from | Purpose |\n| --- | --- | --- | --- |\n");
    foreach (var tier in tiers)
    {
        foreach (var package in packages.Where(p => p.Tier == tier.Name))
        {
            var purpose = purposes[package.Name];
            if (purpose is null)
            {
                problems.Add($"{package.Directory}/README.md has no callout ('> **…**' after the title) to take the catalog purpose from.");
            }

            table.Append($"| `{package.Name}` | {tier.Name} | {tier.ServiceProject} | {purpose ?? "—"} |\n");
        }
    }

    return skill[..(begin + CatalogBegin.Length)] + "\n" + table + skill[end..];
}

void ValidateSkill(string skill)
{
    var directory = Path.Combine(root, SkillDirectory);
    var files = Directory.EnumerateFiles(directory, "*.md", SearchOption.AllDirectories)
        .Select(path => (Relative: Path.GetRelativePath(root, path).Replace('\\', '/'), Text: File.ReadAllText(path).Replace("\r", "")))
        .Select(file => file.Relative == skillPath ? (file.Relative, Text: skill) : file)
        .OrderBy(file => file.Relative, StringComparer.Ordinal)
        .ToList();
    var names = packages.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

    // Routing coverage: the needs table (section "Needs") or a not-routed comment names every routed package.
    var needs = Regex.Match(skill, @"^## [^\n]*Needs[^\n]*\n(.*?)(?=^## )", RegexOptions.Multiline | RegexOptions.Singleline).Groups[1].Value;
    var routed = Regex.Matches(string.Join('\n', needs.Split('\n').Where(line => line.StartsWith('|'))), @"`(SharedKernel(?:\.[A-Za-z0-9]+)+)`")
        .Select(m => m.Groups[1].Value)
        .ToHashSet(StringComparer.Ordinal);
    foreach (Match notRouted in Regex.Matches(skill, @"<!--\s*not-routed:\s*([A-Za-z0-9.]+)\s*(?:—|-)+\s*(.*?)\s*-->"))
    {
        if (notRouted.Groups[2].Value.Length == 0)
        {
            problems.Add($"{skillPath}: not-routed '{notRouted.Groups[1].Value}' gives no reason.");
        }

        routed.Add(notRouted.Groups[1].Value);
    }

    foreach (var package in packages.Where(p => routedTiers.Contains(p.Tier)))
    {
        if (!routed.Contains(package.Name))
        {
            problems.Add($"{skillPath}: {package.Tier} package '{package.Name}' is in no needs-table row; add a row or a '<!-- not-routed: {package.Name} — reason -->' comment.");
        }
    }

    // Recipes: the list in SKILL.md and the files in recipes/ match, and recipes name only existing recipes.
    var recipeFiles = files.Where(f => f.Relative.StartsWith($"{SkillDirectory}/recipes/", StringComparison.Ordinal))
        .ToDictionary(f => Path.GetFileName(f.Relative), f => f, StringComparer.Ordinal);
    var recipeList = Regex.Match(skill, @"^## [^\n]*Recipes[^\n]*\n(.*?)(?=^## |\z)", RegexOptions.Multiline | RegexOptions.Singleline).Groups[1].Value;
    var listed = RecipeNames(recipeList);
    foreach (var name in listed.Where(n => !recipeFiles.ContainsKey(n)))
    {
        problems.Add($"{skillPath}: recipe '{name}' is listed but recipes/{name} does not exist.");
    }

    foreach (var name in recipeFiles.Keys.Where(n => !listed.Contains(n)))
    {
        problems.Add($"{SkillDirectory}/recipes/{name} is not in the recipe list of SKILL.md.");
    }

    foreach (var file in files.Where(f => f.Relative != skillPath || !listed.SetEquals(RecipeNames(StripCode(f.Text)))))
    {
        foreach (var name in RecipeNames(StripCode(file.Text)).Where(n => !recipeFiles.ContainsKey(n) && !(file.Relative == skillPath && listed.Contains(n))))
        {
            problems.Add($"{file.Relative}: names recipe '{name}', which does not exist.");
        }
    }

    foreach (var file in recipeFiles.Values)
    {
        ValidateRecipeFrontmatter(file.Relative, file.Text);
    }

    foreach (var file in files)
    {
        // No dangling ids: a SharedKernel token outside code is a package or a namespace inside one.
        foreach (Match id in Regex.Matches(StripCode(StripFrontmatter(file.Text)), @"(?<![\w.])SharedKernel(?:\.[A-Za-z0-9]+)+"))
        {
            var value = id.Value;
            if (!names.Contains(value) && !names.Any(n => value.StartsWith(n + ".", StringComparison.Ordinal)))
            {
                problems.Add($"{file.Relative}: '{value}' is not a SharedKernel package.");
            }
        }

        // Consumer vocabulary, everywhere in the shipped files.
        foreach (var pattern in new[] { @"(?<![\w.])\d{2}\.[A-Z][A-Za-z]+", @"CLAUDE\.md", @"state-map", @"\bP-\d{3,}\b", @"\bWO-\d{3,}\b" })
        {
            foreach (Match hit in Regex.Matches(file.Text, pattern))
            {
                problems.Add($"{file.Relative}: '{hit.Value}' is maintainer vocabulary; consumers never see it.");
            }
        }
    }
}

void ValidateRecipeFrontmatter(string relative, string text)
{
    var frontmatter = Regex.Match(text, @"\A---\n(.*?)\n---\n", RegexOptions.Singleline);
    if (!frontmatter.Success)
    {
        problems.Add($"{relative}: has no frontmatter with 'proven-by'.");
        return;
    }

    string? key = null;
    var proven = 0;
    foreach (var line in frontmatter.Groups[1].Value.Split('\n'))
    {
        var keyMatch = Regex.Match(line, @"^([a-z-]+):\s*$");
        if (keyMatch.Success)
        {
            key = keyMatch.Groups[1].Value;
            continue;
        }

        var item = Regex.Match(line, @"^\s+-\s+(.+?)\s*$");
        if (!item.Success)
        {
            continue;
        }

        if (key == "proven-by")
        {
            proven++;
            var path = item.Groups[1].Value.Split(' ', 2)[0];
            if (!File.Exists(Path.Combine(root, path)) && !Directory.Exists(Path.Combine(root, path)))
            {
                problems.Add($"{relative}: proven-by path '{path}' does not exist.");
            }
        }
        else if (key == "unproven" && !Regex.IsMatch(item.Groups[1].Value, @":\s*\S"))
        {
            problems.Add($"{relative}: unproven entry '{item.Groups[1].Value}' needs ': reason'.");
        }
    }

    if (proven == 0)
    {
        problems.Add($"{relative}: lists no proven-by path.");
    }
}

static HashSet<string> RecipeNames(string text) =>
    Regex.Matches(text, @"(?<![\w/.-])([a-z][a-z0-9]*(?:-[a-z0-9]+)+\.md)\b")
        .Select(m => m.Groups[1].Value)
        .ToHashSet(StringComparer.Ordinal);

// Removes fenced code blocks, including fences indented under a list item; `using` lines and
// namespaces in snippets are not package references.
static string StripCode(string text) =>
    Regex.Replace(text, @"^[ \t]*(`{3,}|~{3,}).*?^[ \t]*\1[ \t]*$", "", RegexOptions.Multiline | RegexOptions.Singleline);

static string StripFrontmatter(string text) => Regex.Replace(text, @"\A---\n.*?\n---\n", "", RegexOptions.Singleline);

static string ShortName(string name) => name.StartsWith("SharedKernel.", StringComparison.Ordinal) ? name["SharedKernel.".Length..] : name;

static string NodeId(string value) => new(value.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray());

static Project ReadProject(string root, string relativePath)
{
    var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
    var document = XDocument.Load(fullPath);
    string? Property(string name) => document.Descendants(name).Select(e => e.Value.Trim()).LastOrDefault();

    var projectDirectory = Path.GetDirectoryName(fullPath)!;
    var references = document.Descendants("ProjectReference")
        .Where(r => !string.Equals((string?)r.Attribute("ReferenceOutputAssembly") ?? r.Element("ReferenceOutputAssembly")?.Value, "false", StringComparison.OrdinalIgnoreCase))
        .Select(r => (string?)r.Attribute("Include"))
        .Where(include => !string.IsNullOrWhiteSpace(include))
        .Select(include => Path.GetFullPath(Path.Combine(projectDirectory, include!.Replace('\\', Path.DirectorySeparatorChar))))
        .ToList();

    var directory = Path.GetDirectoryName(relativePath)!.Replace('\\', '/');
    var folder = Path.GetDirectoryName(directory)?.Replace('\\', '/');
    return new Project(
        Path.GetFileNameWithoutExtension(fullPath),
        fullPath,
        relativePath.Replace('\\', '/'),
        directory,
        string.IsNullOrEmpty(folder) ? "." : folder,
        Property("SharedKernelTier"),
        Property("IsPackable"),
        references);
}

static List<string> SolutionProjects(string slnx) =>
    XDocument.Load(slnx).Descendants("Project")
        .Select(p => ((string?)p.Attribute("Path"))?.Replace('\\', '/'))
        .Where(p => p is not null)
        .Select(p => p!)
        .ToList();

static List<string> TrackedFiles(string root, string pattern)
{
    var git = Process.Start(new ProcessStartInfo("git", $"ls-files -- \"{pattern}\"")
    {
        WorkingDirectory = root,
        RedirectStandardOutput = true,
        UseShellExecute = false,
    })!;
    var output = git.StandardOutput.ReadToEnd();
    git.WaitForExit();
    if (git.ExitCode != 0)
    {
        throw new InvalidOperationException("git ls-files failed.");
    }

    return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(path => !path.StartsWith("archive/", StringComparison.Ordinal))
        .ToList();
}

static string FindRepositoryRoot()
{
    for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Platform.SharedKernel.slnx")))
        {
            return directory.FullName;
        }
    }

    throw new InvalidOperationException("Run from inside the repository (no Platform.SharedKernel.slnx found above the current directory).");
}

record Project(
    string Name,
    string FullPath,
    string RelativePath,
    string Directory,
    string Folder,
    string? Tier,
    string? IsPackable,
    List<string> References);
