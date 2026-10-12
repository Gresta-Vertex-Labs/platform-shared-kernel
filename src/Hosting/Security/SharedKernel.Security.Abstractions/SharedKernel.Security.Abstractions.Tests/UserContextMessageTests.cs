using SharedKernel.Execution.Context;
using Xunit;

namespace SharedKernel.Security.Abstractions.Tests;

// A thrown message is a sentence ending in a period that quotes its values and names the remedy.
public sealed class UserContextMessageTests
{
    [Theory]
    [InlineData(ActorKind.Anonymous)]
    [InlineData(ActorKind.System)]
    public void Constructor_ActorKindWithoutSubject_ThrowsASentenceNamingTheRemedy(ActorKind kind)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => new UserContext(kind, "subject"));

        Assert.Contains(
            "Only the actor kinds 'User' and 'Service' carry a subject. Use AnonymousUserContext or SystemUserContext for the other kinds.",
            exception.Message,
            StringComparison.Ordinal);
    }
}
