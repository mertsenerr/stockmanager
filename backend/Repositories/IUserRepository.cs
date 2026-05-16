using MongoDB.Driver;
using SayimLink.Api.Models;
using SayimLink.Api.Services;

namespace SayimLink.Api.Repositories;

public interface IUserRepository
{
    Task<User?> FindByEmailAsync(string email, CancellationToken ct = default);
    Task<User?> FindByIdAsync(string id, CancellationToken ct = default);
    Task<User?> FindByPasswordResetHashAsync(string hash, CancellationToken ct = default);
    Task<User?> FindByEmailVerificationHashAsync(string hash, CancellationToken ct = default);
    Task<bool> AnyAdminAsync(CancellationToken ct = default);
    Task<int> CountActiveAdminsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<User>> ListAsync(bool includeInactive, CancellationToken ct = default);
    Task<IReadOnlyList<User>> ListByIdsAsync(IEnumerable<string> ids, CancellationToken ct = default);
    Task InsertAsync(User user, CancellationToken ct = default);
    Task ReplaceAsync(User user, CancellationToken ct = default);
    Task SoftDeleteAsync(string id, CancellationToken ct = default);
    Task AddMagazaToUserAsync(string userId, string magazaId, CancellationToken ct = default);
    Task RemoveMagazaFromUserAsync(string userId, string magazaId, CancellationToken ct = default);

    // ─── Atomic mutations for fields that get touched outside the read/modify/
    // write loop (login failure counters, OTP state, recovery consumption). Full
    // ReplaceAsync here would race with concurrent updates and lose writes.
    Task<int> IncrementLoginFailedAttemptsAsync(string userId, CancellationToken ct = default);
    Task ApplyLoginLockoutAsync(string userId, DateTime lockedUntil, CancellationToken ct = default);
    Task ClearLoginFailureStateAsync(string userId, CancellationToken ct = default);

    Task<int> IncrementTwoFactorFailedAttemptsAsync(string userId, CancellationToken ct = default);
    Task ApplyTwoFactorLockoutAsync(string userId, DateTime lockedUntil, CancellationToken ct = default);
    Task ClearTwoFactorFailureStateAsync(string userId, CancellationToken ct = default);

    Task SetEmailOtpAsync(string userId, string hash, DateTime expiresAt, CancellationToken ct = default);
    Task ClearEmailOtpAsync(string userId, CancellationToken ct = default);
    /// <summary>Records that a fresh OTP was just sent. Also rolls the 24-hour cap window.</summary>
    Task RecordEmailOtpSendAsync(string userId, DateTime sentAt, DateTime dayWindowStart, int dayCount, CancellationToken ct = default);

    Task ConsumeRecoveryCodeAsync(string userId, string matchedHash, CancellationToken ct = default);

    /// <summary>Bumps the per-user TokenInvalidatedAt cut-off. The JwtBearer
    /// OnTokenValidated hook compares incoming JWT <c>iat</c> claims against
    /// this — anything issued before "now" gets rejected immediately, instead
    /// of waiting up to AccessTokenMinutes for natural expiry.</summary>
    Task BumpTokenInvalidationAsync(string userId, CancellationToken ct = default);
}

public sealed class UserRepository : IUserRepository
{
    private readonly IMongoCollection<User> _users;

    public UserRepository(IMongoDbService mongo)
    {
        _users = mongo.Database.GetCollection<User>("users");

        // Eski hard unique email index'i drop et — pasif kayıtlar yeni kayıtla çakışmasın.
        try { _users.Indexes.DropOne("ix_users_email_unique"); }
        catch { /* yoksa yoksay */ }

        // Yeni partial unique email index — sadece aktif kullanıcılar arasında benzersizlik.
        try
        {
            _users.Indexes.CreateOne(new CreateIndexModel<User>(
                Builders<User>.IndexKeys.Ascending(u => u.Email),
                new CreateIndexOptions<User>
                {
                    Unique = true,
                    Name = "ix_users_email_unique_active",
                    PartialFilterExpression = Builders<User>.Filter.Eq(u => u.AktifMi, true),
                }));
        }
        catch { /* mevcut index uyumsuzluğunda servis hâlâ ayağa kalksın */ }
    }

    public Task<User?> FindByEmailAsync(string email, CancellationToken ct = default) =>
        _users.Find(u => u.Email == email.ToLowerInvariant()).FirstOrDefaultAsync(ct)!;

    public Task<User?> FindByIdAsync(string id, CancellationToken ct = default) =>
        _users.Find(u => u.Id == id).FirstOrDefaultAsync(ct)!;

    public Task<User?> FindByPasswordResetHashAsync(string hash, CancellationToken ct = default) =>
        _users.Find(u => u.PasswordResetTokenHash == hash).FirstOrDefaultAsync(ct)!;

    public Task<User?> FindByEmailVerificationHashAsync(string hash, CancellationToken ct = default) =>
        _users.Find(u => u.EmailVerificationTokenHash == hash).FirstOrDefaultAsync(ct)!;

    public async Task<bool> AnyAdminAsync(CancellationToken ct = default) =>
        await _users.Find(u => u.Rol == Roles.Admin).AnyAsync(ct);

    public async Task<int> CountActiveAdminsAsync(CancellationToken ct = default) =>
        (int)await _users.CountDocumentsAsync(u => u.Rol == Roles.Admin && u.AktifMi, cancellationToken: ct);

    public async Task<IReadOnlyList<User>> ListAsync(bool includeInactive, CancellationToken ct = default)
    {
        var filter = includeInactive
            ? Builders<User>.Filter.Empty
            : Builders<User>.Filter.Eq(u => u.AktifMi, true);
        return await _users.Find(filter).SortBy(u => u.AdSoyad).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<User>> ListByIdsAsync(IEnumerable<string> ids, CancellationToken ct = default)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return Array.Empty<User>();
        return await _users
            .Find(Builders<User>.Filter.In(u => u.Id, idList))
            .ToListAsync(ct);
    }

    public Task InsertAsync(User user, CancellationToken ct = default)
    {
        user.Email = user.Email.ToLowerInvariant();
        return _users.InsertOneAsync(user, cancellationToken: ct);
    }

    public Task ReplaceAsync(User user, CancellationToken ct = default)
    {
        user.Email = user.Email.ToLowerInvariant();
        return _users.ReplaceOneAsync(u => u.Id == user.Id, user, cancellationToken: ct);
    }

    public async Task SoftDeleteAsync(string id, CancellationToken ct = default)
    {
        var update = Builders<User>.Update.Set(u => u.AktifMi, false);
        await _users.UpdateOneAsync(u => u.Id == id, update, cancellationToken: ct);
    }

    public Task AddMagazaToUserAsync(string userId, string magazaId, CancellationToken ct = default)
    {
        var update = Builders<User>.Update.AddToSet(u => u.MagazaIds, magazaId);
        return _users.UpdateOneAsync(u => u.Id == userId, update, cancellationToken: ct);
    }

    public Task RemoveMagazaFromUserAsync(string userId, string magazaId, CancellationToken ct = default)
    {
        var update = Builders<User>.Update.Pull(u => u.MagazaIds, magazaId);
        return _users.UpdateOneAsync(u => u.Id == userId, update, cancellationToken: ct);
    }

    public async Task<int> IncrementLoginFailedAttemptsAsync(string userId, CancellationToken ct = default)
    {
        var update = Builders<User>.Update.Inc(u => u.LoginFailedAttempts, 1);
        var opts = new FindOneAndUpdateOptions<User> { ReturnDocument = ReturnDocument.After };
        var updated = await _users.FindOneAndUpdateAsync<User>(
            u => u.Id == userId, update, opts, ct);
        return updated?.LoginFailedAttempts ?? 0;
    }

    public Task ApplyLoginLockoutAsync(string userId, DateTime lockedUntil, CancellationToken ct = default)
    {
        var update = Builders<User>.Update
            .Set(u => u.LoginLockedUntil, lockedUntil)
            .Set(u => u.LoginFailedAttempts, 0);
        return _users.UpdateOneAsync(u => u.Id == userId, update, cancellationToken: ct);
    }

    public Task ClearLoginFailureStateAsync(string userId, CancellationToken ct = default)
    {
        var update = Builders<User>.Update
            .Set(u => u.LoginFailedAttempts, 0)
            .Set(u => u.LoginLockedUntil, (DateTime?)null);
        return _users.UpdateOneAsync(u => u.Id == userId, update, cancellationToken: ct);
    }

    public async Task<int> IncrementTwoFactorFailedAttemptsAsync(string userId, CancellationToken ct = default)
    {
        var update = Builders<User>.Update.Inc(u => u.TwoFactorFailedAttempts, 1);
        var opts = new FindOneAndUpdateOptions<User> { ReturnDocument = ReturnDocument.After };
        var updated = await _users.FindOneAndUpdateAsync<User>(
            u => u.Id == userId, update, opts, ct);
        return updated?.TwoFactorFailedAttempts ?? 0;
    }

    public Task ApplyTwoFactorLockoutAsync(string userId, DateTime lockedUntil, CancellationToken ct = default)
    {
        var update = Builders<User>.Update
            .Set(u => u.TwoFactorLockedUntil, lockedUntil)
            .Set(u => u.TwoFactorFailedAttempts, 0);
        return _users.UpdateOneAsync(u => u.Id == userId, update, cancellationToken: ct);
    }

    public Task ClearTwoFactorFailureStateAsync(string userId, CancellationToken ct = default)
    {
        var update = Builders<User>.Update
            .Set(u => u.TwoFactorFailedAttempts, 0)
            .Set(u => u.TwoFactorLockedUntil, (DateTime?)null);
        return _users.UpdateOneAsync(u => u.Id == userId, update, cancellationToken: ct);
    }

    public Task SetEmailOtpAsync(string userId, string hash, DateTime expiresAt, CancellationToken ct = default)
    {
        var update = Builders<User>.Update
            .Set(u => u.EmailOtpCodeHash, hash)
            .Set(u => u.EmailOtpExpiresAt, expiresAt);
        return _users.UpdateOneAsync(u => u.Id == userId, update, cancellationToken: ct);
    }

    public Task ClearEmailOtpAsync(string userId, CancellationToken ct = default)
    {
        var update = Builders<User>.Update
            .Set(u => u.EmailOtpCodeHash, (string?)null)
            .Set(u => u.EmailOtpExpiresAt, (DateTime?)null);
        return _users.UpdateOneAsync(u => u.Id == userId, update, cancellationToken: ct);
    }

    public Task RecordEmailOtpSendAsync(string userId, DateTime sentAt, DateTime dayWindowStart, int dayCount, CancellationToken ct = default)
    {
        var update = Builders<User>.Update
            .Set(u => u.EmailOtpLastSentAt, sentAt)
            .Set(u => u.EmailOtpDayWindowStart, dayWindowStart)
            .Set(u => u.EmailOtpDayCount, dayCount);
        return _users.UpdateOneAsync(u => u.Id == userId, update, cancellationToken: ct);
    }

    public Task ConsumeRecoveryCodeAsync(string userId, string matchedHash, CancellationToken ct = default)
    {
        var update = Builders<User>.Update.Pull(u => u.RecoveryCodeHashes, matchedHash);
        return _users.UpdateOneAsync(u => u.Id == userId, update, cancellationToken: ct);
    }

    public Task BumpTokenInvalidationAsync(string userId, CancellationToken ct = default)
    {
        var update = Builders<User>.Update.Set(u => u.TokenInvalidatedAt, DateTime.UtcNow);
        return _users.UpdateOneAsync(u => u.Id == userId, update, cancellationToken: ct);
    }
}
