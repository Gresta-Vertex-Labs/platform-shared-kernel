using Bogus;

namespace SharedKernel.Testing.Fakers;

/// <summary>
/// Shared determinism convention for <see cref="Faker{T}"/>-derived test data generation.
/// </summary>
/// <remarks>
/// <para>
/// Concrete <see cref="Faker{T}"/> definitions for business aggregates are <em>not</em> defined in
/// this package — they depend on each microservice's own aggregate shapes and belong in that
/// service's own test project. <see cref="SharedKernel.Testing"/> ships only this shared
/// determinism convention, which every one of those fakers should opt into.
/// </para>
/// <para>
/// This is one of two documented, deliberate exceptions to this domain's "no static mutable state"
/// rule (alongside <c>AmbientActivityTestHelper</c>'s static <c>ActivityListener</c>) — an explicit,
/// opt-in, process-wide determinism convention, never incidental shared state.
/// </para>
/// </remarks>
public static class FakerSeeding
{
    /// <summary>
    /// Sets <see cref="Randomizer.Seed"/> to a fixed, deterministic <see cref="Random"/> instance so
    /// every <see cref="Faker{T}"/> in the current process produces reproducible output across CI
    /// re-executions.
    /// </summary>
    /// <param name="seed">
    /// The seed value to use. Defaults to <c>8675309</c> — a fixed, documented constant, never
    /// derived from real time or the environment.
    /// </param>
    /// <remarks>
    /// Call once per test assembly (e.g. from an xUnit assembly fixture or a module initializer) so
    /// every <see cref="Faker{T}"/> in that run is deterministic.
    /// </remarks>
    public static void Apply(int seed = 8675309) => Randomizer.Seed = new Random(seed);
}
