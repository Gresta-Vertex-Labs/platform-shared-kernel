using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace SharedKernel.Linter.Tests;

/// <summary>
/// Asserts the packaging contract of <c>SharedKernel.Linter</c>: that its MSBuild logic is packed
/// where NuGet will actually import it, and that the package ships exactly the config it claims.
/// </summary>
/// <remarks>
/// <para>
/// These exist because the package once shipped completely inert. Its props and targets were
/// packed under <c>content/build/</c> — the legacy packages.config convention that
/// <c>PackageReference</c> ignores outright — so it restored cleanly, imported nothing, and
/// enforced nothing. Every assertion here corresponds to one way that can come back.
/// </para>
/// </remarks>
public class PackageLayoutTests
{
    [Fact]
    public void PropsAndTargets_ArePackedToPackageRootBuildFolder()
    {
        var packPaths = LinterPackage
            .Csproj.Descendants("None")
            .Where(none =>
                (none.Attribute("Include")?.Value ?? string.Empty).EndsWith(
                    ".props",
                    StringComparison.OrdinalIgnoreCase
                )
                || (none.Attribute("Include")?.Value ?? string.Empty).EndsWith(
                    ".targets",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .Select(none => none.Attribute("PackagePath")?.Value)
            .ToArray();

        packPaths
            .Should()
            .HaveCount(2, because: "the package ships exactly one .props and one .targets");
        packPaths
            .Should()
            .AllSatisfy(path =>
                path.Should()
                    .BeOneOf(
                        ["build\\", "build/"],
                        because: "NuGet auto-imports build/$(PackageId).props|.targets from the PACKAGE ROOT only; "
                            + "anything under content/ or contentFiles/ is never imported for a PackageReference"
                    )
            );
    }

    [Fact]
    public void PropsAndTargets_AreNamedExactlyAfterThePackageId()
    {
        // NuGet matches on the file name, not on content: build/SomethingElse.props is never
        // imported no matter what it contains.
        LinterPackage.PropsPath.Name.Should().Be("SharedKernel.Linter.props");
        LinterPackage.TargetsPath.Name.Should().Be("SharedKernel.Linter.targets");
    }

    [Fact]
    public void Package_DoesNotUseBuildTransitive()
    {
        // A formatting gate must not appear in someone's build because they referenced a library
        // that referenced this package. build/ applies to direct references only.
        var packagePaths = LinterPackage
            .Csproj.Descendants("None")
            .Select(none => none.Attribute("PackagePath")?.Value ?? string.Empty);

        packagePaths
            .Should()
            .NotContain(path =>
                path.Contains("buildTransitive", StringComparison.OrdinalIgnoreCase)
            );
    }

    [Fact]
    public void Package_ShipsNoAssemblyAndIsMarkedDevelopmentDependency()
    {
        LinterPackage.Property("IncludeBuildOutput").Should().Be("false");
        LinterPackage.Property("DevelopmentDependency").Should().Be("true");
        LinterPackage.Property("IsPackable").Should().Be("true");
    }

    [Fact]
    public void Package_DeclaresDescriptionAndTags()
    {
        // SKPKG001/SKPKG002 fail the pack without these; asserting here reports the cause in a
        // test name rather than as a build error at publish time.
        LinterPackage.Property("Description").Should().NotBeNullOrWhiteSpace();
        LinterPackage.Property("PackageTags").Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Package_ShipsTheEditorConfigUnderBuildConfig()
    {
        var editorConfig = LinterPackage
            .Csproj.Descendants("None")
            .SingleOrDefault(none =>
                (none.Attribute("Include")?.Value ?? string.Empty).EndsWith(
                    ".editorconfig",
                    StringComparison.OrdinalIgnoreCase
                )
            );

        editorConfig
            .Should()
            .NotBeNull(because: "the install target copies .editorconfig out of the package");
        editorConfig!
            .Attribute("PackagePath")!
            .Value.Should()
            .BeOneOf(["build\\config\\", "build/config/"]);

        LinterPackage.EditorConfigPath.Exists.Should().BeTrue();
    }

    [Fact]
    public void Package_ShipsNoCSharpierRcFile()
    {
        // Measured, not assumed: when a .csharpierrc exists CSharpier uses it INSTEAD of
        // .editorconfig rather than merging the two, so shipping both would mean an edit to
        // indent_size or max_line_length in .editorconfig silently does nothing. One source of
        // truth is the whole design of this package's config half.
        LinterPackage
            .Root.EnumerateFiles(".csharpierrc*", SearchOption.AllDirectories)
            .Should()
            .BeEmpty(
                because: "a .csharpierrc would silently override the entire .editorconfig formatting section"
            );
    }

    [Fact]
    public void Package_DeclaresCSharpierMsBuildAsItsOnlyDependency()
    {
        var packageReferences = LinterPackage
            .Csproj.Descendants("PackageReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .ToArray();

        packageReferences.Should().Equal(["CSharpier.MsBuild"]);
    }

    [Fact]
    public void Package_DeclaresNoSharedKernelDependency()
    {
        // This package is published out of band rather than through the tag-triggered,
        // whole-solution release path, which is only sound while it has no SharedKernel.*
        // dependency to resolve at a version that was never published.
        var projectReferences = LinterPackage.Csproj.Descendants("ProjectReference");

        projectReferences.Should().BeEmpty();
    }

    [Fact]
    public void Targets_ExposeTheDocumentedTargets()
    {
        var targetNames = LinterPackage
            .Targets.Descendants()
            .Where(element => element.Name.LocalName == "Target")
            .Select(element => element.Attribute("Name")?.Value)
            .ToArray();

        targetNames
            .Should()
            .Contain(
                "InstallSharedKernelLinterConfig",
                because: "the README documents it as the config install entry point"
            )
            .And.Contain(
                "SharedKernelLinterFormat",
                because: "the README documents it as the local format entry point"
            );
    }

    [Fact]
    public void InstallTarget_NeverOverwritesAnExistingFileWithoutAnExplicitOptIn()
    {
        var copy = LinterPackage
            .Targets.Descendants()
            .Single(element => element.Name.LocalName == "Copy");

        var condition = copy.Attribute("Condition")?.Value ?? string.Empty;

        condition
            .Should()
            .Contain(
                "SharedKernelLinterOverwriteConfig",
                because: "overwriting must require an explicit opt-in"
            )
            .And.Contain(
                "!Exists(",
                because: "the copy is conditioned on the destination file being absent"
            );
    }
}
