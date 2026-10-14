using System.Reflection;
using System.Reflection.Emit;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Telemetry;
using Xunit;

namespace SharedKernel.Primitives.Tests.Telemetry;

public sealed class TelemetryVersionTests
{
    [Fact]
    public void Unknown_IsPinnedLiteral() => Assert.Equal("0.0.0", TelemetryVersion.Unknown);

    [Fact]
    public void Of_RealAssembly_EqualsTheInformationalVersionAttribute()
    {
        var assembly = typeof(Error).Assembly;
        var expected = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

        var actual = TelemetryVersion.Of(assembly);

        Assert.Equal(expected, actual);
        Assert.NotEqual(TelemetryVersion.Unknown, actual);
    }

    [Fact]
    public void Of_AssemblyWithInformationalVersion_KeepsBuildMetadata()
    {
        var assembly = DefineAssembly("TelemetryVersionInformational", new Version(9, 9, 9, 9), informational: "1.4.0+3f2a9c1");

        Assert.Equal("1.4.0+3f2a9c1", TelemetryVersion.Of(assembly));
    }

    [Fact]
    public void Of_DynamicAssemblyWithoutAttribute_FallsBackToAssemblyVersion()
    {
        var assembly = DefineAssembly("TelemetryVersionAssemblyVersion", new Version(1, 2, 3, 4), informational: null);

        Assert.Equal("1.2.3.4", TelemetryVersion.Of(assembly));
    }

    [Fact]
    public void Of_DynamicAssemblyWithNeitherVersion_ReturnsUnknown()
    {
        var assembly = DefineAssembly("TelemetryVersionNone", version: null, informational: null);

        Assert.Equal(TelemetryVersion.Unknown, TelemetryVersion.Of(assembly));
    }

    [Fact]
    public void Of_NullAssembly_Throws()
        => Assert.Throws<ArgumentNullException>(() => TelemetryVersion.Of(null!));

    private static Assembly DefineAssembly(string name, Version? version, string? informational)
    {
        var assemblyName = new AssemblyName(name);
        if (version is not null)
            assemblyName.Version = version;

        var builder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        if (informational is not null)
        {
            var constructor = typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!;
            builder.SetCustomAttribute(new CustomAttributeBuilder(constructor, [informational]));
        }

        return builder;
    }
}
