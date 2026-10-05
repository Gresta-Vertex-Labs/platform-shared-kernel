using SharedKernel.ServiceDefaults.Probes;

namespace SharedKernel.ServiceDefaults.Tests.Probes;

public sealed class StartupGateTests
{
    [Fact]
    public void IsReady_DefaultsToFalse()
    {
        var gate = new StartupGate();

        Assert.False(gate.IsReady);
    }

    [Fact]
    public void MarkReady_SetsIsReadyToTrue()
    {
        var gate = new StartupGate();

        gate.MarkReady();

        Assert.True(gate.IsReady);
    }

    [Fact]
    public void MarkReady_CalledTwice_DoesNotThrowAndStaysReady()
    {
        var gate = new StartupGate();

        gate.MarkReady();
        gate.MarkReady();

        Assert.True(gate.IsReady);
    }
}
