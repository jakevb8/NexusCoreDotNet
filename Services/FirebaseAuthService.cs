using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;

namespace NexusCoreDotNet.Services;

public class FirebaseAuthService : IFirebaseAuthService
{
    private readonly FirebaseAuth _auth;

    public FirebaseAuthService(IConfiguration configuration)
    {
        // FirebaseApp is initialized once in Program.cs; grab the default instance here.
        _auth = FirebaseAuth.DefaultInstance;
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
