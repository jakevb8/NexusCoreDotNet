namespace NexusCoreDotNet.Services;

/// <summary>
/// Lightweight representation of a verified Firebase ID token, decoupled from the
/// FirebaseAdmin SDK's <c>FirebaseToken</c> so it can be easily faked in unit tests.
/// </summary>
public record DecodedToken(string Uid, IReadOnlyDictionary<string, object> Claims);
