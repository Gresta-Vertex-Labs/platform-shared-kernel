namespace SharedKernel.Presentation.WebApi.Startup;

/// <summary>
/// Registered once by <c>AddSharedKernelWebApi()</c>, which it also marks as done; records whether
/// <c>UseSharedKernelWebApi()</c> added the pipeline.
/// </summary>
internal sealed class WebApiPipelineState
{
    private volatile bool _applied;

    /// <summary>Gets a value indicating whether <c>UseSharedKernelWebApi()</c> added the pipeline.</summary>
    public bool Applied => _applied;

    /// <summary>Records that <c>UseSharedKernelWebApi()</c> added the pipeline.</summary>
    public void MarkApplied() => _applied = true;
}
