using System.Text.Json.Serialization;

namespace SharedKernel.Integration.Notifications.Email.SendGrid.Serialization;

/// <summary>
/// STJ source-generated serialization context for the SendGrid Mail Send API request envelope.
/// </summary>
/// <remarks>
/// <see cref="Serialization.SendGridPersonalization.DynamicTemplateData"/> is typed as
/// <see cref="System.Text.Json.JsonElement"/> — a built-in STJ shape requiring no
/// <see cref="JsonSerializableAttribute"/> entry of its own — so the caller's arbitrary
/// <c>TTemplateModel</c> (serialized separately via the runtime, non-source-generated
/// <see cref="System.Text.Json.JsonSerializer"/>, since its concrete type is unknown at this
/// package's compile time) can be embedded without breaking this context's AOT-friendly,
/// source-generated coverage of the surrounding envelope.
/// </remarks>
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(SendGridMailRequest))]
internal sealed partial class SendGridJsonContext : JsonSerializerContext
{
}
