namespace Domain.People;

/// <summary>
/// A person in the system (docs/domain.md, Katılımcı). Single-record model: the system account is
/// an optional part of this record, so a person who will never log in still has exactly one row,
/// and an Admin or Instructor is the same record with an account and no member firm.
/// </summary>
public sealed class Participant
{
    /// <summary>Column size of the name; checked before saving so it is a user error, not a crash.</summary>
    public const int MaxFullNameLength = 200;

    /// <summary>Materialisation only (ORM); real creation goes through the factory methods.</summary>
    private Participant()
    {
        FullName = string.Empty;
    }

    private Participant(Guid id, string fullName, Guid? memberFirmId, UserAccount? account)
    {
        Id = id;
        FullName = fullName;
        MemberFirmId = memberFirmId;
        Account = account;
    }

    public Guid Id { get; }

    public string FullName { get; private set; }

    /// <summary>Null for institution staff (Admin, Instructor); set for member firm employees.</summary>
    public Guid? MemberFirmId { get; private set; }

    /// <summary>Null when the person has no system account - they cannot log in (BR: tek kayıt modeli).</summary>
    public UserAccount? Account { get; private set; }

    public bool HasAccount => Account is not null;

    /// <summary>
    /// Institution staff (Admin, Instructor) belong to no member firm. Holding this as an invariant
    /// is what keeps a firm representative's firm scope from ever reaching a staff account.
    /// </summary>
    public static bool RoleMayBelongToFirm(Role role) => role is Role.FirmRepresentative or Role.Participant;

    /// <summary>A person who will be enrolled but never logs in.</summary>
    public static Participant CreateWithoutAccount(string fullName, Guid? memberFirmId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);

        return new Participant(Guid.CreateVersion7(), fullName.Trim(), memberFirmId, account: null);
    }

    public static Participant CreateWithAccount(
        string fullName,
        Guid? memberFirmId,
        string email,
        string temporaryPasswordHash,
        Role role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        EnsureFirmFitsRole(memberFirmId, role);

        return new Participant(
            Guid.CreateVersion7(),
            fullName.Trim(),
            memberFirmId,
            UserAccount.CreateWithTemporaryPassword(email, temporaryPasswordHash, role));
    }

    public static Participant FromStorage(Guid id, string fullName, Guid? memberFirmId, UserAccount? account) =>
        new(id, fullName, memberFirmId, account);

    public void Rename(string fullName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);

        FullName = fullName.Trim();
    }

    /// <summary>Gives an existing person an account; a person never has more than one.</summary>
    public void GrantAccount(string email, string temporaryPasswordHash, Role role)
    {
        if (HasAccount)
        {
            throw new InvalidOperationException(
                $"Participant {Id} already has an account; a person record holds at most one.");
        }

        EnsureFirmFitsRole(MemberFirmId, role);

        Account = UserAccount.CreateWithTemporaryPassword(email, temporaryPasswordHash, role);
    }

    /// <summary>
    /// Changes the account's role. A member firm employee cannot become institution staff while
    /// still belonging to the firm; the caller is expected to have checked this and refused politely.
    /// </summary>
    public void AssignRole(Role role)
    {
        if (Account is null)
        {
            throw new InvalidOperationException($"Participant {Id} has no account to assign a role to.");
        }

        EnsureFirmFitsRole(MemberFirmId, role);

        Account.AssignRole(role);
    }

    /// <summary>True when this person belongs to the given firm - the mechanical form of BR-8.</summary>
    public bool BelongsToFirm(Guid firmId) => MemberFirmId == firmId;

    private static void EnsureFirmFitsRole(Guid? memberFirmId, Role role)
    {
        if (memberFirmId is not null && !RoleMayBelongToFirm(role))
        {
            throw new InvalidOperationException($"A {role} account cannot belong to a member firm.");
        }
    }
}
