using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0037 <see cref="ValueObjectMissingEnsureValidAnalyzer"/>.</summary>
public class SK0037_ValueObjectMissingEnsureValidAnalyzerTests
{
    /// <summary>Stubs mirroring the shape of SharedKernel.Domain's value object bases.</summary>
    private const string Stubs = """
        using System.Collections.Generic;

        namespace SharedKernel.Primitives.Errors { public sealed class Error { } }

        namespace SharedKernel.Domain.ValueObjects
        {
            using SharedKernel.Primitives.Errors;

            public abstract class ValueObject
            {
                protected abstract IEnumerable<object> GetEqualityComponents();
                protected abstract IEnumerable<Error> Validate();
                protected void EnsureValid() { }
            }

            public abstract class SingleValueObject<TValue> : ValueObject
            {
                protected SingleValueObject(TValue value) { Value = value; EnsureValid(); }
                public TValue Value { get; }
                protected sealed override IEnumerable<object> GetEqualityComponents() { yield return Value; }
            }
        }
        """;

    private static Task VerifyAsync(string source)
    {
        var test = new CSharpAnalyzerTest<ValueObjectMissingEnsureValidAnalyzer, DefaultVerifier>
        {
            TestState = { Sources = { Stubs, source } },
        };
        return test.RunAsync();
    }

    private const string Usings = """
        using System.Collections.Generic;
        using SharedKernel.Domain.ValueObjects;
        using SharedKernel.Primitives.Errors;

        """;

    private const string Members = """
            protected override IEnumerable<object> GetEqualityComponents() { yield return 1; }
            protected override IEnumerable<Error> Validate() { if (true) yield return new Error(); }
        """;

    [Fact]
    public Task FirePath_ConstructorWithoutEnsureValid_Reports() => VerifyAsync(Usings + """
        public sealed class {|SK0037:DateRange|} : ValueObject
        {
            public DateRange(int start) { Start = start; }
            public int Start { get; }
        """ + Members + "}");

    [Fact]
    public Task FirePath_NoDeclaredConstructor_Reports() => VerifyAsync(Usings + """
        public sealed class {|SK0037:Tag|} : ValueObject
        {
        """ + Members + "}");

    [Fact]
    public Task FirePath_OneOfTwoConstructorsSkipsEnsureValid_Reports() => VerifyAsync(Usings + """
        public sealed class {|SK0037:Range|} : ValueObject
        {
            public Range(int a) { EnsureValid(); }
            public Range(string b) { }
        """ + Members + "}");

    [Fact]
    public Task FirePath_PrimaryConstructorOnly_Reports() => VerifyAsync(Usings + """
        public sealed class {|SK0037:Code|}(string value) : ValueObject
        {
            public string Value { get; } = value;
        """ + Members + "}");

    [Fact]
    public Task FirePath_ConcreteSubclassOfUnvalidatedIntermediate_Reports() => VerifyAsync(Usings + """
        public abstract class Measurement : ValueObject
        {
            protected Measurement(int value) { }
        """ + Members + """
        }

        public sealed class {|SK0037:Length|} : Measurement
        {
            public Length(int value) : base(value) { }
        }
        """);

    [Fact]
    public Task PassPath_EveryConstructorCallsEnsureValidOrChains_NoDiagnostic() => VerifyAsync(Usings + """
        public sealed class Range : ValueObject
        {
            public Range(int a, int b) { EnsureValid(); }
            public Range(int a) : this(a, a) { }
            public Range(string s) { this.EnsureValid(); }
        """ + Members + "}");

    [Fact]
    public Task PassPath_IntermediateBaseCallsEnsureValid_NoDiagnostic() => VerifyAsync(Usings + """
        public abstract class Measurement : ValueObject
        {
            protected Measurement(int value) { EnsureValid(); }
        """ + Members + """
        }

        public sealed class Length : Measurement
        {
            public Length(int value) : base(value) { }
        }
        """);

    [Fact]
    public Task PassPath_SingleValueObject_NoDiagnostic() => VerifyAsync(Usings + """
        public sealed class Sku : SingleValueObject<string>
        {
            public Sku(string value) : base(value) { }
            protected override IEnumerable<Error> Validate() { if (Value.Length == 0) yield return new Error(); }
        }
        """);

    [Theory]
    [InlineData("protected override IEnumerable<Error> Validate() => [];")]
    [InlineData("protected override IEnumerable<Error> Validate() => null;")]
    [InlineData("protected override IEnumerable<Error> Validate() => System.Array.Empty<Error>();")]
    [InlineData("protected override IEnumerable<Error> Validate() => System.Linq.Enumerable.Empty<Error>();")]
    [InlineData("protected override IEnumerable<Error> Validate() { yield break; }")]
    [InlineData("protected override IEnumerable<Error> Validate() { return null; }")]
    public Task PassPath_ValidateDeclaresNoRules_NoDiagnostic(string validate) => VerifyAsync(Usings + $$"""
        public sealed class Money : ValueObject
        {
            public Money(decimal amount) { }
            protected override IEnumerable<object> GetEqualityComponents() { yield return 1; }
            {{validate}}
        }
        """);

    [Fact]
    public Task PassPath_AbstractValueObject_NoDiagnostic() => VerifyAsync(Usings + """
        public abstract class Measurement : ValueObject
        {
            protected Measurement(int value) { }
        """ + Members + "}");

    [Fact]
    public Task PassPath_UnrelatedClassNamedValueObject_NoDiagnostic() => VerifyAsync("""
        namespace Elsewhere
        {
            public abstract class ValueObject { }
            public sealed class Thing : ValueObject { public Thing() { } }
        }
        """);
}
