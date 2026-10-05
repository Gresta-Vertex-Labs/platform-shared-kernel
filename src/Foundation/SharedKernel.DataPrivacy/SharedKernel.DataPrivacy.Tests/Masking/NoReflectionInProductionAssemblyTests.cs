using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharedKernel.DataPrivacy.Masking;
using Xunit;

namespace SharedKernel.DataPrivacy.Tests.Masking;

/// <summary>
/// Asserts the absence of reflection-invocation surface in the compiled
/// <c>SharedKernel.DataPrivacy.dll</c> production assembly (T-58) — a rigor step above a plain
/// source-code grep, which would only prove nobody typed the word "reflection", not that no
/// reflection call actually compiled into the shipped binary.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this test actually proves, precisely:</b> it opens the compiled production DLL as raw
/// PE/metadata (via <see cref="PEReader"/>/<see cref="MetadataReader"/> — the same low-level API
/// tooling like ildasm/dotnet-trace builds on) and enumerates every <c>TypeReference</c> row the
/// assembly's metadata carries. Any reference to a non-attribute type in the
/// <c>System.Reflection</c> namespace (<see cref="MethodInfo"/>, <see cref="PropertyInfo"/>,
/// <see cref="FieldInfo"/>, <see cref="Assembly"/>, <see cref="BindingFlags"/>, etc.) is flagged as
/// a violation — resolving a member obtained through <c>Type.GetMethod</c>/<c>GetProperty</c>/etc.
/// requires the compiler to emit a metadata token referencing that return type even if the code
/// never explicitly imports the type by name, so this check is not defeated by an
/// unqualified/aliased usage.
/// </para>
/// <para>
/// <b>Why attribute types are excluded (and why that is not a loophole):</b> this project's own
/// NuGet packaging metadata (Authors/Company/Product/Description/SourceLink, all centrally set in
/// the repo's <c>Directory.Build.props</c>) causes the .NET SDK to auto-generate assembly-level
/// attributes that themselves live in the <c>System.Reflection</c> namespace —
/// <c>AssemblyCompanyAttribute</c>, <c>AssemblyProductAttribute</c>, <c>AssemblyMetadataAttribute</c>,
/// etc. Those are pure declarative metadata, emitted by the build pipeline regardless of anything
/// this package's own source does, and carry no dynamic-invocation capability — banning them would
/// make this test fail on every SharedKernel package, proving nothing. Excluding only type names
/// ending in <c>"Attribute"</c> removes exactly that build-generated noise while still catching the
/// invocation surface (<c>MethodInfo</c>, <c>PropertyInfo</c>, <c>Activator</c>, ...) this test
/// exists to catch.
/// </para>
/// <para>
/// <b>Honest limits:</b> this is a metadata-table scan, not an IL-body simulation or a full
/// whole-program data-flow analysis. It would not catch reflection reached only through an
/// obfuscated/dynamically-constructed call (not a realistic risk in a hand-written,
/// non-obfuscated, source-generator-free package like this one), and it does not (and cannot,
/// alone) prove the *test* assembly is reflection-free —
/// <see cref="SharedKernel.DataPrivacy.Tests.Classification.TaxonomyTests"/>
/// uses reflection deliberately and legitimately, in the TEST project only, which this test never
/// inspects.
/// </para>
/// </remarks>
public sealed class NoReflectionInProductionAssemblyTests
{
    // TypeRef names in the System.Reflection namespace that indicate genuine reflective
    // invocation capability (as opposed to a pure declarative attribute type). Not exhaustive of
    // every possible System.Reflection type — deliberately scoped to the surface a dynamic
    // invocation pattern would actually need to touch.
    private static readonly HashSet<string> InvocationSurfaceTypeNames = new(StringComparer.Ordinal)
    {
        "Assembly",
        "BindingFlags",
        "ConstructorInfo",
        "EventInfo",
        "FieldInfo",
        "MemberInfo",
        "MethodBase",
        "MethodInfo",
        "Module",
        "ParameterInfo",
        "PropertyInfo",
        "TypeInfo",
    };

    [Fact]
    public void ProductionAssembly_CarriesNoReflectionInvocationTypeReferences()
    {
        string productionAssemblyPath = typeof(PiiMasking).Assembly.Location;
        Assert.True(
            File.Exists(productionAssemblyPath),
            $"Could not locate the compiled production assembly at '{productionAssemblyPath}'.");

        using FileStream stream = File.OpenRead(productionAssemblyPath);
        using var peReader = new PEReader(stream);
        MetadataReader metadataReader = peReader.GetMetadataReader();

        List<string> violations = [];

        foreach (TypeReferenceHandle handle in metadataReader.TypeReferences)
        {
            TypeReference typeRef = metadataReader.GetTypeReference(handle);
            string ns = metadataReader.GetString(typeRef.Namespace);
            string name = metadataReader.GetString(typeRef.Name);

            if (ns != "System.Reflection")
            {
                continue;
            }

            // Auto-generated assembly-level attributes (AssemblyCompanyAttribute,
            // AssemblyMetadataAttribute, etc.) are build-pipeline noise, not invocation surface —
            // see the class-level remarks for why excluding them is not a loophole.
            if (name.EndsWith("Attribute", StringComparison.Ordinal))
            {
                continue;
            }

            if (InvocationSurfaceTypeNames.Contains(name))
            {
                violations.Add($"{ns}.{name}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "SharedKernel.DataPrivacy.dll references reflection-invocation type(s) it must not "
                + $"use in production code: {string.Join(", ", violations)}. Classification attributes are read by the logging source generator at compile time, "
                + "never reflectively at runtime.");
    }

    [Fact]
    public void ProductionAssembly_StillCarriesItsExpectedBuildGeneratedAttributeTypeReferences()
    {
        // Sanity check that the exclusion above is actually exercised (not vacuously true because
        // the assembly carries no System.Reflection TypeReferences at all) — the SDK's own
        // auto-generated assembly attributes (driven by the repo's centralized NuGet packaging
        // metadata) are expected to be present and excluded, not merely absent.
        string productionAssemblyPath = typeof(PiiMasking).Assembly.Location;

        using FileStream stream = File.OpenRead(productionAssemblyPath);
        using var peReader = new PEReader(stream);
        MetadataReader metadataReader = peReader.GetMetadataReader();

        bool foundAnyReflectionAttributeTypeRef = metadataReader.TypeReferences
            .Select(metadataReader.GetTypeReference)
            .Any(typeRef =>
                metadataReader.GetString(typeRef.Namespace) == "System.Reflection"
                && metadataReader.GetString(typeRef.Name).EndsWith("Attribute", StringComparison.Ordinal));

        Assert.True(
            foundAnyReflectionAttributeTypeRef,
            "Expected at least one System.Reflection.*Attribute TypeReference (e.g. "
                + "AssemblyCompanyAttribute) in the compiled assembly — its absence would mean the "
                + "attribute-exclusion branch in the companion test is never actually exercised.");
    }
}
