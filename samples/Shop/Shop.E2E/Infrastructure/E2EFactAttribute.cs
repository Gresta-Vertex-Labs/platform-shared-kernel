using Xunit;

namespace Shop.E2E.Infrastructure;

/// <summary>A fact that runs only when <c>SHOP_E2E=1</c>: the platform needs Docker, several GB of RAM and minutes to start.</summary>
public sealed class E2EFactAttribute : FactAttribute
{
    /// <summary>Whether this run starts the platform.</summary>
    public static bool Enabled => Environment.GetEnvironmentVariable("SHOP_E2E") == "1";

    public E2EFactAttribute()
    {
        if (!Enabled)
        {
            Skip = "End-to-end flows run locally with SHOP_E2E=1 (see samples/Shop/README.md).";
        }
    }
}
