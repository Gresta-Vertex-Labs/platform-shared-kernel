using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>
/// Tests for the fifth SK0022 shape: a raw string literal as a metric or span tag key
/// (<c>TagList.Add</c>, <c>new KeyValuePair&lt;string, object?&gt;(key, …)</c>, a <c>TagList</c> initializer and the tag collection
/// passed to <c>ActivitySource.StartActivity</c>). The BCL types are real; nothing is stubbed.
/// </summary>
public class SK0022_MetricTagKeyShapeTests
{
    private static Task VerifyAsync(string source)
    {
        var test = new CSharpAnalyzerTest<CrossCuttingMagicStringLiteralAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestState = { Sources = { source } },
        };
        return test.RunAsync();
    }

    private const string Usings = """
        using System.Collections.Generic;
        using System.Diagnostics;
        using System.Diagnostics.Metrics;

        """;

    [Fact]
    public Task FirePath_TagListAdd_Reports() => VerifyAsync(Usings + """
        public sealed class Instruments
        {
            public void Record(Counter<long> counter)
            {
                var tags = new TagList();
                tags.Add({|SK0022:"outcome"|}, "success");
                counter.Add(1, tags);
            }
        }
        """);

    [Fact]
    public Task FirePath_KeyValuePairConstruction_Reports() => VerifyAsync(Usings + """
        public sealed class Instruments
        {
            public void Record(Counter<long> counter, Histogram<double> histogram)
            {
                counter.Add(1, new KeyValuePair<string, object?>({|SK0022:"outcome"|}, "success"));
                KeyValuePair<string, object?> target = new({|SK0022:"operation"|}, "get");
                histogram.Record(0.5, target, new KeyValuePair<string, object?>({|SK0022:"provider"|}, "s3"));
            }
        }
        """);

    [Fact]
    public Task FirePath_TagListCollectionInitializer_Reports() => VerifyAsync(Usings + """
        public sealed class Instruments
        {
            public void Record(Counter<long> counter)
            {
                counter.Add(1, new TagList { { {|SK0022:"outcome"|}, "success" }, { {|SK0022:"operation"|}, "get" } });
            }
        }
        """);

    [Fact]
    public Task FirePath_TagCollectionPassedToStartActivity_Reports() => VerifyAsync(Usings + """
        public sealed class Tracing
        {
            private static readonly ActivitySource Source = new("Fixture.Tracing");

            public void Start(ActivityContext parent)
            {
                using var a = Source.StartActivity("op", ActivityKind.Client, parent, new List<KeyValuePair<string, object?>>
                {
                    new({|SK0022:"store"|}, "main"),
                });
                using var b = Source.StartActivity("op", ActivityKind.Client, parent, new Dictionary<string, object?>
                {
                    { {|SK0022:"provider"|}, "s3" },
                });
                using var c = Source.StartActivity("op", ActivityKind.Client, parent, new Dictionary<string, object?>
                {
                    [{|SK0022:"operation"|}] = "get",
                });
            }
        }
        """);

    [Fact]
    public Task PassPath_NamedConstantKeys_Clean() => VerifyAsync(Usings + """
        public static class TagKeys
        {
            public const string Outcome = "outcome";
            public static readonly string Operation = "operation";
        }

        public sealed class Instruments
        {
            private static readonly ActivitySource Source = new("Fixture.Tracing");

            public void Record(Counter<long> counter, ActivityContext parent)
            {
                var tags = new TagList { { TagKeys.Outcome, "success" } };
                tags.Add(TagKeys.Operation, "get");
                counter.Add(1, tags);
                counter.Add(1, new KeyValuePair<string, object?>(TagKeys.Outcome, "x"));
                using var a = Source.StartActivity("op", ActivityKind.Client, parent, new Dictionary<string, object?> { [TagKeys.Outcome] = "x" });
            }
        }
        """);

    [Fact]
    public Task PassPath_UnrelatedTypesWithTheSameShapes_Clean() => VerifyAsync(Usings + """
        public sealed class Bag
        {
            public void Add(string key, object? value) { }
        }

        public sealed class Other
        {
            public void Run()
            {
                new Bag().Add("outcome", 1);
                var pairs = new KeyValuePair<string, string>("a", "b");
                var counts = new Dictionary<string, object?> { ["a"] = 1 };
                var more = new Dictionary<string, object?> { { "b", 2 } };
                var list = new List<KeyValuePair<string, object?>>();
            }
        }
        """);
}
