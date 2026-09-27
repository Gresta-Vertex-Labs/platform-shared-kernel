using System.Security.Cryptography;
using System.Text;
using SharedKernel.Security.ApiKey.Validation;

namespace InventoryApi;

/// <summary>
/// Accepts the one API key in <c>Inventory:ApiKey</c>, compared in fixed time, as the client <c>checkout</c>. A real
/// service keeps hashed keys in a store (<c>AddManagedApiKeyAuthentication&lt;TStore&gt;</c>).
/// </summary>
public sealed class ConfiguredApiKeyValidator(IConfiguration configuration) : IApiKeyValidator
{
    public const string SettingName = "Inventory:ApiKey";

    public ValueTask<ApiKeyValidationResult> ValidateAsync(string presentedKey, CancellationToken cancellationToken)
    {
        string? expected = configuration[SettingName];
        bool valid = !string.IsNullOrEmpty(expected)
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presentedKey), Encoding.UTF8.GetBytes(expected));

        return ValueTask.FromResult(valid ? ApiKeyValidationResult.Success("checkout") : ApiKeyValidationResult.Failure());
    }
}
