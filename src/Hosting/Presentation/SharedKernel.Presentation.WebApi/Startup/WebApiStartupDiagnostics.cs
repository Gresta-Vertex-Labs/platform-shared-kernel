using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Presentation.WebApi.Diagnostics;

namespace SharedKernel.Presentation.WebApi.Startup;

/// <summary>
/// Warns, when the host starts, about a setup that works but not the way the service expects:
/// <c>AddSharedKernelWebApi()</c> without <c>UseSharedKernelWebApi()</c>, and exception details exposed outside
/// Development.
/// </summary>
internal sealed class WebApiStartupDiagnostics : IHostedLifecycleService
{
    private readonly WebApiPipelineState _pipeline;
    private readonly IOptions<SharedKernelWebApiOptions> _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<WebApiStartupDiagnostics> _logger;

    public WebApiStartupDiagnostics(
        WebApiPipelineState pipeline,
        IOptions<SharedKernelWebApiOptions> options,
        IHostEnvironment environment,
        ILogger<WebApiStartupDiagnostics> logger)
    {
        _pipeline = pipeline;
        _options = options;
        _environment = environment;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        if (_options.Value.Problems.IncludeExceptionDetails == true && !_environment.IsDevelopment())
        {
            WebApiLog.ExceptionDetailsOutsideDevelopment(_logger, _environment.EnvironmentName);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    /// <remarks>
    /// Checked once every hosted service has started, because a <c>Startup</c> class builds the pipeline while the
    /// web host starts.
    /// </remarks>
    public Task StartedAsync(CancellationToken cancellationToken)
    {
        if (!_pipeline.Applied)
        {
            WebApiLog.PipelineNotApplied(_logger);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
