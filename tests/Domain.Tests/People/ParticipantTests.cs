using Domain.People;

using Shouldly;

namespace Domain.Tests.People;

public sealed class ParticipantTests
{
    private static readonly Guid Firm = Guid.CreateVersion7();

    [Fact]
    public void CreateWithoutAccount_ShouldProduceAPersonWhoCannotLogIn_WhenEnrolledButNeverLogsIn()
    {
        Participant person = Participant.CreateWithoutAccount("  Ayşe Yılmaz  ", Firm);

        person.FullName.ShouldBe("Ayşe Yılmaz");
        person.HasAccount.ShouldBeFalse();
        person.Account.ShouldBeNull();
        person.Id.Version.ShouldBe(7);
    }

    [Fact]
    public void CreateWithAccount_ShouldCarryTemporaryPasswordAndRole_WhenAccountIsCreatedUpFront()
    {
        Participant person = Participant.CreateWithAccount(
            "Mehmet Demir", Firm, "Mehmet@Firma.com", "hash", Role.Participant);

        person.HasAccount.ShouldBeTrue();
        person.Account!.Email.ShouldBe("mehmet@firma.com");
        person.Account.MustChangePassword.ShouldBeTrue();
        person.Account.Role.ShouldBe(Role.Participant);
    }

    [Fact]
    public void CreateWithAccount_ShouldAllowNoFirm_WhenPersonIsInstitutionStaff()
    {
        Participant admin = Participant.CreateWithAccount(
            "Eğitim Sorumlusu", memberFirmId: null, "admin@immib.org", "hash", Role.Admin);

        admin.MemberFirmId.ShouldBeNull();
        admin.BelongsToFirm(Firm).ShouldBeFalse();
    }

    [Fact]
    public void GrantAccount_ShouldGiveAnExistingPersonAnAccount_WhenTheyHaveNone()
    {
        Participant person = Participant.CreateWithoutAccount("Ayşe Yılmaz", Firm);

        person.GrantAccount("ayse@firma.com", "hash", Role.Participant);

        person.HasAccount.ShouldBeTrue();
        person.Account!.Role.ShouldBe(Role.Participant);
    }

    [Fact]
    public void GrantAccount_ShouldReject_WhenPersonAlreadyHasOne()
    {
        Participant person = Participant.CreateWithAccount(
            "Ayşe Yılmaz", Firm, "ayse@firma.com", "hash", Role.Participant);

        // docs/domain.md invariant: a person record holds at most one system account.
        Should.Throw<InvalidOperationException>(() =>
            person.GrantAccount("other@firma.com", "hash", Role.Participant));
    }

    [Fact]
    public void BelongsToFirm_ShouldBeTrueOnlyForOwnFirm_WhenComparingFirms()
    {
        Participant person = Participant.CreateWithoutAccount("Ayşe Yılmaz", Firm);

        person.BelongsToFirm(Firm).ShouldBeTrue();
        person.BelongsToFirm(Guid.CreateVersion7()).ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateWithoutAccount_ShouldReject_WhenNameIsBlank(string name)
    {
        Should.Throw<ArgumentException>(() => Participant.CreateWithoutAccount(name, Firm));
    }

    [Theory]
    [InlineData(Role.Admin)]
    [InlineData(Role.Instructor)]
    public void CreateWithAccount_ShouldReject_WhenStaffRoleIsGivenAFirm(Role role)
    {
        Should.Throw<InvalidOperationException>(() =>
            Participant.CreateWithAccount("Kişi", Firm, "kisi@firma.com", "hash", role));
    }

    [Theory]
    [InlineData(Role.Admin)]
    [InlineData(Role.Instructor)]
    public void AssignRole_ShouldReject_WhenFirmEmployeeWouldBecomeStaff(Role role)
    {
        Participant person = Participant.CreateWithAccount(
            "Firma Çalışanı", Firm, "calisan@firma.com", "hash", Role.Participant);

        Should.Throw<InvalidOperationException>(() => person.AssignRole(role));
        person.Account!.Role.ShouldBe(Role.Participant);
    }

    [Fact]
    public void AssignRole_ShouldChangeTheAccountRole_WhenRoleFitsTheFirm()
    {
        Participant person = Participant.CreateWithAccount(
            "Firma Çalışanı", Firm, "calisan@firma.com", "hash", Role.Participant);
        int before = person.Account!.SessionVersion;

        person.AssignRole(Role.FirmRepresentative);

        person.Account.Role.ShouldBe(Role.FirmRepresentative);
        person.Account.SessionVersion.ShouldBe(before + 1);
    }
}
