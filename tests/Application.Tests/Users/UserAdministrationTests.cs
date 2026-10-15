using Application.Abstractions;
using Application.Common;
using Application.Users;

using Domain.People;

using NSubstitute;

using Shouldly;

namespace Application.Tests.Users;

public sealed class UserAdministrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid FirmA = Guid.CreateVersion7();
    private static readonly Guid FirmB = Guid.CreateVersion7();

    private readonly IParticipantRepository _participants = Substitute.For<IParticipantRepository>();
    private readonly IRefreshChainRepository _chains = Substitute.For<IRefreshChainRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ITemporaryPasswordGenerator _temporaryPasswords =
        Substitute.For<ITemporaryPasswordGenerator>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private static readonly CallerContext Admin = new(Guid.CreateVersion7(), Role.Admin, null);
    private static readonly CallerContext FirmARepresentative =
        new(Guid.CreateVersion7(), Role.FirmRepresentative, FirmA);
    private static readonly CallerContext Instructor = new(Guid.CreateVersion7(), Role.Instructor, null);
    private static readonly CallerContext ParticipantCaller = new(Guid.CreateVersion7(), Role.Participant, FirmA);

    public UserAdministrationTests()
    {
        _clock.UtcNow.Returns(Now);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result<int>.Success(1));
        _hasher.Hash(Arg.Any<string>()).Returns(call => $"hash-of-{call.Arg<string>()}");
        _temporaryPasswords.Create().Returns("Gecici-Sifre-1234");
        _participants.EmailExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(false));
        _chains.ListActiveForParticipantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<RefreshChain>>.Success([]));
        _unitOfWork.RunExclusivelyAsync(
                Arg.Any<ExclusiveSection>(),
                Arg.Any<Func<CancellationToken, Task<Result<UserDto>>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<Result<UserDto>>>>()(call.Arg<CancellationToken>()));
    }

    // --- creating accounts -------------------------------------------------------------------

    [Fact]
    public async Task CreateUser_ShouldReturnTemporaryPasswordOnce_WhenAdminCreatesAnAccount()
    {
        Result<UserWithTemporaryPasswordDto> result = await Create(
            Admin, new CreateUserCommand("Ayşe Yılmaz", " Ayse@Firma.COM ", "Participant", FirmA));

        result.IsSuccess.ShouldBeTrue();
        result.Value.TemporaryPassword.ShouldBe("Gecici-Sifre-1234");
        result.Value.User.Email.ShouldBe("ayse@firma.com");
        result.Value.User.MustChangePassword.ShouldBeTrue();
        result.Value.User.MemberFirmId.ShouldBe(FirmA);
        _participants.Received(1).Add(Arg.Any<Participant>());
    }

    [Fact]
    public async Task CreateUser_ShouldStoreTheHasherOutput_NotTheRawTemporaryPassword()
    {
        Participant? added = null;
        _participants.When(repository => repository.Add(Arg.Any<Participant>()))
            .Do(call => added = call.Arg<Participant>());

        await Create(Admin, new CreateUserCommand("Ayşe", "ayse@firma.com", "Participant", FirmA));

        // What lands on the account is whatever the hasher returned, and the handler asked it to
        // hash the generated password. That a real hash contains no plaintext is AC-9's claim and
        // belongs to the integration test that runs the actual PBKDF2 implementation - a fake
        // hasher cannot prove it.
        _hasher.Received(1).Hash("Gecici-Sifre-1234");
        added!.Account!.PasswordHash.ShouldBe("hash-of-Gecici-Sifre-1234");
    }

    [Fact]
    public async Task CreateUser_ShouldReturnConflict_WhenEmailAlreadyExists()
    {
        _participants.EmailExistsAsync("ayse@firma.com", Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));

        Result<UserWithTemporaryPasswordDto> result = await Create(
            Admin, new CreateUserCommand("Ayşe", "Ayse@Firma.com", "Participant", FirmA));

        // spec 0002, AC-31
        result.Error!.Code.ShouldBe("users.email_already_used");
        result.Error.Type.ShouldBe(ErrorType.BusinessRule);
        _participants.DidNotReceive().Add(Arg.Any<Participant>());
    }

    [Fact]
    public async Task CreateUser_ShouldBeRejected_WhenFirmRepresentativeNamesAnotherFirm()
    {
        // The body asks for firm B; the caller belongs to firm A (spec 0002, AC-19).
        Result<UserWithTemporaryPasswordDto> result = await Create(
            FirmARepresentative, new CreateUserCommand("Mehmet", "mehmet@firma.com", "Participant", FirmB));

        result.Error!.Code.ShouldBe("users.other_firm");
        _participants.DidNotReceive().Add(Arg.Any<Participant>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateUser_ShouldUseTheCallersFirm_WhenFirmRepresentativeNamesTheirOwnOrNone(bool namesOwnFirm)
    {
        Participant? added = null;
        _participants.When(repository => repository.Add(Arg.Any<Participant>()))
            .Do(call => added = call.Arg<Participant>());

        Result<UserWithTemporaryPasswordDto> result = await Create(
            FirmARepresentative,
            new CreateUserCommand("Mehmet", "mehmet@firma.com", "Participant", namesOwnFirm ? FirmA : null));

        result.IsSuccess.ShouldBeTrue();
        added!.MemberFirmId.ShouldBe(FirmA);
        result.Value.User.MemberFirmId.ShouldBe(FirmA);
    }

    [Theory]
    [InlineData("FirmRepresentative")]
    [InlineData("Instructor")]
    [InlineData("Admin")]
    public async Task CreateUser_ShouldBeRejected_WhenFirmRepresentativeAssignsNonParticipantRole(string role)
    {
        Result<UserWithTemporaryPasswordDto> result = await Create(
            FirmARepresentative, new CreateUserCommand("Mehmet", "mehmet@firma.com", role, FirmA));

        // spec 0002, AC-32
        result.Error!.Code.ShouldBe("users.role_not_assignable");
        result.Error.Type.ShouldBe(ErrorType.Forbidden);
        _participants.DidNotReceive().Add(Arg.Any<Participant>());
    }

    [Fact]
    public async Task CreateUser_ShouldBeRejected_WhenFirmRepresentativeHasNoFirm()
    {
        CallerContext firmless = new(Guid.CreateVersion7(), Role.FirmRepresentative, null);

        Result<UserWithTemporaryPasswordDto> result = await Create(
            firmless, new CreateUserCommand("Mehmet", "mehmet@firma.com", "Participant", FirmA));

        result.Error!.Code.ShouldBe("users.other_firm");
    }

    [Theory]
    [InlineData("Instructor")]
    [InlineData("Participant")]
    public async Task CreateUser_ShouldBeRejected_WhenCallerMayNotManageUsers(string callerRole)
    {
        CallerContext caller = callerRole == "Instructor" ? Instructor : ParticipantCaller;

        Result<UserWithTemporaryPasswordDto> result = await Create(
            caller, new CreateUserCommand("Mehmet", "mehmet@firma.com", "Participant", FirmA));

        result.Error!.Code.ShouldBe("users.not_allowed");
        result.Error.Type.ShouldBe(ErrorType.Forbidden);
    }

    [Fact]
    public async Task CreateUser_ShouldBeRejected_WhenRoleIsUnknown()
    {
        Result<UserWithTemporaryPasswordDto> result = await Create(
            Admin, new CreateUserCommand("Mehmet", "mehmet@firma.com", "SuperAdmin", FirmA));

        result.Error!.Code.ShouldBe("users.unknown_role");
    }

    [Theory]
    [InlineData("", "ayse@firma.com", "users.full_name_required")]
    [InlineData("Ayşe", "   ", "users.email_required")]
    public async Task CreateUser_ShouldBeRejected_WhenRequiredFieldIsBlank(
        string fullName, string email, string expectedCode)
    {
        Result<UserWithTemporaryPasswordDto> result = await Create(
            Admin, new CreateUserCommand(fullName, email, "Participant", FirmA));

        result.Error!.Code.ShouldBe(expectedCode);
    }

    [Theory]
    [InlineData("ayse")]
    [InlineData("ayse@")]
    [InlineData("@firma.com")]
    [InlineData("ayse@firma")]
    [InlineData("ayse@@firma.com")]
    [InlineData("ayse yilmaz@firma.com")]
    public async Task CreateUser_ShouldBeRejected_WhenEmailIsMalformed(string email)
    {
        Result<UserWithTemporaryPasswordDto> result = await Create(
            Admin, new CreateUserCommand("Ayşe", email, "Participant", FirmA));

        result.Error!.Code.ShouldBe("users.email_invalid");
        _participants.DidNotReceive().Add(Arg.Any<Participant>());
    }

    [Fact]
    public async Task CreateUser_ShouldBeRejectedAsInput_WhenNameDoesNotFitTheColumn()
    {
        Result<UserWithTemporaryPasswordDto> result = await Create(
            Admin, new CreateUserCommand(new string('a', Participant.MaxFullNameLength + 1), "uzun@firma.com", "Participant", FirmA));

        result.Error!.Code.ShouldBe("users.full_name_too_long");
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public async Task CreateUser_ShouldBeRejectedAsInput_WhenEmailDoesNotFitTheColumn()
    {
        string email = new string('a', UserAccount.MaxEmailLength) + "@firma.com";

        Result<UserWithTemporaryPasswordDto> result = await Create(
            Admin, new CreateUserCommand("Ayşe", email, "Participant", FirmA));

        result.Error!.Code.ShouldBe("users.email_too_long");
    }

    [Theory]
    [InlineData("99")]
    [InlineData("0")]
    [InlineData("participant")]
    public async Task CreateUser_ShouldBeRejected_WhenRoleIsNotAnExactRoleName(string role)
    {
        Result<UserWithTemporaryPasswordDto> result = await Create(
            Admin, new CreateUserCommand("Ayşe", "ayse@firma.com", role, FirmA));

        result.Error!.Code.ShouldBe("users.unknown_role");
    }

    [Fact]
    public async Task CreateUser_ShouldReportEmailAlreadyUsed_WhenARacingCreationWinsTheUniqueIndex()
    {
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<int>.Failure(PersistenceErrors.UniqueViolation));

        Result<UserWithTemporaryPasswordDto> result = await Create(
            Admin, new CreateUserCommand("Ayşe", "ayse@firma.com", "Participant", FirmA));

        result.Error!.Code.ShouldBe("users.email_already_used");
        result.Error.Type.ShouldBe(ErrorType.BusinessRule);
    }

    // --- role, status, password reset --------------------------------------------------------

    [Fact]
    public async Task AssignRole_ShouldBeRejected_WhenRoleIsANumber()
    {
        Participant person = GivenUser(memberFirmId: null, Role.Participant);

        Result<UserDto> result = await Handlers().AssignRoleAsync(
            Admin, new AssignRoleCommand(person.Id, "99"), TestContext.Current.CancellationToken);

        result.Error!.Code.ShouldBe("users.unknown_role");
        person.Account!.Role.ShouldBe(Role.Participant);
    }

    [Fact]
    public async Task SetAccountStatus_ShouldBeRejected_WhenTheWantedStatusIsMissing()
    {
        Participant person = GivenUser(FirmA, Role.Participant);
        person.Account!.Disable();

        Result<UserDto> result = await Handlers().SetAccountStatusAsync(
            Admin, new SetAccountStatusCommand(person.Id, Disabled: null), TestContext.Current.CancellationToken);

        result.Error!.Code.ShouldBe("users.status_required");
        person.Account.IsDisabled.ShouldBeTrue();
    }

    [Fact]
    public async Task AssignRole_ShouldChangeRoleAndEndSessions_WhenAdminAssigns()
    {
        Participant person = GivenUser(memberFirmId: null, Role.Participant);
        int before = person.Account!.SessionVersion;

        Result<UserDto> result = await Handlers().AssignRoleAsync(
            Admin, new AssignRoleCommand(person.Id, "Instructor"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Role.ShouldBe("Instructor");
        person.Account.SessionVersion.ShouldBe(before + 1);
    }

    [Fact]
    public async Task AssignRole_ShouldBeRejected_WhenFirmRepresentativeTargetsAnotherFirm()
    {
        Participant person = GivenUser(FirmB, Role.Participant);

        Result<UserDto> result = await Handlers().AssignRoleAsync(
            FirmARepresentative,
            new AssignRoleCommand(person.Id, "Participant"),
            TestContext.Current.CancellationToken);

        // spec 0002, AC-20: guessing an id must not cross the firm boundary.
        result.Error!.Code.ShouldBe("users.other_firm");
        person.Account!.Role.ShouldBe(Role.Participant);
    }

    [Fact]
    public async Task AssignRole_ShouldBeRejected_WhenFirmRepresentativeTargetsSomeoneWithoutAFirm()
    {
        Participant admin = GivenUser(memberFirmId: null, Role.Admin);

        Result<UserDto> result = await Handlers().AssignRoleAsync(
            FirmARepresentative,
            new AssignRoleCommand(admin.Id, "Participant"),
            TestContext.Current.CancellationToken);

        result.Error!.Code.ShouldBe("users.other_firm");
    }

    [Fact]
    public async Task AssignRole_ShouldBeRejected_WhenFirmRepresentativeAssignsNonParticipantRole()
    {
        Participant person = GivenUser(FirmA, Role.Participant);

        Result<UserDto> result = await Handlers().AssignRoleAsync(
            FirmARepresentative,
            new AssignRoleCommand(person.Id, "FirmRepresentative"),
            TestContext.Current.CancellationToken);

        result.Error!.Code.ShouldBe("users.role_not_assignable");
        person.Account!.Role.ShouldBe(Role.Participant);
    }

    [Fact]
    public async Task AssignRole_ShouldBeRejected_WhenAdminMakesAFirmEmployeeStaff()
    {
        Participant person = GivenUser(FirmA, Role.Participant);

        Result<UserDto> result = await Handlers().AssignRoleAsync(
            Admin, new AssignRoleCommand(person.Id, "Admin"), TestContext.Current.CancellationToken);

        result.Error!.Code.ShouldBe("users.staff_role_with_firm");
        person.Account!.Role.ShouldBe(Role.Participant);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Instructor")]
    public async Task CreateUser_ShouldBeRejected_WhenStaffRoleIsGivenAFirm(string role)
    {
        Result<UserWithTemporaryPasswordDto> result = await Create(
            Admin, new CreateUserCommand("Kişi", "kisi@immib.org", role, FirmA));

        result.Error!.Code.ShouldBe("users.staff_role_with_firm");
        _participants.DidNotReceive().Add(Arg.Any<Participant>());
    }

    // A representative may manage Participant accounts of their own firm and nothing else: not a
    // fellow representative (resetting their password would hand the account over) and not a staff
    // record that still carries a firm from before the invariant existed.

    [Fact]
    public async Task ResetPassword_ShouldBeRejected_WhenFirmRepresentativeTargetsAnotherRepresentativeOfTheSameFirm()
    {
        Participant colleague = GivenUser(FirmA, Role.FirmRepresentative);

        Result<UserWithTemporaryPasswordDto> result = await Handlers().ResetPasswordAsync(
            FirmARepresentative, new ResetPasswordCommand(colleague.Id), TestContext.Current.CancellationToken);

        result.Error!.Code.ShouldBe("users.not_allowed");
        colleague.Account!.PasswordHash.ShouldBe("stored-hash");
    }

    [Fact]
    public async Task SetAccountStatus_ShouldBeRejected_WhenFirmRepresentativeTargetsAnotherRepresentativeOfTheSameFirm()
    {
        Participant colleague = GivenUser(FirmA, Role.FirmRepresentative);

        Result<UserDto> result = await Handlers().SetAccountStatusAsync(
            FirmARepresentative,
            new SetAccountStatusCommand(colleague.Id, Disabled: true),
            TestContext.Current.CancellationToken);

        result.Error!.Code.ShouldBe("users.not_allowed");
        colleague.Account!.IsDisabled.ShouldBeFalse();
    }

    [Fact]
    public async Task AssignRole_ShouldBeRejected_WhenFirmRepresentativeDemotesAnotherRepresentativeOfTheSameFirm()
    {
        Participant colleague = GivenUser(FirmA, Role.FirmRepresentative);

        Result<UserDto> result = await Handlers().AssignRoleAsync(
            FirmARepresentative,
            new AssignRoleCommand(colleague.Id, "Participant"),
            TestContext.Current.CancellationToken);

        result.Error!.Code.ShouldBe("users.not_allowed");
        colleague.Account!.Role.ShouldBe(Role.FirmRepresentative);
    }

    [Fact]
    public async Task ResetPassword_ShouldBeRejected_WhenFirmRepresentativeTargetsAnAdminThatStillCarriesTheirFirm()
    {
        UserAccount account = UserAccount.FromStorage(
            "eski-admin@firma.com", "stored-hash", Role.Admin, isDisabled: false,
            mustChangePassword: false, failedAttempts: 0, lockedUntilUtc: null, sessionVersion: 1);
        Participant legacyAdmin = Participant.FromStorage(Guid.CreateVersion7(), "Eski Kayıt", FirmA, account);
        _participants.FindByIdAsync(legacyAdmin.Id, Arg.Any<CancellationToken>())
            .Returns(Result<Participant?>.Success(legacyAdmin));

        Result<UserWithTemporaryPasswordDto> result = await Handlers().ResetPasswordAsync(
            FirmARepresentative, new ResetPasswordCommand(legacyAdmin.Id), TestContext.Current.CancellationToken);

        result.Error!.Code.ShouldBe("users.not_allowed");
        account.PasswordHash.ShouldBe("stored-hash");
    }

    [Fact]
    public async Task ResetPassword_ShouldSucceed_WhenFirmRepresentativeTargetsAParticipantOfTheirFirm()
    {
        Participant employee = GivenUser(FirmA, Role.Participant);

        Result<UserWithTemporaryPasswordDto> result = await Handlers().ResetPasswordAsync(
            FirmARepresentative, new ResetPasswordCommand(employee.Id), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task AssignRole_ShouldReportNotFound_WhenTargetDoesNotExist()
    {
        Guid missing = Guid.CreateVersion7();
        _participants.FindByIdAsync(missing, Arg.Any<CancellationToken>())
            .Returns(Result<Participant?>.Success(null));

        Result<UserDto> result = await Handlers().AssignRoleAsync(
            Admin, new AssignRoleCommand(missing, "Participant"), TestContext.Current.CancellationToken);

        result.Error!.Code.ShouldBe("users.not_found");
    }

    [Fact]
    public async Task SetAccountStatus_ShouldDisableAndRevokeEveryChain_WhenAdminDisables()
    {
        Participant person = GivenUser(FirmA, Role.Participant);
        RefreshChain chain = RefreshChain.Start(person.Id, "hash", person.Account!.SessionVersion, Now);
        _chains.ListActiveForParticipantAsync(person.Id, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<RefreshChain>>.Success([chain]));

        Result<UserDto> result = await Handlers().SetAccountStatusAsync(
            Admin, new SetAccountStatusCommand(person.Id, Disabled: true), TestContext.Current.CancellationToken);

        result.Value.IsDisabled.ShouldBeTrue();
        chain.IsRevoked.ShouldBeTrue();
    }

    [Fact]
    public async Task SetAccountStatus_ShouldReEnableAndClearLock_WhenAdminEnables()
    {
        Participant person = GivenUser(FirmA, Role.Participant);
        person.Account!.Disable();

        Result<UserDto> result = await Handlers().SetAccountStatusAsync(
            Admin, new SetAccountStatusCommand(person.Id, Disabled: false), TestContext.Current.CancellationToken);

        result.Value.IsDisabled.ShouldBeFalse();
        person.Account.LockedUntilUtc.ShouldBeNull();
    }

    [Fact]
    public async Task ResetPassword_ShouldIssueTemporaryPasswordAndRevokeSessions_WhenAdminResets()
    {
        Participant person = GivenUser(FirmA, Role.Participant);
        person.Account!.ChangePassword("own-hash");
        RefreshChain chain = RefreshChain.Start(person.Id, "hash", person.Account.SessionVersion, Now);
        _chains.ListActiveForParticipantAsync(person.Id, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<RefreshChain>>.Success([chain]));

        Result<UserWithTemporaryPasswordDto> result = await Handlers().ResetPasswordAsync(
            Admin, new ResetPasswordCommand(person.Id), TestContext.Current.CancellationToken);

        result.Value.TemporaryPassword.ShouldBe("Gecici-Sifre-1234");
        result.Value.User.MustChangePassword.ShouldBeTrue();
        person.Account.PasswordHash.ShouldBe("hash-of-Gecici-Sifre-1234");
        chain.IsRevoked.ShouldBeTrue();
    }

    [Fact]
    public async Task ResetPassword_ShouldBeRejected_WhenFirmRepresentativeTargetsAnotherFirm()
    {
        Participant person = GivenUser(FirmB, Role.Participant);

        Result<UserWithTemporaryPasswordDto> result = await Handlers().ResetPasswordAsync(
            FirmARepresentative, new ResetPasswordCommand(person.Id), TestContext.Current.CancellationToken);

        result.Error!.Code.ShouldBe("users.other_firm");
        person.Account!.PasswordHash.ShouldBe("stored-hash");
    }

    [Fact]
    public async Task ResetPassword_ShouldReportNoAccount_WhenPersonHasNone()
    {
        Participant person = Participant.CreateWithoutAccount("Hesapsız", FirmA);
        _participants.FindByIdAsync(person.Id, Arg.Any<CancellationToken>())
            .Returns(Result<Participant?>.Success(person));

        Result<UserWithTemporaryPasswordDto> result = await Handlers().ResetPasswordAsync(
            Admin, new ResetPasswordCommand(person.Id), TestContext.Current.CancellationToken);

        result.Error!.Code.ShouldBe("users.no_account");
    }

    // --- listing -----------------------------------------------------------------------------

    [Fact]
    public async Task List_ShouldAskForEveryFirm_WhenCallerIsAdmin()
    {
        GivenList([GivenUser(FirmA, Role.Participant), GivenUser(memberFirmId: null, Role.Instructor)]);

        Result<IReadOnlyList<UserDto>> result = await Handlers().ListAsync(
            Admin, new ListUsersQuery(), TestContext.Current.CancellationToken);

        result.Value.Count.ShouldBe(2);
        await _participants.Received(1).ListWithAccountsAsync(null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_ShouldScopeToCallerFirm_WhenCallerIsFirmRepresentative()
    {
        GivenList([GivenUser(FirmA, Role.Participant)]);

        await Handlers().ListAsync(
            FirmARepresentative, new ListUsersQuery(), TestContext.Current.CancellationToken);

        // The scope is a query parameter, not a filter applied afterwards (AC-20).
        await _participants.Received(1).ListWithAccountsAsync(FirmA, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_ShouldSkipPeopleWithoutAccounts_WhenProjecting()
    {
        GivenList([GivenUser(FirmA, Role.Participant), Participant.CreateWithoutAccount("Hesapsız", FirmA)]);

        Result<IReadOnlyList<UserDto>> result = await Handlers().ListAsync(
            Admin, new ListUsersQuery(), TestContext.Current.CancellationToken);

        result.Value.Count.ShouldBe(1);
    }

    [Fact]
    public async Task List_ShouldReturnEmpty_WhenNoUsersExist()
    {
        GivenList([]);

        Result<IReadOnlyList<UserDto>> result = await Handlers().ListAsync(
            Admin, new ListUsersQuery(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Instructor")]
    [InlineData("Participant")]
    public async Task List_ShouldBeRejected_WhenCallerMayNotManageUsers(string callerRole)
    {
        CallerContext caller = callerRole == "Instructor" ? Instructor : ParticipantCaller;

        Result<IReadOnlyList<UserDto>> result = await Handlers().ListAsync(
            caller, new ListUsersQuery(), TestContext.Current.CancellationToken);

        result.Error!.Code.ShouldBe("users.not_allowed");
        result.Error.Type.ShouldBe(ErrorType.Forbidden);
        await _participants.DidNotReceive().ListWithAccountsAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetAccountStatus_ShouldReportTheFailureAndSaveNothing_WhenSessionsCannotBeRead()
    {
        Participant person = GivenUser(FirmA, Role.Participant);
        Error unavailable = Error.Unavailable("database.unavailable", "x");
        _chains.ListActiveForParticipantAsync(person.Id, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<RefreshChain>>.Failure(unavailable));

        Result<UserDto> result = await Handlers().SetAccountStatusAsync(
            Admin, new SetAccountStatusCommand(person.Id, Disabled: true), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(unavailable);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResetPassword_ShouldReportTheFailureAndHandOutNoPassword_WhenSessionsCannotBeRead()
    {
        Participant person = GivenUser(FirmA, Role.Participant);
        Error unavailable = Error.Unavailable("database.unavailable", "x");
        _chains.ListActiveForParticipantAsync(person.Id, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<RefreshChain>>.Failure(unavailable));

        Result<UserWithTemporaryPasswordDto> result = await Handlers().ResetPasswordAsync(
            Admin, new ResetPasswordCommand(person.Id), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(unavailable);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // --- the system keeps an administrator --------------------------------------------------

    [Fact]
    public async Task SetAccountStatus_ShouldRefuse_WhenAnAdminDisablesTheirOwnAccount()
    {
        Participant self = GivenUser(memberFirmId: null, Role.Admin);
        CallerContext caller = new(self.Id, Role.Admin, null);

        Result<UserDto> result = await Handlers().SetAccountStatusAsync(
            caller, new SetAccountStatusCommand(self.Id, Disabled: true), TestContext.Current.CancellationToken);

        result.Error!.Code.ShouldBe("users.own_access");
        self.Account!.IsDisabled.ShouldBeFalse();
    }

    [Fact]
    public async Task AssignRole_ShouldRefuse_WhenAnAdminDemotesThemselves()
    {
        Participant self = GivenUser(memberFirmId: null, Role.Admin);
        CallerContext caller = new(self.Id, Role.Admin, null);

        Result<UserDto> result = await Handlers().AssignRoleAsync(
            caller, new AssignRoleCommand(self.Id, "Instructor"), TestContext.Current.CancellationToken);

        result.Error!.Code.ShouldBe("users.own_access");
        self.Account!.Role.ShouldBe(Role.Admin);
    }

    [Fact]
    public async Task SetAccountStatus_ShouldRefuse_WhenTheTargetIsTheLastActiveAdmin()
    {
        Participant lastAdmin = GivenUser(memberFirmId: null, Role.Admin);
        _participants.CountActiveAdminsAsync(Arg.Any<CancellationToken>()).Returns(Result<int>.Success(1));

        Result<UserDto> result = await Handlers().SetAccountStatusAsync(
            Admin, new SetAccountStatusCommand(lastAdmin.Id, Disabled: true), TestContext.Current.CancellationToken);

        result.Error!.Code.ShouldBe("users.last_admin");
        lastAdmin.Account!.IsDisabled.ShouldBeFalse();
    }

    [Fact]
    public async Task AssignRole_ShouldRefuse_WhenTheTargetIsTheLastActiveAdmin()
    {
        Participant lastAdmin = GivenUser(memberFirmId: null, Role.Admin);
        _participants.CountActiveAdminsAsync(Arg.Any<CancellationToken>()).Returns(Result<int>.Success(1));

        Result<UserDto> result = await Handlers().AssignRoleAsync(
            Admin, new AssignRoleCommand(lastAdmin.Id, "Participant"), TestContext.Current.CancellationToken);

        result.Error!.Code.ShouldBe("users.last_admin");
        lastAdmin.Account!.Role.ShouldBe(Role.Admin);
    }

    [Fact]
    public async Task SetAccountStatus_ShouldAllowDisablingAnAdmin_WhenAnotherActiveAdminRemains()
    {
        Participant otherAdmin = GivenUser(memberFirmId: null, Role.Admin);
        _participants.CountActiveAdminsAsync(Arg.Any<CancellationToken>()).Returns(Result<int>.Success(2));

        Result<UserDto> result = await Handlers().SetAccountStatusAsync(
            Admin, new SetAccountStatusCommand(otherAdmin.Id, Disabled: true), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        otherAdmin.Account!.IsDisabled.ShouldBeTrue();
    }

    [Fact]
    public async Task SetAccountStatus_ShouldCountTheAdminsInsideTheExclusiveSection_WhenDisablingAnAdmin()
    {
        Participant otherAdmin = GivenUser(memberFirmId: null, Role.Admin);
        bool insideSection = false;
        bool countedInside = false;
        _unitOfWork.RunExclusivelyAsync(
                ExclusiveSection.AdminAccess,
                Arg.Any<Func<CancellationToken, Task<Result<UserDto>>>>(),
                Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                insideSection = true;
                Result<UserDto> result = await call.Arg<Func<CancellationToken, Task<Result<UserDto>>>>()(
                    call.Arg<CancellationToken>());
                insideSection = false;

                return result;
            });
        _participants.CountActiveAdminsAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                countedInside = insideSection;

                return Result<int>.Success(2);
            });

        await Handlers().SetAccountStatusAsync(
            Admin, new SetAccountStatusCommand(otherAdmin.Id, Disabled: true), TestContext.Current.CancellationToken);

        // A count read outside the section could be overtaken by a parallel change (review R2-L-1).
        countedInside.ShouldBeTrue();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private UserAdministrationHandlers Handlers() => new(
        _participants, _chains, _hasher, _temporaryPasswords, _unitOfWork, _clock);

    private Task<Result<UserWithTemporaryPasswordDto>> Create(CallerContext caller, CreateUserCommand command) =>
        new CreateUserCommandHandler(_participants, _hasher, _temporaryPasswords, _unitOfWork)
            .HandleAsync(caller, command, TestContext.Current.CancellationToken);

    private Participant GivenUser(Guid? memberFirmId, Role role)
    {
        Participant person = Participant.CreateWithAccount(
            "Kayıtlı Kişi", memberFirmId, $"{Guid.NewGuid():N}@firma.com", "stored-hash", role);

        _participants.FindByIdAsync(person.Id, Arg.Any<CancellationToken>())
            .Returns(Result<Participant?>.Success(person));

        return person;
    }

    private void GivenList(IReadOnlyList<Participant> people) =>
        _participants.ListWithAccountsAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Participant>>.Success(people));
}
