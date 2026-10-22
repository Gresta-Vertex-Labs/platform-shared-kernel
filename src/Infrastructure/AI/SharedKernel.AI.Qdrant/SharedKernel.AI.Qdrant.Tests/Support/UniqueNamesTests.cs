using FluentAssertions;
using Xunit;

namespace SharedKernel.AI.Qdrant.Tests.Support;

public sealed class UniqueNamesTests
{
    [Fact]
    public void Names_AreUnique_AndNeverContainADigit_SoNoNumericLeakTokenCanMatchThem()
    {
        var names = Enumerable.Range(0, 5_000).Select(_ => UniqueNames.Of("obs")).ToList();

        names.Should().OnlyHaveUniqueItems();
        names.Should().OnlyContain(n => n.StartsWith("obs-", StringComparison.Ordinal) && n.All(c => !char.IsDigit(c)));
        names.Should().NotContain(n => n.Contains("6334", StringComparison.Ordinal));
    }
}
