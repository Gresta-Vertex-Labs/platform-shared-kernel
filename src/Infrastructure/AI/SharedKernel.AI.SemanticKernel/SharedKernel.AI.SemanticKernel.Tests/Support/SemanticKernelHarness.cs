using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Constants;
using SharedKernel.AI.SemanticKernel.Extensions;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.AI.SemanticKernel.Tests.Support;

/// <summary>
/// A scripted <see cref="HttpMessageHandler"/> behind the named client of the real registration: the real connector,
/// the real SDK pipeline, no network. It counts every request that reaches it.
/// </summary>
internal sealed class ScriptedHandler(Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        respond(Interlocked.Increment(ref _count), request, cancellationToken);
}

/// <summary>The real registration over a <see cref="ScriptedHandler"/>, with the recorders attached.</summary>
internal sealed class SemanticKernelHarness : IDisposable
{
    private const string WireModel = "gpt-test";
    public const string HostSentinel = "host-sentinel.internal";
    public const string BodySentinel = "body-sentinel";
    public const string KeySentinel = "api-key-sentinel";

    private readonly ServiceProvider _provider;

    private SemanticKernelHarness(ScriptedHandler handler, ServiceProvider provider, string chatModel, string embeddingModel)
    {
        Handler = handler;
        ChatModel = chatModel;
        EmbeddingModel = embeddingModel;
        _provider = provider;
        Activities = ActivityRecorder.StartRecording(IntelligenceWellKnown.ActivitySourceName);
        Metrics = MetricRecorder.StartRecording(IntelligenceWellKnown.MeterName);
    }

    /// <summary>Gets this harness's chat model id; unique, so the process-wide recorders can be filtered to it.</summary>
    public string ChatModel { get; }

    /// <summary>Gets this harness's embedding model id; unique for the same reason.</summary>
    public string EmbeddingModel { get; }

    public ScriptedHandler Handler { get; }

    public ActivityRecorder Activities { get; }

    public MetricRecorder Metrics { get; }

    public ISemanticKernel Kernel => _provider.GetRequiredService<ISemanticKernel>();

    public IEmbeddingGenerator Embeddings => _provider.GetRequiredService<IEmbeddingGenerator>();

    public InMemoryLoggerFactory Logs => (InMemoryLoggerFactory)_provider.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>();

    /// <summary>Gets every log record this package wrote.</summary>
    public IReadOnlyList<LogRecord> PackageLogs =>
        [.. Logs.Loggers.Where(l => l.Key.StartsWith("SharedKernel.AI.SemanticKernel", StringComparison.Ordinal)).SelectMany(l => l.Value.Records)];

    /// <summary>Gets the recorded spans of this harness (its models, or the bounded other-model value).</summary>
    public IReadOnlyList<Activity> Spans => [.. Activities.RecordedActivities.Where(a => IsMine(a.GetTagItem(IntelligenceTagKeys.Model) as string))];

    private bool IsMine(string? model) => model == ChatModel || model == EmbeddingModel;

    /// <summary>
    /// Gets the spans tagged with the bounded other-model value. They cannot be tied to a harness (the value is shared by
    /// design), so only the single test that sends an unlisted model reads them.
    /// </summary>
    public IReadOnlyList<Activity> OtherModelSpans => [.. Activities.RecordedActivities.Where(a => a.GetTagItem(IntelligenceTagKeys.Model) as string == IntelligenceWellKnown.OtherModel)];

    /// <summary>Gets the measurements of <paramref name="instrument"/> tagged with the bounded other-model value (see <see cref="OtherModelSpans"/>).</summary>
    public IReadOnlyList<RecordedMeasurement> OtherModelMeasurements(string instrument) =>
        [.. Metrics.MeasurementsOf(instrument).Where(m => m.TryGetTag(IntelligenceTagKeys.Model, out var model) && (string?)model == IntelligenceWellKnown.OtherModel)];

    public IReadOnlyList<RecordedMeasurement> Measurements(string instrument) =>
        [.. Metrics.MeasurementsOf(instrument).Where(m => m.TryGetTag(IntelligenceTagKeys.Model, out var model) && IsMine((string?)model))];

    public static SemanticKernelHarness Create(
        Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond,
        Action<SemanticKernelBuilder>? configure = null,
        int httpTimeoutSeconds = 60)
    {
        var chatModel = $"gpt-test-{Guid.NewGuid():N}";
        var embeddingModel = $"emb-test-{Guid.NewGuid():N}";
        var handler = new ScriptedHandler(respond);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Intelligence:SemanticKernel:ApiKey"] = KeySentinel,
            ["Intelligence:SemanticKernel:ChatModelId"] = chatModel,
            ["Intelligence:SemanticKernel:EmbeddingModelId"] = embeddingModel,
            ["Intelligence:SemanticKernel:EmbeddingDimension"] = "3",
            ["Intelligence:SemanticKernel:Endpoint"] = $"https://{HostSentinel}/v1",
            ["Intelligence:SemanticKernel:HttpTimeoutSeconds"] = httpTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }).Build();

        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        var builder = services.AddSharedKernelSemanticKernel(configuration);
        configure?.Invoke(builder);
        builder.Build();
        services.AddHttpClient("SharedKernel.AI.SemanticKernel").ConfigurePrimaryHttpMessageHandler(() => handler);

        return new SemanticKernelHarness(handler, services.BuildServiceProvider(), chatModel, embeddingModel);
    }

    public static Task<HttpResponseMessage> Status(int status) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
    {
        Content = new StringContent($"{{\"error\":{{\"message\":\"{BodySentinel} {HostSentinel}\"}}}}", Encoding.UTF8, "application/json"),
    });

    public static Task<HttpResponseMessage> Chat(string content, int promptTokens, int completionTokens) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$$"""
                {"id":"c1","object":"chat.completion","created":1,"model":"{{{WireModel}}}","choices":[{"index":0,"message":{"role":"assistant","content":"{{{content}}}"},"finish_reason":"stop"}],"usage":{"prompt_tokens":{{{promptTokens}}},"completion_tokens":{{{completionTokens}}},"total_tokens":{{{promptTokens + completionTokens}}}}}
                """,
                Encoding.UTF8,
                "application/json"),
        });

    public static Task<HttpResponseMessage> Embedding(int promptTokens, params float[][] vectors)
    {
        var data = string.Join(
            ",",
            vectors.Select((v, i) => $$"""{ "object": "embedding", "index": {{i}}, "embedding": "{{ToBase64(v)}}" }"""));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{ "object": "list", "data": [ {{data}} ], "model": "{{WireModel}}", "usage": { "prompt_tokens": {{promptTokens}}, "total_tokens": {{promptTokens}} } }""",
                Encoding.UTF8,
                "application/json"),
        });
    }

    public static Task<HttpResponseMessage> ChatStream(params string[] deltas)
    {
        var body = new StringBuilder();
        foreach (var delta in deltas)
        {
            body.Append($$"""data: {"id":"c1","object":"chat.completion.chunk","created":1,"model":"{{WireModel}}","choices":[{"index":0,"delta":{"content":"{{delta}}"},"finish_reason":null}]}""").Append("\n\n");
        }

        body.Append($$"""data: {"id":"c1","object":"chat.completion.chunk","created":1,"model":"{{WireModel}}","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}""").Append("\n\n");
        body.Append("data: {\"id\":\"c1\",\"object\":\"chat.completion.chunk\",\"created\":1,\"model\":\"" + WireModel + "\",\"choices\":[],\"usage\":{\"prompt_tokens\":5,\"completion_tokens\":3,\"total_tokens\":8}}").Append("\n\n");
        body.Append("data: [DONE]\n\n");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body.ToString(), Encoding.UTF8, "text/event-stream"),
        });
    }

    public CompletionRequest Request(string? modelId = null) => new()
    {
        Messages = [new ChatMessage { Role = ChatRole.User, Content = "prompt-text-sentinel" }],
        ModelId = modelId,
    };

    public void Dispose()
    {
        Activities.Dispose();
        Metrics.Dispose();
        _provider.Dispose();
    }

    private static string ToBase64(float[] vector)
    {
        var bytes = new byte[vector.Length * 4];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return Convert.ToBase64String(bytes);
    }
}
