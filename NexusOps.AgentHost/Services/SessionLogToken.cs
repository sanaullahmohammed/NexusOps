using System.Security.Cryptography;
using System.Text;

namespace NexusOps.AgentHost.Services;

/// <summary>
/// Derives the token used to identify a session in logs.
/// </summary>
/// <remarks>
/// Both <see cref="AgentService"/> and <see cref="RedisConversationStore"/> emit a field named
/// <c>SessionIdPrefix</c>, but they derived it differently — one hashed the identifier, the other
/// emitted its first eight raw characters. No query could join a <c>session.created</c> event to the
/// <c>session.degraded</c> that followed it, and the raw form leaked a third of a real UUID while
/// the other deliberately did not. One derivation, used everywhere, fixes both.
/// </remarks>
public static class SessionLogToken
{
    /// <summary>Emitted in place of a token when there is no session identifier to describe.</summary>
    public const string None = "none";

    private const int TokenLength = 8;

    /// <summary>
    /// Returns a short, stable, non-reversible token for <paramref name="sessionId"/>.
    /// The same identifier always yields the same token; the identifier cannot be recovered from it.
    /// </summary>
    public static string For(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return None;
        }

        // Strip CR/LF before hashing. Hashed output cannot contain them, but the guard documents
        // that untrusted input reaches this path and keeps the property if the format ever changes.
        var sanitised = sessionId.Replace("\r", string.Empty).Replace("\n", string.Empty);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sanitised));
        return Convert.ToHexString(hash)[..TokenLength];
    }
}
