namespace Domain.People;

/// <summary>
/// The four roles of docs/security.md (AuthN / AuthZ). Stored as text, never as an int
/// (docs/conventions.md, Data rules).
/// </summary>
public enum Role
{
    /// <summary>Eğitim sorumlusu: may manage every account and assign every role.</summary>
    Admin,

    /// <summary>Teaches sessions; no user management.</summary>
    Instructor,

    /// <summary>Üye firma temsilcisi: own firm only (BR-8), may assign Participant only.</summary>
    FirmRepresentative,

    /// <summary>Sees only their own data.</summary>
    Participant
}
