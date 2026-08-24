using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NexusOps.AgentHost.Configuration;
using NexusOps.AgentHost.Services;

namespace NexusOps.Tests.Sessions;

/// <summary>
/// Covers 003 FR-008 (startup validation) and FR-012 (one correlatable session log token).
/// </summary>
public class SessionConfigurationTests
{
    private static IOptions<ConversationSessionOptions> Resolve(params (string Key, string Value)[] settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s =>
                new KeyValuePair<string, string?>($"Session:{s.Key}", s.Value)))
            .Build();

        var services = new ServiceCollection();
        services.AddOptions<ConversationSessionOptions>()
            .Bind(config.GetSection(ConversationSessionOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services.BuildServiceProvider().GetRequiredService<IOptions<ConversationSessionOptions>>();
    }

    [Fact]
    public void Defaults_AreTwentyTurnsAndThirtyMinutes()
    {
        var options = Resolve().Value;

        Assert.Equal(20, options.MaxTurns);
        Assert.Equal(30, options.SlidingExpirationMinutes);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void ANonPositiveMaxTurns_IsRejected(string value)
    {
        var ex = Assert.Throws<OptionsValidationException>(() => Resolve(("MaxTurns", value)).Value);

        Assert.Contains("Session:MaxTurns", string.Join(" ", ex.Failures));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void ANonPositiveSlidingExpiration_IsRejected(string value)
    {
        // Finding 4: this previously started cleanly, then threw on every store write — swallowed
        // inside the store's catch and logged as a Redis connection failure.
        var ex = Assert.Throws<OptionsValidationException>(() => Resolve(("SlidingExpirationMinutes", value)).Value);

        Assert.Contains("Session:SlidingExpirationMinutes", string.Join(" ", ex.Failures));
    }

    [Fact]
    public void ValidValues_AreAccepted()
    {
        var options = Resolve(("MaxTurns", "4"), ("SlidingExpirationMinutes", "10")).Value;

        Assert.Equal(4, options.MaxTurns);
        Assert.Equal(10, options.SlidingExpirationMinutes);
    }

    [Fact]
    public void ANonPositiveSlidingExpiration_WouldHaveThrownOnEveryWrite()
    {
        // Demonstrates why FR-008 covers this key: the framework type rejects it outright.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(0)
            });
    }

    [Fact]
    public void RangeAttributes_DocumentTheConstraintOnBothKeys()
    {
        var properties = typeof(ConversationSessionOptions).GetProperties();

        Assert.All(
            properties.Where(p => p.PropertyType == typeof(int)),
            p => Assert.Contains(p.GetCustomAttributes(typeof(RangeAttribute), false), _ => true));
    }
}

/// <summary>Covers 003 FR-012: both components must emit the same, non-reversible session token.</summary>
public class SessionLogTokenTests
{
    private const string SessionId = "11111111-2222-3333-4444-555555555555";

    [Fact]
    public void TheSameSessionId_AlwaysYieldsTheSameToken()
    {
        Assert.Equal(SessionLogToken.For(SessionId), SessionLogToken.For(SessionId));
    }

    [Fact]
    public void DifferentSessionIds_YieldDifferentTokens()
    {
        Assert.NotEqual(SessionLogToken.For(SessionId), SessionLogToken.For(Guid.NewGuid().ToString()));
    }

    [Fact]
    public void TheTokenDoesNotLeakTheIdentifier()
    {
        var token = SessionLogToken.For(SessionId);

        // The old store-side derivation emitted sessionId[..8], exposing a third of a real UUID.
        Assert.DoesNotContain(token, SessionId, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(SessionId[..8], token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnAbsentSessionId_YieldsTheSentinel(string? sessionId)
    {
        Assert.Equal(SessionLogToken.None, SessionLogToken.For(sessionId));
    }

    [Fact]
    public void NewlinesCannotReachTheLog()
    {
        var token = SessionLogToken.For("abc\r\ninjected log line");

        Assert.DoesNotContain('\r', token);
        Assert.DoesNotContain('\n', token);
    }

    [Fact]
    public void TheTokenIsShortAndHexadecimal()
    {
        var token = SessionLogToken.For(SessionId);

        Assert.Equal(8, token.Length);
        Assert.True(token.All(Uri.IsHexDigit));
    }
}
