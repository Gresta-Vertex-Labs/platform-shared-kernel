using System.Diagnostics;
using System.Reflection;
using SharedKernel.Primitives.Enums;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Primitives.Tests;

/// <summary>
/// Covers the <see cref="DebuggerDisplayAttribute"/> rendering on this package's core types.
/// </summary>
/// <remarks>
/// <para>
/// These types are the ones a developer stares at in a watch window most often, and without an
/// explicit display they render as their bare type name — <c>{SharedKernel.Primitives.Results.Result&lt;Order&gt;}</c>
/// — forcing a manual expand to learn anything at all.
/// </para>
/// <para>
/// The real risk being tested is not the text but the EVALUATION. Every one of these types throws
/// from at least one public property depending on its state: <c>Result.Value</c> on a failure,
/// <c>Result.Error</c> on a success, <c>ValidationResult&lt;T&gt;.Value</c> when invalid. A
/// <see cref="DebuggerDisplayAttribute"/> expression that touches one of those renders as an
/// evaluation error instead of the outcome, which is worse than no attribute. Each display member
/// therefore reads backing fields, and each test below asserts it does not throw in the state that
/// would trip it.
/// </para>
/// </remarks>
public sealed class DebuggerDisplayTests
{
    private static string Render(object instance)
    {
        // Walks up BaseType on purpose: a private member is not visible through a derived type's
        // GetProperty even with BindingFlags.NonPublic, and SmartEnum declares its display member
        // on the generic base rather than on each concrete enum.
        for (var type = instance.GetType(); type is not null; type = type.BaseType)
        {
            var member = type.GetProperty(
                "DebuggerDisplay",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
            );

            if (member is not null)
            {
                return (string)member.GetValue(instance)!;
            }
        }

        Assert.Fail(
            $"{instance.GetType().Name} declares no private DebuggerDisplay property anywhere in "
                + "its hierarchy, so its [DebuggerDisplay] attribute cannot resolve."
        );
        return string.Empty;
    }

    private static string RenderStruct<T>(T instance)
        where T : struct
    {
        var member = typeof(T).GetProperty(
            "DebuggerDisplay",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        Assert.NotNull(member);

        return (string)member!.GetValue(instance)!;
    }

    [Theory]
    [InlineData(typeof(Result))]
    [InlineData(typeof(Result<int>))]
    [InlineData(typeof(Error))]
    [InlineData(typeof(ValidationResult))]
    [InlineData(typeof(ValidationResult<int>))]
    public void CoreTypes_DeclareADebuggerDisplay(Type type)
    {
        var attribute = type.GetCustomAttribute<DebuggerDisplayAttribute>(inherit: false);

        Assert.NotNull(attribute);
        Assert.Equal("{DebuggerDisplay,nq}", attribute!.Value);
    }

    [Fact]
    public void SmartEnum_DeclaresADebuggerDisplay()
    {
        // Declared on the open generic base, so a subclass inherits it with no ceremony.
        var attribute = typeof(SmartEnum<DisplayStatus, int>).GetCustomAttribute<DebuggerDisplayAttribute>(
            inherit: false
        );

        Assert.NotNull(attribute);
    }

    [Fact]
    public void GenericResult_Success_ShowsTheValue()
    {
        Assert.Equal("Success: 42", Render(Result<int>.Success(42)));
    }

    [Fact]
    public void GenericResult_Failure_ShowsCodeAndType()
    {
        var result = Result<int>.Failure(Error.NotFound("order.not_found", "Absent."));

        Assert.Equal("Failure: order.not_found (NotFound)", Render(result));
    }

    [Fact]
    public void GenericResult_Failure_DisplayDoesNotThrow_EvenThoughValueWould()
    {
        var result = Result<int>.Failure(Error.NotFound("order.not_found", "Absent."));

        Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Null(Record.Exception(() => Render(result)));
    }

    [Fact]
    public void GenericResult_Success_DisplayDoesNotThrow_EvenThoughErrorWould()
    {
        var result = Result<int>.Success(1);

        Assert.Throws<InvalidOperationException>(() => result.Error);
        Assert.Null(Record.Exception(() => Render(result)));
    }

    [Fact]
    public void Result_Success_ShowsSuccess()
    {
        Assert.Equal("Success", RenderStruct(Result.Success()));
    }

    [Fact]
    public void Result_Failure_ShowsCodeAndType()
    {
        var result = Result.Failure(Error.Conflict("order.duplicate", "Already exists."));

        Assert.Equal("Failure: order.duplicate (Conflict)", RenderStruct(result));
    }

    [Fact]
    public void Result_Default_NamesTheUninitializedState()
    {
        // The payoff case: spotting a default(Result) in a watch window without having to work
        // out why Error throws.
        var rendered = RenderStruct(default(Result));

        Assert.Contains("default(Result)", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Result_Default_DisplayDoesNotThrow_EvenThoughErrorWould()
    {
        Assert.Throws<InvalidOperationException>(() => default(Result).Error);
        Assert.Null(Record.Exception(() => RenderStruct(default(Result))));
    }

    [Fact]
    public void Error_ShowsTypeAndCode()
    {
        Assert.Equal("Validation: field.required", Render(Error.Validation("field.required", "x")));
    }

    [Fact]
    public void Error_None_ShowsNone()
    {
        Assert.Equal("None", Render(Error.None));
    }

    [Fact]
    public void ValidationResult_Valid_ShowsValid()
    {
        Assert.Equal("Valid", Render(ValidationResult.Success()));
    }

    [Fact]
    public void ValidationResult_Invalid_ShowsTheErrorCount()
    {
        var result = ValidationResult.Failure(
            new[] { Error.Validation("a", "1"), Error.Validation("b", "2") }
        );

        Assert.Equal("Invalid: 2 error(s)", Render(result));
    }

    [Fact]
    public void GenericValidationResult_Valid_ShowsTheValue()
    {
        Assert.Equal("Valid: 7", Render(ValidationResult<int>.Success(7)));
    }

    [Fact]
    public void GenericValidationResult_Invalid_DisplayDoesNotThrow_EvenThoughValueWould()
    {
        var result = ValidationResult<int>.Failure(new[] { Error.Validation("a", "1") });

        Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Equal("Invalid: 1 error(s)", Render(result));
    }

    [Fact]
    public void SmartEnum_ShowsNameAndValue()
    {
        // ToString() stays just the Name, since that is what reaches logs and messages.
        Assert.Equal("Shipped", DisplayStatus.Shipped.ToString());
        Assert.Equal("Shipped (2)", Render(DisplayStatus.Shipped));
    }

    private sealed class DisplayStatus : SmartEnum<DisplayStatus, int>
    {
        public static readonly DisplayStatus Pending = new(nameof(Pending), 1);
        public static readonly DisplayStatus Shipped = new(nameof(Shipped), 2);

        private DisplayStatus(string name, int value)
            : base(name, value) { }
    }
}
