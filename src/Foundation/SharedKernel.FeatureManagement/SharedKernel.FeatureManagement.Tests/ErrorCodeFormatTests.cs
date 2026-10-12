using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace SharedKernel.FeatureManagement.Tests;

/// <summary>Every public error code constant of the package follows the kernel format (C1): lowercase, dot-separated snake_case.</summary>
public sealed class ErrorCodeFormatTests
{
    private static readonly Regex Format = new(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$", RegexOptions.CultureInvariant);

    [Fact]
    public void EveryErrorCodeConstant_FollowsTheKernelFormat()
    {
        List<(string Name, string Value)> codes = Collect(typeof(FeatureManagementErrorCodes)).ToList();

        Assert.NotEmpty(codes);
        foreach ((string name, string value) in codes)
        {
            Assert.True(Format.IsMatch(value), $"{name} = '{value}' does not match the error code format.");
        }
    }

    private static IEnumerable<(string Name, string Value)> Collect(Type type)
    {
        foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
        {
            if (field.IsLiteral && field.FieldType == typeof(string))
            {
                yield return ($"{type.Name}.{field.Name}", (string)field.GetRawConstantValue()!);
            }
        }

        foreach (Type nested in type.GetNestedTypes(BindingFlags.Public))
        {
            foreach ((string name, string value) in Collect(nested))
            {
                yield return ($"{type.Name}.{name}", value);
            }
        }
    }
}
