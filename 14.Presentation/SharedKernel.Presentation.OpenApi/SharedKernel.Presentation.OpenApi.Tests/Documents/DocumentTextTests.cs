using System.Reflection;
using FluentAssertions;
using SharedKernel.Presentation.OpenApi.Documents;
using Xunit;

namespace SharedKernel.Presentation.OpenApi.Tests.Documents;

/// <summary>How the configured description combines with what Asp.Versioning writes, and where XML comments come from.</summary>
public sealed class DocumentTextTests
{
    private const string Notices = "This API version has been deprecated.";

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(null, "", null)]
    [InlineData(null, Notices, Notices)]
    [InlineData("Orders.", null, "Orders.")]
    [InlineData("Orders.", "", "Orders.")]
    [InlineData("Orders.", Notices, "Orders.\n\n" + Notices)]
    public void ConfiguredDescription_IsFollowedByTheVersionNotices(string? configured, string? generated, string? expected)
    {
        DocumentText.ComposeDescription(configured, generated, assemblyDescription: null).Should().Be(expected);
    }

    [Theory]
    [InlineData("Legacy description", "Legacy description. " + Notices, "Orders.\n\n" + Notices)]
    [InlineData("Legacy description.", "Legacy description. " + Notices, "Orders.\n\n" + Notices)]
    [InlineData("Legacy description", "Legacy description", "Orders.")]
    public void ConfiguredDescription_ReplacesTheEntryAssemblyDescription(string assemblyDescription, string generated, string expected)
    {
        DocumentText.ComposeDescription("Orders.", generated, assemblyDescription).Should().Be(expected);
    }

    [Fact]
    public void WithoutAConfiguredDescription_TheEntryAssemblyDescriptionIsKept()
    {
        DocumentText.ComposeDescription(null, "Legacy description", "Legacy description").Should().Be("Legacy description");
    }

    [Fact]
    public void XmlComments_AreLookedUpForTheAssembly_InItsDirectoryThenTheContentRootThenTheBaseDirectory()
    {
        var assembly = typeof(DocumentTextTests).Assembly;
        var name = assembly.GetName().Name + ".xml";
        var contentRoot = Directory.CreateTempSubdirectory("openapi-xml-").FullName;

        try
        {
            EntryAssemblyXmlComments.FindPath(assembly, contentRoot, baseDirectory: null)
                .Should().BeEmpty("the test assembly generates no documentation file");

            File.WriteAllText(Path.Join(contentRoot, name), "<doc />");

            EntryAssemblyXmlComments.FindPath(assembly, contentRoot, baseDirectory: null).Should().Be(Path.Join(contentRoot, name));
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public void XmlComments_WithoutAnAssembly_AreNotLookedUp()
    {
        EntryAssemblyXmlComments.FindPath(assembly: null, contentRootPath: null, baseDirectory: null).Should().BeEmpty();
        EntryAssemblyXmlComments.FindPath(Assembly.GetExecutingAssembly(), contentRootPath: "", baseDirectory: "").Should().BeEmpty();
    }
}
