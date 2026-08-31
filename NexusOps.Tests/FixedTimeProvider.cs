namespace NexusOps.Tests;

/// <summary>
/// A <see cref="TimeProvider"/> pinned to one instant, so that anything derived from "today"
/// is deterministic. Written by hand rather than pulled from a testing package — the only
/// member under test here is <see cref="GetUtcNow"/>.
/// </summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    /// <summary>A stable, arbitrary instant for tests that only need dates to be fixed, not specific.</summary>
    public static readonly DateTimeOffset DefaultNow = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    public static FixedTimeProvider Default => new(DefaultNow);

    public static DateOnly DefaultToday => DateOnly.FromDateTime(DefaultNow.UtcDateTime);

    public override DateTimeOffset GetUtcNow() => now;
}
