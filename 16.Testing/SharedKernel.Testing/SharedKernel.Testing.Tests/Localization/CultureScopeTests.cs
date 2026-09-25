using System.Globalization;
using SharedKernel.Testing.Localization;

namespace SharedKernel.Testing.SelfTests.Localization;

/// <summary>
/// Proves <see cref="CultureScope"/> — a standalone helper with no owning consuming-domain
/// interface, proven in <c>SharedKernel.Testing.SelfTests</c> per this domain's routing
/// convention.
/// </summary>
public sealed class CultureScopeTests
{
    [Fact]
    public void Constructor_SetsCurrentCultureAndUiCulture()
    {
        var original = CultureInfo.CurrentCulture;
        var target = CultureInfo.GetCultureInfo("tr-TR");

        using (new CultureScope(target))
        {
            Assert.Equal(target, CultureInfo.CurrentCulture);
            Assert.Equal(target, CultureInfo.CurrentUICulture);
        }

        Assert.Equal(original, CultureInfo.CurrentCulture);
    }

    [Fact]
    public void Dispose_RestoresOriginalCulture()
    {
        var original = CultureInfo.CurrentCulture;
        var originalUi = CultureInfo.CurrentUICulture;

        var scope = new CultureScope("fr-FR");
        scope.Dispose();

        Assert.Equal(original, CultureInfo.CurrentCulture);
        Assert.Equal(originalUi, CultureInfo.CurrentUICulture);
    }

    [Fact]
    public void Dispose_CalledTwice_IsIdempotent()
    {
        var scope = new CultureScope("de-DE");
        scope.Dispose();
        scope.Dispose(); // must not throw or double-restore incorrectly
    }

    [Fact]
    public void Dispose_RestoresCulture_EvenWhenBlockThrows()
    {
        var original = CultureInfo.CurrentCulture;

        try
        {
            using var scope = new CultureScope("ja-JP");
            throw new InvalidOperationException("boom");
        }
        catch (InvalidOperationException)
        {
            // expected
        }

        Assert.Equal(original, CultureInfo.CurrentCulture);
    }

    [Fact]
    public void StringConstructor_ResolvesCultureByName()
    {
        using var scope = new CultureScope("es-ES");

        Assert.Equal("es-ES", CultureInfo.CurrentCulture.Name);
    }

    [Fact]
    public void CultureInfoConstructor_NullCulture_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new CultureScope((CultureInfo)null!));

    [Fact]
    public void StringConstructor_NullCultureName_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new CultureScope((string)null!));

    [Fact]
    public void NestedScopes_RestoreInReverseOrder()
    {
        var original = CultureInfo.CurrentCulture;

        using (new CultureScope("tr-TR"))
        {
            using (new CultureScope("de-DE"))
            {
                Assert.Equal("de-DE", CultureInfo.CurrentCulture.Name);
            }

            Assert.Equal("tr-TR", CultureInfo.CurrentCulture.Name);
        }

        Assert.Equal(original, CultureInfo.CurrentCulture);
    }
}
