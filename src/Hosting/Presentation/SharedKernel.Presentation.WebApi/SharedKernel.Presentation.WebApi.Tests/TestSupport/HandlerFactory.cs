using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Presentation.WebApi.ExceptionHandling;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Presentation.WebApi.Tests.TestSupport;

/// <summary>Builds the platform's exception handler and a request for it without a host, to drive a case a client cannot (a request that vanished).</summary>
internal static class HandlerFactory
{
    public static SharedKernelExceptionHandler Create(string environment = "Production") =>
        new(
            new InMemoryLogger<SharedKernelExceptionHandler>(),
            new TestEnvironment(environment),
            Options.Create(new SharedKernelWebApiOptions()));

    public static DefaultHttpContext CreateContext() => new()
    {
        RequestServices = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider(),
        Response = { Body = new MemoryStream() },
    };

    private sealed class TestEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
