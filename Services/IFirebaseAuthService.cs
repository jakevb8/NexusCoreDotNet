namespace NexusCoreDotNet.Services;

public interface IFirebaseAuthService
{
    Task<DecodedToken> VerifyIdTokenAsync(string idToken);
}
