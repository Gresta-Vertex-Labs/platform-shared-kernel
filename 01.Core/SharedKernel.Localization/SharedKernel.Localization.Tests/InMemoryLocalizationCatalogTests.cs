using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SharedKernel.Localization.Tests;

public sealed class InMemoryLocalizationCatalogTests
{
    private static readonly CultureInfo TurkishTurkey = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr");
    private static readonly CultureInfo EnglishUs = CultureInfo.GetCultureInfo("en-US");

    [Fact]
    public void TryGetString_RegisteredCodeAndCulture_ReturnsTrueAndTranslation()
    {
        var catalog = new InMemoryLocalizationCatalog()
            .AddTranslation("user.not_found", EnglishUs, "User not found.");

        bool found = catalog.TryGetString("user.not_found", EnglishUs, out string? value);

        Assert.True(found);
        Assert.Equal("User not found.", value);
    }

    [Fact]
    public void TryGetString_UnregisteredCode_ReturnsFalseAndNull_NeverThrows()
    {
        var catalog = new InMemoryLocalizationCatalog();

        bool found = catalog.TryGetString("nothing.registered", EnglishUs, out string? value);

        Assert.False(found);
        Assert.Null(value);
    }

    [Fact]
    public void TryGetString_UnregisteredCode_NeverReturnsEmptyString()
    {
        var catalog = new InMemoryLocalizationCatalog();

        catalog.TryGetString("nothing.registered", EnglishUs, out string? value);

        Assert.NotEqual(string.Empty, value);
    }

    [Fact]
    public void AddTranslation_ReturnsSameInstance_ForChaining()
    {
        var catalog = new InMemoryLocalizationCatalog();

        InMemoryLocalizationCatalog result = catalog.AddTranslation("code.one", EnglishUs, "One");

        Assert.Same(catalog, result);
    }

    [Fact]
    public void AddTranslation_ChainedCalls_AllResolve()
    {
        var catalog = new InMemoryLocalizationCatalog()
            .AddTranslation("code.one", EnglishUs, "One")
            .AddTranslation("code.two", EnglishUs, "Two")
            .AddTranslation("code.one", Turkish, "Bir");

        Assert.True(catalog.TryGetString("code.one", EnglishUs, out string? one));
        Assert.Equal("One", one);
        Assert.True(catalog.TryGetString("code.two", EnglishUs, out string? two));
        Assert.Equal("Two", two);
        Assert.True(catalog.TryGetString("code.one", Turkish, out string? bir));
        Assert.Equal("Bir", bir);
    }

    [Fact]
    public void AddTranslation_SameCodeAndCultureAgain_OverwritesPreviousValue()
    {
        var catalog = new InMemoryLocalizationCatalog()
            .AddTranslation("code.one", EnglishUs, "First")
            .AddTranslation("code.one", EnglishUs, "Second");

        catalog.TryGetString("code.one", EnglishUs, out string? value);

        Assert.Equal("Second", value);
    }

    // ── Culture fallback — the sharpest edge the design left unspecified. Decision: a lookup for a
    // specific culture (tr-TR) falls back through each parent culture (tr) and finally
    // CultureInfo.InvariantCulture, mirroring standard .NET ResourceManager/IStringLocalizer
    // fallback semantics. Documented in this type's own XML docs.

    [Fact]
    public void TryGetString_SpecificCultureNotRegistered_FallsBackToParentCulture()
    {
        var catalog = new InMemoryLocalizationCatalog()
            .AddTranslation("greeting", Turkish, "Merhaba");

        bool found = catalog.TryGetString("greeting", TurkishTurkey, out string? value);

        Assert.True(found);
        Assert.Equal("Merhaba", value);
    }

    [Fact]
    public void TryGetString_NeitherSpecificNorParentRegistered_FallsBackToInvariantCulture()
    {
        var catalog = new InMemoryLocalizationCatalog()
            .AddTranslation("greeting", CultureInfo.InvariantCulture, "Hello");

        bool found = catalog.TryGetString("greeting", TurkishTurkey, out string? value);

        Assert.True(found);
        Assert.Equal("Hello", value);
    }

    [Fact]
    public void TryGetString_MoreSpecificCultureRegistered_PreferredOverParentOrInvariant()
    {
        var catalog = new InMemoryLocalizationCatalog()
            .AddTranslation("greeting", CultureInfo.InvariantCulture, "Hello")
            .AddTranslation("greeting", Turkish, "Merhaba")
            .AddTranslation("greeting", TurkishTurkey, "Merhaba (Turkiye)");

        bool found = catalog.TryGetString("greeting", TurkishTurkey, out string? value);

        Assert.True(found);
        Assert.Equal("Merhaba (Turkiye)", value);
    }

    [Fact]
    public void TryGetString_NoFallbackRegisteredAtAll_ReturnsFalse()
    {
        var catalog = new InMemoryLocalizationCatalog();

        bool found = catalog.TryGetString("greeting", TurkishTurkey, out string? value);

        Assert.False(found);
        Assert.Null(value);
    }

    [Fact]
    public void TryGetString_InvariantCultureRequestedDirectly_DoesNotLoopForever()
    {
        var catalog = new InMemoryLocalizationCatalog()
            .AddTranslation("greeting", CultureInfo.InvariantCulture, "Hello");

        bool found = catalog.TryGetString("greeting", CultureInfo.InvariantCulture, out string? value);

        Assert.True(found);
        Assert.Equal("Hello", value);
    }

    [Fact]
    public void TryGetString_CodeLookupIsCaseSensitive()
    {
        var catalog = new InMemoryLocalizationCatalog()
            .AddTranslation("user.not_found", EnglishUs, "User not found.");

        bool found = catalog.TryGetString("USER.NOT_FOUND", EnglishUs, out string? value);

        Assert.False(found);
        Assert.Null(value);
    }

    [Fact]
    public void TryGetString_NullCode_ThrowsArgumentNullException()
    {
        var catalog = new InMemoryLocalizationCatalog();

        Assert.Throws<ArgumentNullException>(() => catalog.TryGetString(null!, EnglishUs, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryGetString_EmptyOrWhitespaceCode_ThrowsArgumentException(string code)
    {
        var catalog = new InMemoryLocalizationCatalog();

        Assert.Throws<ArgumentException>(() => catalog.TryGetString(code, EnglishUs, out _));
    }

    [Fact]
    public void TryGetString_NullCulture_ThrowsArgumentNullException()
    {
        var catalog = new InMemoryLocalizationCatalog();

        Assert.Throws<ArgumentNullException>(() => catalog.TryGetString("code", null!, out _));
    }

    [Fact]
    public void AddTranslation_NullValue_ThrowsArgumentNullException()
    {
        var catalog = new InMemoryLocalizationCatalog();

        Assert.Throws<ArgumentNullException>(() => catalog.AddTranslation("code", EnglishUs, null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddTranslation_EmptyOrWhitespaceValue_ThrowsArgumentException(string value)
    {
        var catalog = new InMemoryLocalizationCatalog();

        Assert.Throws<ArgumentException>(() => catalog.AddTranslation("code", EnglishUs, value));
    }

    [Fact]
    public void AddTranslation_NullCulture_ThrowsArgumentNullException()
    {
        var catalog = new InMemoryLocalizationCatalog();

        Assert.Throws<ArgumentNullException>(() => catalog.AddTranslation("code", null!, "value"));
    }

    // ── Seal()/IsSealed (T-84, SK.01.P516) ───────────────────────────────────────────────────
    // The seed-once-then-immutable lifecycle: Seal() freezes the catalog so AddTranslation always
    // throws afterward (never silently corrupting the backing dictionary under concurrent access),
    // while TryGetString remains safe to call concurrently with zero lock overhead.

    [Fact]
    public void IsSealed_NewCatalog_IsFalse()
    {
        var catalog = new InMemoryLocalizationCatalog();

        Assert.False(catalog.IsSealed);
    }

    [Fact]
    public void Seal_SetsIsSealedTrue()
    {
        var catalog = new InMemoryLocalizationCatalog();

        catalog.Seal();

        Assert.True(catalog.IsSealed);
    }

    [Fact]
    public void Seal_CalledTwice_IsIdempotent_DoesNotThrow()
    {
        var catalog = new InMemoryLocalizationCatalog();

        catalog.Seal();
        Exception? exception = Record.Exception(catalog.Seal);

        Assert.Null(exception);
        Assert.True(catalog.IsSealed);
    }

    [Fact]
    public void AddTranslation_AfterSeal_ThrowsInvalidOperationException()
    {
        var catalog = new InMemoryLocalizationCatalog();
        catalog.Seal();

        Assert.Throws<InvalidOperationException>(
            () => catalog.AddTranslation("greeting", EnglishUs, "Hello"));
    }

    [Fact]
    public void AddTranslation_AfterSeal_ThrowingCall_NeverCorruptsDictionaryState()
    {
        var catalog = new InMemoryLocalizationCatalog()
            .AddTranslation("greeting", EnglishUs, "Hello");
        catalog.Seal();

        Assert.Throws<InvalidOperationException>(
            () => catalog.AddTranslation("greeting", EnglishUs, "Overwritten"));

        bool found = catalog.TryGetString("greeting", EnglishUs, out string? value);
        Assert.True(found);
        Assert.Equal("Hello", value);
    }

    [Fact]
    public void AddTranslation_AfterSeal_RejectedCall_DoesNotAddNewEntry()
    {
        var catalog = new InMemoryLocalizationCatalog();
        catalog.Seal();

        Assert.Throws<InvalidOperationException>(
            () => catalog.AddTranslation("never.added", EnglishUs, "Should not appear"));

        bool found = catalog.TryGetString("never.added", EnglishUs, out string? value);
        Assert.False(found);
        Assert.Null(value);
    }

    [Fact]
    public void AddTranslation_BeforeSeal_StillSucceeds()
    {
        var catalog = new InMemoryLocalizationCatalog();

        catalog.AddTranslation("greeting", EnglishUs, "Hello");
        catalog.Seal();

        bool found = catalog.TryGetString("greeting", EnglishUs, out string? value);
        Assert.True(found);
        Assert.Equal("Hello", value);
    }

    [Fact]
    public void TryGetString_ConcurrentReadsAgainstSealedCatalog_NeverThrowOrCorruptResults()
    {
        var catalog = new InMemoryLocalizationCatalog()
            .AddTranslation("greeting", EnglishUs, "Hello")
            .AddTranslation("greeting", Turkish, "Merhaba")
            .AddTranslation("farewell", EnglishUs, "Goodbye");
        catalog.Seal();

        const int threadCount = 16;
        const int iterationsPerThread = 2_000;
        var barrier = new Barrier(threadCount);
        var exceptions = new ConcurrentBag<Exception>();

        var threads = Enumerable.Range(0, threadCount)
            .Select(threadIndex => new Thread(() =>
            {
                barrier.SignalAndWait();
                try
                {
                    for (int i = 0; i < iterationsPerThread; i++)
                    {
                        (string code, CultureInfo culture, string? expected) = (threadIndex % 3) switch
                        {
                            0 => ("greeting", EnglishUs, "Hello"),
                            1 => ("greeting", TurkishTurkey, "Merhaba"),
                            _ => ("farewell", EnglishUs, "Goodbye"),
                        };

                        bool found = catalog.TryGetString(code, culture, out string? value);

                        if (!found || value != expected)
                        {
                            throw new InvalidOperationException(
                                $"Corrupted read: code={code}, culture={culture.Name}, "
                                    + $"found={found}, value={value}, expected={expected}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }))
            .ToArray();

        foreach (Thread thread in threads)
        {
            thread.Start();
        }

        foreach (Thread thread in threads)
        {
            thread.Join();
        }

        Assert.Empty(exceptions);
    }

    [Fact]
    public void AddInMemoryLocalizationCatalog_ResolvedCatalog_IsAlreadySealed()
    {
        var services = new ServiceCollection();
        services.AddInMemoryLocalizationCatalog(catalog =>
            catalog.AddTranslation("greeting", EnglishUs, "Hello"));

        using ServiceProvider provider = services.BuildServiceProvider();
        ILocalizationCatalog catalog = provider.GetRequiredService<ILocalizationCatalog>();
        var concrete = Assert.IsType<InMemoryLocalizationCatalog>(catalog);

        Assert.True(concrete.IsSealed);
        Assert.Throws<InvalidOperationException>(
            () => concrete.AddTranslation("late", EnglishUs, "Too late"));
    }
}
