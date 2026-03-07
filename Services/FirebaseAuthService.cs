using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;

namespace NexusCoreDotNet.Services;

public class FirebaseAuthService : IFirebaseAuthService
{
    private readonly FirebaseAuth _auth;

    public FirebaseAuthService(IConfiguration configuration)
    {
        // The REST API (FirebaseJwtHandler) receives tokens from mobile clients
        // (Android / React Native / iOS) which authenticate against the
        // "nexus-core-rms" Firebase project. Use the named "rms" FirebaseApp
        // so VerifyIdTokenAsync checks the correct "aud" claim.
        // The default FirebaseApp ("nexus-core-dotnet") is used only by Razor Pages,
        // which instantiate FirebaseAuthService directly (not via DI).
        var rmsApp = FirebaseApp.GetInstance("rms");
        _auth = FirebaseAuth.GetAuth(rmsApp);
    }

    /// <summary>
    /// Verify a raw Firebase ID token and return the decoded claims.
    /// Throws UnauthorizedAccessException on failure.
    /// </summary>
    public async Task<DecodedToken> VerifyIdTokenAsync(string idToken)
    {
        try
        {
            var token = await _auth.VerifyIdTokenAsync(idToken);
            return new DecodedToken(token.Uid, token.Claims);
        }
        catch (Exception ex)
        {
            throw new UnauthorizedAccessException("Invalid or expired Firebase token", ex);
        }
    }
}
