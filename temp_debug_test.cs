// Minimal repro for NSubstitute ValueTask tracking
// Not a real project file - just checking concept
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;

// Simulate ICacheService with ValueTask RemoveAsync
public interface ITestService
{
    ValueTask RemoveAsync(string key, CancellationToken ct = default);
}

public class Program
{
    public static async Task Main()
    {
        var svc = Substitute.For<ITestService>();
        // NOT configured - uses default
        
        await svc.RemoveAsync("test-key");
        
        await svc.Received(1).RemoveAsync("test-key", Arg.Any<CancellationToken>());
        Console.WriteLine("ValueTask tracking works!");
    }
}
