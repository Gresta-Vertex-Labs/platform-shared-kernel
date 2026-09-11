using System.Diagnostics;
using FluentAssertions;
using Xunit;

namespace SharedKernel.Linter.Tests;

/// <summary>
/// Evaluates the shipped <c>.props</c> with a real MSBuild invocation and asserts the two
/// properties that decide whether CSharpier runs, and in which mode.
/// </summary>
/// <remarks>
/// <para>
/// Both bugs this package shipped were MSBuild semantics, not content, and neither is visible by
/// reading a file:
/// </para>
/// <list type="number">
///   <item>
///     <description>
///     <c>CSharpier_Check</c> selects the sub-command — <c>check</c> when true, and an EMPTY
///     command otherwise, which makes CSharpier <em>format in place</em>. Leaving it false does
///     not mean "do not run", it means "rewrite the caller's sources during their build", which
///     is what a plain <c>dotnet build</c> was observed doing. <c>CSharpier_Bypass</c> is the
///     actual off switch.
///     </description>
///   </item>
///   <item>
///     <description>
///     CSharpier.MsBuild derives its command line while its own <c>.targets</c> imports, and
///     guards its <c>CSharpier_Check</c> default on the property being empty. So the assignment
///     has to happen in <c>.props</c>, and has to be unconditional, or the result depends on
///     which package NuGet imports first — an order it does not guarantee.
///     </description>
///   </item>
/// </list>
/// <para>
/// Each test therefore imports a stand-in for CSharpier.MsBuild's props alongside the real
/// shipped file, on both sides, and asks MSBuild what it resolved.
/// </para>
/// </remarks>
public class MsBuildSemanticsTests : IDisposable
{
    // Mirrors CSharpier.MsBuild 1.3.0's build/CSharpier.MsBuild.props verbatim in shape: a
    // default guarded on the property being empty, true for Release.
    private const string CSharpierStubProps = """
        <Project>
          <PropertyGroup Condition=" '$(CSharpier_Check)' == '' ">
            <CSharpier_Check>false</CSharpier_Check>
            <CSharpier_Check Condition=" '$(Configuration)' == 'Release' ">true</CSharpier_Check>
          </PropertyGroup>
        </Project>
        """;

    private readonly DirectoryInfo _workspace;

    public MsBuildSemanticsTests()
    {
        // Outside the repository on purpose: inside it, Directory.Build.props/.targets would layer
        // onto the probe and the test would no longer be measuring this package alone.
        _workspace = Directory.CreateDirectory(
            Path.Combine(
                Path.GetTempPath(),
                "sk-linter-msbuild-" + Guid.NewGuid().ToString("N")[..8]
            )
        );
        File.WriteAllText(
            Path.Combine(_workspace.FullName, "CSharpierStub.props"),
            CSharpierStubProps
        );
    }

    public void Dispose()
    {
        try
        {
            _workspace.Delete(recursive: true);
        }
        catch (IOException)
        {
            // A locked file in a temp directory must never fail a test run.
        }
    }

    [Theory]
    [InlineData(ImportOrder.StubFirst)]
    [InlineData(ImportOrder.LinterFirst)]
    public void CSharpierCheck_IsPinnedTrue_RegardlessOfImportOrder(ImportOrder order)
    {
        // The property that keeps a build read-only. If this ever resolves to anything but true,
        // consuming projects get their source files rewritten during an ordinary build.
        var project = WriteProbe(order);

        Evaluate(project, "CSharpier_Check", configuration: "Debug")
            .Should()
            .Be(
                "true",
                because: "an empty CSharpier command means format-in-place, so the check mode must be pinned"
            );

        Evaluate(project, "CSharpier_Check", configuration: "Release").Should().Be("true");
    }

    [Theory]
    [InlineData(ImportOrder.StubFirst)]
    [InlineData(ImportOrder.LinterFirst)]
    public void CSharpierBypass_IsSetOutsideCi_RegardlessOfImportOrder(ImportOrder order)
    {
        var project = WriteProbe(order);

        Evaluate(project, "CSharpier_Bypass", configuration: "Release")
            .Should()
            .Be("true", because: "a local build must not run the formatter at all");
    }

    [Fact]
    public void CSharpierBypass_IsNotSetInCi()
    {
        var project = WriteProbe(ImportOrder.StubFirst);

        Evaluate(
                project,
                "CSharpier_Bypass",
                configuration: "Release",
                ("ContinuousIntegrationBuild", "true")
            )
            .Should()
            .BeEmpty(because: "CI is where the check is meant to run, so nothing may bypass it");
    }

    [Fact]
    public void Enforcement_DefaultsToTheContinuousIntegrationSignal()
    {
        var project = WriteProbe(ImportOrder.StubFirst);

        Evaluate(project, "SharedKernelLinterEnforceFormatting", configuration: "Release")
            .Should()
            .Be("false");

        Evaluate(
                project,
                "SharedKernelLinterEnforceFormatting",
                configuration: "Release",
                ("ContinuousIntegrationBuild", "true")
            )
            .Should()
            .Be("true");
    }

    [Fact]
    public void Enforcement_CanBeForcedOnLocally()
    {
        // Documented in the README as the way to reproduce a CI formatting failure.
        var project = WriteProbe(ImportOrder.StubFirst);

        Evaluate(
                project,
                "CSharpier_Bypass",
                configuration: "Debug",
                ("SharedKernelLinterEnforceFormatting", "true")
            )
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void Enforcement_CanBeOptedOutOfInCi()
    {
        // A consuming service must be able to adopt the package without adopting the gate on the
        // same day, or it will not adopt the package at all.
        var project = WriteProbe(ImportOrder.StubFirst);

        Evaluate(
                project,
                "CSharpier_Bypass",
                configuration: "Release",
                ("ContinuousIntegrationBuild", "true"),
                ("SharedKernelLinterEnforceFormatting", "false")
            )
            .Should()
            .Be("true");
    }

    [Fact]
    public void CSharpierSwitches_AreDeclaredInPropsAndNotInTargets()
    {
        // The structural counterpart to the evaluation tests above: an assignment moved into
        // .targets still passes a naive "is the property set?" check while landing after
        // CSharpier.MsBuild has already derived its command line.
        var propsText = File.ReadAllText(LinterPackage.PropsPath.FullName);
        var targetsText = File.ReadAllText(LinterPackage.TargetsPath.FullName);

        propsText.Should().Contain("<CSharpier_Check>");
        propsText.Should().Contain("<CSharpier_Bypass>");

        targetsText
            .Should()
            .NotContain(
                "<CSharpier_Check>",
                because: "every package's .props is imported before any package's .targets, so an assignment "
                    + "here lands after CSharpier.MsBuild has already chosen format or check"
            );
        targetsText.Should().NotContain("<CSharpier_Bypass>");
    }

    /// <summary>Which of the two props files the probe project imports first.</summary>
    public enum ImportOrder
    {
        /// <summary>CSharpier.MsBuild's stand-in imports first, as it would alphabetically.</summary>
        StubFirst,

        /// <summary>SharedKernel.Linter's props imports first.</summary>
        LinterFirst,
    }

    private string WriteProbe(ImportOrder order)
    {
        var stub = "CSharpierStub.props";
        var linter = LinterPackage.PropsPath.FullName;

        var imports =
            order == ImportOrder.StubFirst
                ? $"""
                        <Import Project="{stub}" />
                        <Import Project="{linter}" />
                    """
                : $"""
                        <Import Project="{linter}" />
                        <Import Project="{stub}" />
                    """;

        var path = Path.Combine(_workspace.FullName, $"Probe.{order}.csproj");
        File.WriteAllText(
            path,
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
              </PropertyGroup>
            {imports}
            </Project>
            """
        );

        return path;
    }

    private static string Evaluate(
        string projectPath,
        string property,
        string configuration,
        params (string Name, string Value)[] globalProperties
    )
    {
        var arguments = new List<string>
        {
            "msbuild",
            projectPath,
            $"-getProperty:{property}",
            $"-p:Configuration={configuration}",
            "-nologo",
        };
        arguments.AddRange(globalProperties.Select(pair => $"-p:{pair.Name}={pair.Value}"));

        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(projectPath)!,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Could not start 'dotnet msbuild' to evaluate the project."
            );

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(milliseconds: 300_000);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'dotnet msbuild -getProperty:{property}' failed with exit code {process.ExitCode}.{Environment.NewLine}"
                    + $"stdout: {stdout}{Environment.NewLine}stderr: {stderr}"
            );
        }

        // -getProperty with a single property prints the bare value, or nothing when unset.
        return stdout.Trim();
    }
}
