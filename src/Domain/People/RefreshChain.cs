namespace Domain.People;

/// <summary>Why a refresh attempt was refused. The caller maps these to one HTTP answer.</summary>
public enum RefreshOutcome
{
    Rotated,

    /// <summary>The presented secret does not match the chain's current link.</summary>
    UnknownToken,

    /// <summary>A link that was already rotated away was presented again - the chain is revoked.</summary>
    Reused,

    Revoked,

    /// <summary>The seven day window has passed; the user logs in again.</summary>
    Expired,

    /// <summary>The account moved on (password, role, status, logout-all) since this chain started.</summary>
    Stale
}

/// <summary>
/// One login on one device. The chain carries a single live secret: every renewal replaces it, so a
/// stolen copy of an older link is detectable - and the detection revokes the whole chain rather
/// than the single link, because by then it is unknown whether the thief or the owner is holding
/// the newest one (spec 0002, AC-12).
/// </summary>
public sealed class RefreshChain
{
    public static readonly TimeSpan Window = TimeSpan.FromDays(7);

    private readonly List<string> _rotatedTokenHashes;

    /// <summary>Materialisation only (ORM); real creation goes through <see cref="Start"/>.</summary>
    private RefreshChain()
    {
        CurrentTokenHash = string.Empty;
        _rotatedTokenHashes = [];
    }

    private RefreshChain(
        Guid id,
        Guid participantId,
        string currentTokenHash,
        int accountSessionVersion,
        DateTimeOffset startedAtUtc,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset? revokedAtUtc,
        IEnumerable<string> rotatedTokenHashes)
    {
        Id = id;
        ParticipantId = participantId;
        CurrentTokenHash = currentTokenHash;
        AccountSessionVersion = accountSessionVersion;
        StartedAtUtc = startedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        RevokedAtUtc = revokedAtUtc;
        _rotatedTokenHashes = [.. rotatedTokenHashes];
    }

    public Guid Id { get; }

    public Guid ParticipantId { get; }

    /// <summary>Hash of the only secret that may currently be presented; never the secret itself.</summary>
    public string CurrentTokenHash { get; private set; }

    /// <summary>The account's session version when this chain was issued.</summary>
    public int AccountSessionVersion { get; }

    public DateTimeOffset StartedAtUtc { get; }

    /// <summary>Fixed at login: rotation never extends the window (spec 0002, AC-13).</summary>
    public DateTimeOffset ExpiresAtUtc { get; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public bool IsRevoked => RevokedAtUtc is not null;

    /// <summary>Links already rotated away - presenting one is the theft signal.</summary>
    public IReadOnlyList<string> RotatedTokenHashes => _rotatedTokenHashes;

    public static RefreshChain Start(
        Guid participantId,
        string tokenHash,
        int accountSessionVersion,
        DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        DateTimeOffset startedAt = nowUtc.ToUniversalTime();

        return new RefreshChain(
            Guid.CreateVersion7(),
            participantId,
            tokenHash,
            accountSessionVersion,
            startedAt,
            startedAt.Add(Window),
            revokedAtUtc: null,
            rotatedTokenHashes: []);
    }

    public static RefreshChain FromStorage(
        Guid id,
        Guid participantId,
        string currentTokenHash,
        int accountSessionVersion,
        DateTimeOffset startedAtUtc,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset? revokedAtUtc,
        IEnumerable<string> rotatedTokenHashes) =>
        new(id, participantId, currentTokenHash, accountSessionVersion,
            startedAtUtc.ToUniversalTime(), expiresAtUtc.ToUniversalTime(),
            revokedAtUtc?.ToUniversalTime(), rotatedTokenHashes);

    /// <summary>
    /// Attempts a rotation. On <see cref="RefreshOutcome.Reused"/> the chain is already revoked when
    /// this returns - the caller only has to persist it.
    /// </summary>
    public RefreshOutcome TryRotate(
        string presentedTokenHash,
        string newTokenHash,
        int currentAccountSessionVersion,
        DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presentedTokenHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(newTokenHash);

        if (_rotatedTokenHashes.Contains(presentedTokenHash))
        {
            // Theft signal: this link was spent earlier. Burn the whole chain, including the newest
            // link, because we cannot tell the owner from the thief any more.
            Revoke(nowUtc);
            return RefreshOutcome.Reused;
        }

        if (IsRevoked)
        {
            return RefreshOutcome.Revoked;
        }

        if (presentedTokenHash != CurrentTokenHash)
        {
            return RefreshOutcome.UnknownToken;
        }

        if (nowUtc.ToUniversalTime() >= ExpiresAtUtc)
        {
            return RefreshOutcome.Expired;
        }

        if (currentAccountSessionVersion != AccountSessionVersion)
        {
            return RefreshOutcome.Stale;
        }

        _rotatedTokenHashes.Add(CurrentTokenHash);
        CurrentTokenHash = newTokenHash;

        return RefreshOutcome.Rotated;
    }

    public void Revoke(DateTimeOffset nowUtc) => RevokedAtUtc ??= nowUtc.ToUniversalTime();
}
