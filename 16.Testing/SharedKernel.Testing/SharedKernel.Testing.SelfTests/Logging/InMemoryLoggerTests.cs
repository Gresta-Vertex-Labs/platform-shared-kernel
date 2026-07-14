using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Logging;

/// <summary>
/// Real <c>[LoggerMessage]</c> source-generated call sites used as the representative production
/// shape (root <c>CLAUDE.md</c> WO-041 "Logging Conventions") that <see cref="InMemoryLogger"/> and
/// friends must capture faithfully. Never a hand-written <c>logger.Log(...)</c> call standing in
/// for one.
/// </summary>
internal static partial class TestLogMessages
{
    [LoggerMessage(
        EventId = 90001,
        Level = LogLevel.Information,
        Message = "Order {OrderId} processed with status {Status}")]
    public static partial void OrderProcessed(ILogger logger, int orderId, string status);

    [LoggerMessage(
        EventId = 90002,
        Level = LogLevel.Error,
        Message = "Order {OrderId} failed to process")]
    public static partial void OrderFailed(ILogger logger, Exception exception, int orderId);
}

public sealed class InMemoryLoggerTests
{
    [Fact]
    public void Log_ViaGeneratedLoggerMessage_CapturesEventIdLevelAndMessageExactly()
    {
        var logger = new InMemoryLogger();

        TestLogMessages.OrderProcessed(logger, 42, "Shipped");

        var record = Assert.Single(logger.Records);
        Assert.Equal(new EventId(90001), record.EventId);
        Assert.Equal(LogLevel.Information, record.LogLevel);
        Assert.Equal("Order 42 processed with status Shipped", record.Message);
    }

    [Fact]
    public void Log_ViaGeneratedLoggerMessage_CapturesStructuredPropertiesExactly()
    {
        var logger = new InMemoryLogger();

        TestLogMessages.OrderProcessed(logger, 42, "Shipped");

        var record = Assert.Single(logger.Records);
        Assert.True(record.TryGetProperty("OrderId", out var orderId));
        Assert.Equal(42, orderId);
        Assert.True(record.TryGetProperty("Status", out var status));
        Assert.Equal("Shipped", status);
    }

    [Fact]
    public void Log_ViaGeneratedLoggerMessage_UnknownProperty_TryGetPropertyReturnsFalse()
    {
        var logger = new InMemoryLogger();

        TestLogMessages.OrderProcessed(logger, 42, "Shipped");

        var record = Assert.Single(logger.Records);
        Assert.False(record.TryGetProperty("DoesNotExist", out var value));
        Assert.Null(value);
    }

    [Fact]
    public void Log_WithException_CapturesExceptionOnRecord()
    {
        var logger = new InMemoryLogger();
        var exception = new InvalidOperationException("boom");

        TestLogMessages.OrderFailed(logger, exception, 7);

        var record = Assert.Single(logger.Records);
        Assert.Same(exception, record.Exception);
    }

    [Fact]
    public void Log_WithoutException_RecordExceptionIsNull()
    {
        var logger = new InMemoryLogger();

        TestLogMessages.OrderProcessed(logger, 42, "Shipped");

        var record = Assert.Single(logger.Records);
        Assert.Null(record.Exception);
    }

    [Fact]
    public void BeginScope_NestedScopes_PopulateOuterToInnerOrder()
    {
        var logger = new InMemoryLogger();

        using (logger.BeginScope("Outer"))
        {
            using (logger.BeginScope("Inner"))
            {
                TestLogMessages.OrderProcessed(logger, 1, "A");
            }
        }

        var record = Assert.Single(logger.Records);
        Assert.Equal(["Outer", "Inner"], record.Scopes);
    }

    [Fact]
    public async Task BeginScope_AcrossAwaitBoundary_PreservesScopeStackViaAsyncLocal()
    {
        var logger = new InMemoryLogger();

        using (logger.BeginScope("Outer"))
        {
            await Task.Yield();

            using (logger.BeginScope("Inner"))
            {
                await Task.Yield();
                TestLogMessages.OrderProcessed(logger, 1, "A");
            }
        }

        var record = Assert.Single(logger.Records);
        Assert.Equal(["Outer", "Inner"], record.Scopes);
    }

    [Fact]
    public void BeginScope_DisposedScope_NoLongerAppliesToSubsequentLogs()
    {
        var logger = new InMemoryLogger();

        using (logger.BeginScope("Outer"))
        {
            // Scope disposed at end of this block.
        }

        TestLogMessages.OrderProcessed(logger, 1, "A");

        var record = Assert.Single(logger.Records);
        Assert.Empty(record.Scopes);
    }

    [Fact]
    public void Log_NoScopeActive_ScopesIsEmpty()
    {
        var logger = new InMemoryLogger();

        TestLogMessages.OrderProcessed(logger, 1, "A");

        var record = Assert.Single(logger.Records);
        Assert.Empty(record.Scopes);
    }

    [Fact]
    public void IsEnabled_BelowMinLevel_ReturnsFalse()
    {
        var logger = new InMemoryLogger { MinLevel = LogLevel.Warning };

        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Warning));
        Assert.True(logger.IsEnabled(LogLevel.Error));
    }

    [Fact]
    public void IsEnabled_DefaultMinLevel_IsTraceSoEverythingIsEnabled()
    {
        var logger = new InMemoryLogger();

        Assert.True(logger.IsEnabled(LogLevel.Trace));
        Assert.True(logger.IsEnabled(LogLevel.Critical));
    }

    [Fact]
    public void Clear_EmptiesCapturedRecords()
    {
        var logger = new InMemoryLogger();
        TestLogMessages.OrderProcessed(logger, 1, "A");

        logger.Clear();

        Assert.Empty(logger.Records);
    }
}

public sealed class LoggerAssertionsTests
{
    [Fact]
    public void ShouldHaveLogged_ByEventId_ReturnsMatchingRecord()
    {
        var logger = new InMemoryLogger();
        TestLogMessages.OrderProcessed(logger, 42, "Shipped");

        var record = logger.Records.ShouldHaveLogged(new EventId(90001));

        Assert.Equal("Order 42 processed with status Shipped", record.Message);
    }

    [Fact]
    public void ShouldHaveLogged_ByEventId_NoMatch_ThrowsInvalidOperationException()
    {
        var logger = new InMemoryLogger();
        TestLogMessages.OrderProcessed(logger, 42, "Shipped");

        Assert.Throws<InvalidOperationException>(() => logger.Records.ShouldHaveLogged(new EventId(12345)));
    }

    [Fact]
    public void ShouldHaveLogged_ByEventIdAndLevel_MismatchedLevel_ThrowsInvalidOperationException()
    {
        var logger = new InMemoryLogger();
        TestLogMessages.OrderProcessed(logger, 42, "Shipped");

        Assert.Throws<InvalidOperationException>(
            () => logger.Records.ShouldHaveLogged(new EventId(90001), LogLevel.Error));
    }

    [Fact]
    public void ShouldHaveLoggedWithProperty_FindsRecordByEventIdAndPropertyValue()
    {
        var logger = new InMemoryLogger();
        TestLogMessages.OrderProcessed(logger, 42, "Shipped");

        var record = logger.Records.ShouldHaveLoggedWithProperty(new EventId(90001), "OrderId", 42);

        Assert.Equal("Order 42 processed with status Shipped", record.Message);
    }

    [Fact]
    public void ShouldHaveLoggedWithProperty_MismatchedPropertyValue_ThrowsInvalidOperationException()
    {
        var logger = new InMemoryLogger();
        TestLogMessages.OrderProcessed(logger, 42, "Shipped");

        Assert.Throws<InvalidOperationException>(
            () => logger.Records.ShouldHaveLoggedWithProperty(new EventId(90001), "OrderId", 99));
    }

    [Fact]
    public void ShouldHaveLoggedWithProperty_UnknownPropertyName_ThrowsInvalidOperationException()
    {
        var logger = new InMemoryLogger();
        TestLogMessages.OrderProcessed(logger, 42, "Shipped");

        Assert.Throws<InvalidOperationException>(
            () => logger.Records.ShouldHaveLoggedWithProperty(new EventId(90001), "DoesNotExist", 42));
    }

    [Fact]
    public void ShouldNotHaveLogged_NoMatchingRecord_DoesNotThrow()
    {
        var logger = new InMemoryLogger();
        TestLogMessages.OrderProcessed(logger, 42, "Shipped");

        logger.Records.ShouldNotHaveLogged(new EventId(99999));
    }

    [Fact]
    public void ShouldNotHaveLogged_MatchingRecordExists_ThrowsInvalidOperationException()
    {
        var logger = new InMemoryLogger();
        TestLogMessages.OrderProcessed(logger, 42, "Shipped");

        Assert.Throws<InvalidOperationException>(() => logger.Records.ShouldNotHaveLogged(new EventId(90001)));
    }

    [Fact]
    public void ShouldHaveLoggedCount_MatchesExpectedCount_DoesNotThrow()
    {
        var logger = new InMemoryLogger();
        TestLogMessages.OrderProcessed(logger, 1, "A");
        TestLogMessages.OrderProcessed(logger, 2, "B");

        logger.Records.ShouldHaveLoggedCount(new EventId(90001), 2);
    }

    [Fact]
    public void ShouldHaveLoggedCount_MismatchedCount_ThrowsInvalidOperationException()
    {
        var logger = new InMemoryLogger();
        TestLogMessages.OrderProcessed(logger, 1, "A");

        Assert.Throws<InvalidOperationException>(() => logger.Records.ShouldHaveLoggedCount(new EventId(90001), 2));
    }
}

public sealed class InMemoryLoggerOfTTests
{
    [Fact]
    public void Log_ViaGeneratedLoggerMessage_DelegatesToInnerInMemoryLogger()
    {
        var logger = new InMemoryLogger<InMemoryLoggerOfTTests>();

        TestLogMessages.OrderProcessed(logger, 3, "Packed");

        var record = Assert.Single(logger.Records);
        Assert.Equal(new EventId(90001), record.EventId);
        Assert.Equal("Order 3 processed with status Packed", record.Message);
    }

    [Fact]
    public void MinLevel_ForwardsToInnerLogger()
    {
        var logger = new InMemoryLogger<InMemoryLoggerOfTTests> { MinLevel = LogLevel.Error };

        Assert.False(logger.IsEnabled(LogLevel.Warning));
        Assert.True(logger.IsEnabled(LogLevel.Error));
    }

    [Fact]
    public void BeginScope_NestedScopes_PopulateOuterToInnerOrder()
    {
        var logger = new InMemoryLogger<InMemoryLoggerOfTTests>();

        using (logger.BeginScope("Outer"))
        using (logger.BeginScope("Inner"))
        {
            TestLogMessages.OrderProcessed(logger, 1, "A");
        }

        var record = Assert.Single(logger.Records);
        Assert.Equal(["Outer", "Inner"], record.Scopes);
    }

    [Fact]
    public void Clear_EmptiesCapturedRecords()
    {
        var logger = new InMemoryLogger<InMemoryLoggerOfTTests>();
        TestLogMessages.OrderProcessed(logger, 1, "A");

        logger.Clear();

        Assert.Empty(logger.Records);
    }
}

public sealed class InMemoryLoggerFactoryTests
{
    [Fact]
    public void CreateLogger_And_GetLogger_ReturnSameInstance_ForRepeatedCategoryName()
    {
        var factory = new InMemoryLoggerFactory();

        var viaCreateLogger = factory.CreateLogger("SomeCategory");
        var viaGetLogger = factory.GetLogger("SomeCategory");

        Assert.Same(viaCreateLogger, viaGetLogger);
    }

    [Fact]
    public void CreateLogger_DifferentCategoryNames_ReturnDistinctInstances()
    {
        var factory = new InMemoryLoggerFactory();

        var first = factory.GetLogger("CategoryA");
        var second = factory.GetLogger("CategoryB");

        Assert.NotSame(first, second);
    }

    [Fact]
    public void GetLogger_CapturesLogsIssuedThroughCreateLogger()
    {
        var factory = new InMemoryLoggerFactory();
        var logger = factory.CreateLogger("SomeCategory");

        TestLogMessages.OrderProcessed(logger, 1, "A");

        var records = factory.GetLogger("SomeCategory").Records;
        Assert.Single(records);
    }

    [Fact]
    public void AddProvider_IsDocumentedNoOp()
    {
        var factory = new InMemoryLoggerFactory();

        // Must not throw — documented no-op.
        factory.AddProvider(NullLoggerProvider.Instance);
    }

    [Fact]
    public void Dispose_IsDocumentedNoOp()
    {
        var factory = new InMemoryLoggerFactory();

        // Must not throw — documented no-op.
        factory.Dispose();
    }
}

public sealed class AddInMemoryLoggerFactoryTests
{
    [Fact]
    public void AddInMemoryLoggerFactory_ResolvesInMemoryLoggerFactorySingletonAsILoggerFactory()
    {
        var provider = BuildProvider();

        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();

        Assert.IsType<InMemoryLoggerFactory>(loggerFactory);
    }

    [Fact]
    public void AddInMemoryLoggerFactory_ResolvesSameLoggerFactoryInstance_AcrossResolutions()
    {
        var provider = BuildProvider();

        Assert.Same(provider.GetRequiredService<ILoggerFactory>(), provider.GetRequiredService<ILoggerFactory>());
    }

    [Fact]
    public void AddInMemoryLoggerFactory_ResolvesGenericILoggerOfT_ViaRealBclLoggerAdapter()
    {
        var provider = BuildProvider();

        var typedLogger = provider.GetRequiredService<ILogger<AddInMemoryLoggerFactoryTests>>();

        Assert.IsType<Logger<AddInMemoryLoggerFactoryTests>>(typedLogger);
    }

    [Fact]
    public void AddInMemoryLoggerFactory_LogCallThroughDiResolvedTypedLogger_IsVisibleViaFactoryGetLogger()
    {
        var provider = BuildProvider();
        var loggerFactory = (InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>();
        var typedLogger = provider.GetRequiredService<ILogger<AddInMemoryLoggerFactoryTests>>();

        TestLogMessages.OrderProcessed(typedLogger, 5, "Delivered");

        var registeredCategory = Assert.Single(loggerFactory.Loggers);
        var record = Assert.Single(registeredCategory.Value.Records);
        Assert.Equal(new EventId(90001), record.EventId);
        Assert.Equal("Order 5 processed with status Delivered", record.Message);
    }

    [Fact]
    public void AddInMemoryLoggerFactory_NullServices_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddInMemoryLoggerFactory());

    [Fact]
    public void AddInMemoryLoggerFactory_CalledTwice_DoesNotDuplicateRegistration()
    {
        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        services.AddInMemoryLoggerFactory();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<InMemoryLoggerFactory>(provider.GetRequiredService<ILoggerFactory>());
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        return services.BuildServiceProvider();
    }
}
