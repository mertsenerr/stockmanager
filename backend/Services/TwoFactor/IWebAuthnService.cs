using System.Text;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.Extensions.Caching.Memory;
using SayimLink.Api.Models;

namespace SayimLink.Api.Services.TwoFactor;

public interface IWebAuthnService
{
    /// <summary>Build registration options ("creation options") for an authenticated user.</summary>
    Task<CredentialCreateOptions> StartRegistrationAsync(User user, CancellationToken ct = default);

    /// <summary>Verify the attestation response and return the credential to persist.</summary>
    Task<WebAuthnCredential> CompleteRegistrationAsync(User user, AuthenticatorAttestationRawResponse response, CancellationToken ct = default);

    /// <summary>Build assertion options for a known user (called during 2FA login step).</summary>
    AssertionOptions StartAssertion(User user);

    /// <summary>Verify the assertion response. Returns the matched credential and the new
    /// signature counter so the caller can persist it.</summary>
    Task<(WebAuthnCredential matched, uint newCounter)> CompleteAssertionAsync(
        User user, AuthenticatorAssertionRawResponse response, CancellationToken ct = default);
}

public sealed class WebAuthnService : IWebAuthnService
{
    private readonly IFido2 _fido2;
    private readonly IMemoryCache _cache;

    public WebAuthnService(IFido2 fido2, IMemoryCache cache)
    {
        _fido2 = fido2;
        _cache = cache;
    }

    public Task<CredentialCreateOptions> StartRegistrationAsync(User user, CancellationToken ct = default)
    {
        var fido2User = ToFido2User(user);
        var existing = user.WebAuthnCredentials
            .Select(c => new PublicKeyCredentialDescriptor(Convert.FromBase64String(c.CredentialId)))
            .ToList();

        var authSel = AuthenticatorSelection.Default;
        authSel.UserVerification = UserVerificationRequirement.Preferred;

        var options = _fido2.RequestNewCredential(
            fido2User, existing, authSel,
            AttestationConveyancePreference.None,
            new AuthenticationExtensionsClientInputs());

        _cache.Set(RegKey(user.Id), options.ToJson(), TimeSpan.FromMinutes(5));
        return Task.FromResult(options);
    }

    public async Task<WebAuthnCredential> CompleteRegistrationAsync(User user, AuthenticatorAttestationRawResponse response, CancellationToken ct = default)
    {
        if (!_cache.TryGetValue(RegKey(user.Id), out string? optionsJson) || optionsJson is null)
            throw new InvalidOperationException("Registration challenge not found or expired.");
        var options = CredentialCreateOptions.FromJson(optionsJson);

        IsCredentialIdUniqueToUserAsyncDelegate isUnique = (args, _) => Task.FromResult(true);
        var result = await _fido2.MakeNewCredentialAsync(response, options, isUnique, cancellationToken: ct);
        if (result.Status != "ok" || result.Result is null)
            throw new InvalidOperationException(result.ErrorMessage ?? "Registration failed.");

        _cache.Remove(RegKey(user.Id));

        return new WebAuthnCredential
        {
            CredentialId     = Convert.ToBase64String(result.Result.CredentialId),
            PublicKeyCose    = Convert.ToBase64String(result.Result.PublicKey),
            UserHandle       = Convert.ToBase64String(result.Result.User.Id),
            SignatureCounter = result.Result.Counter,
            CredType         = result.Result.CredType ?? "public-key",
            AaGuid           = result.Result.Aaguid.ToString(),
            CreatedAt        = DateTime.UtcNow,
        };
    }

    public AssertionOptions StartAssertion(User user)
    {
        var allow = user.WebAuthnCredentials
            .Select(c => new PublicKeyCredentialDescriptor(Convert.FromBase64String(c.CredentialId)))
            .ToList();

        var options = _fido2.GetAssertionOptions(allow, UserVerificationRequirement.Preferred);
        _cache.Set(AuthKey(user.Id), options.ToJson(), TimeSpan.FromMinutes(5));
        return options;
    }

    public async Task<(WebAuthnCredential matched, uint newCounter)> CompleteAssertionAsync(
        User user, AuthenticatorAssertionRawResponse response, CancellationToken ct = default)
    {
        if (!_cache.TryGetValue(AuthKey(user.Id), out string? optionsJson) || optionsJson is null)
            throw new InvalidOperationException("Assertion challenge not found or expired.");
        var options = AssertionOptions.FromJson(optionsJson);

        var credIdB64 = Convert.ToBase64String(response.Id);
        var stored = user.WebAuthnCredentials.FirstOrDefault(c => c.CredentialId == credIdB64)
            ?? throw new InvalidOperationException("Credential not registered for this user.");

        var publicKey = Convert.FromBase64String(stored.PublicKeyCose);

        IsUserHandleOwnerOfCredentialIdAsync isOwner = (_, _) => Task.FromResult(true);
        var result = await _fido2.MakeAssertionAsync(
            response, options, publicKey,
            stored.SignatureCounter, isOwner, cancellationToken: ct);

        if (result.Status != "ok")
            throw new InvalidOperationException(result.ErrorMessage ?? "Assertion failed.");

        _cache.Remove(AuthKey(user.Id));
        return (stored, result.Counter);
    }

    private static Fido2User ToFido2User(User user) => new()
    {
        Id          = Encoding.UTF8.GetBytes(user.Id),
        Name        = user.Email,
        DisplayName = user.AdSoyad,
    };

    private static string RegKey(string uid)  => $"webauthn:reg:{uid}";
    private static string AuthKey(string uid) => $"webauthn:auth:{uid}";
}
