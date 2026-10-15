using Application.Abstractions;
using Application.Common;
using Application.Identity;

using Domain.People;

using NSubstitute;

using Shouldly;

namespace Application.Tests.Identity;

public sealed class LoginCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private readonly IParticipantRepository _participants = Substitute.For<IParticipantRepository>();
    private readonly IRefreshChainRepository _chains = Substitute.For<IRefreshChainRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ITokenIssuer _tokens = Substitute.For<ITokenIssuer>();
    private readonly IRefreshTokenGenerator _refresh = Substitute.For<IRefreshTokenGenerator>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public LoginCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result<int>.Success(1));
        _participants.RegisterFailedLoginAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Result<int>.Success(1));
        _tokens.IssueAccessToken(Arg.Any<Guid>(), Arg.Any<Role>(), Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<bool>())
            .Returns(new AccessToken("access-token", Now.AddMinutes(15)));
        _refresh.Create().Returns(new RefreshTokenPair("refresh-secret", "refresh-hash"));
    }

    [Fact]
    public async Task HandleAsync_ShouldIssueSessionWithRole_WhenCredentialsAreValid()
    {
        Participant person = GivenAccount("ayse@firma.com", Role.Instructor, changed: true);
        GivenPasswordMatches(true);

        Result<AuthenticationResultDto> result = await Handle("Ayse@Firma.com", "dogru-sifre-123");

        result.IsSuccess.ShouldBeTrue();
        result.Value.AccessToken.ShouldBe("access-token");
        result.Value.RefreshToken.ShouldBe("refresh-secret");
        result.Value.Role.ShouldBe("Instructor");
        result.Value.MustChangePassword.ShouldBeFalse();
        result.Value.RefreshTokenExpiresAtUtc.ShouldBe(Now.AddDays(7));
        _chains.Received(1).Add(Arg.Is<RefreshChain>(chain => chain.ParticipantId == person.Id));
    }

    [Fact]
    public async Task HandleAsync_ShouldLookUpTheNormalisedEmail_WhenCallerUsesMixedCase()
    {
        GivenAccount("ayse@firma.com", Role.Participant, changed: true);
        GivenPasswordMatches(true);

        await Handle("  AYSE@Firma.COM ", "dogru-sifre-123");

        await _participants.Received(1).FindByEmailAsync("ayse@firma.com", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ShouldReportMustChangePassword_WhenAccountCarriesTemporaryPassword()
    {
        GivenAccount("ayse@firma.com", Role.Participant, changed: false);
        GivenPasswordMatches(true);

        Result<AuthenticationResultDto> result = await Handle("ayse@firma.com", "gecici-sifre");

        // The session is real on purpose: the user must be authenticated to change the password.
        result.IsSuccess.ShouldBeTrue();
        result.Value.MustChangePassword.ShouldBeTrue();
        _tokens.Received(1).IssueAccessToken(Arg.Any<Guid>(), Arg.Any<Role>(), Arg.Any<Guid?>(), Arg.Any<int>(), true);
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnInvalidCredentials_WhenPasswordIsWrong()
    {
        GivenAccount("ayse@firma.com", Role.Participant, changed: true);
        GivenPasswordMatches(false);

        Result<AuthenticationResultDto> result = await Handle("ayse@firma.com", "yanlis");

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("auth.invalid_credentials");
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnTheSameError_WhenEmailIsUnknownAndWhenPasswordIsWrong()
    {
        GivenAccount("ayse@firma.com", Role.Participant, changed: true);
        GivenPasswordMatches(false);
        Result<AuthenticationResultDto> wrongPassword = await Handle("ayse@firma.com", "yanlis");

        _participants.FindByEmailAsync("yok@firma.com", Arg.Any<CancellationToken>())
            .Returns(Result<Participant?>.Success(null));
        Result<AuthenticationResultDto> unknownEmail = await Handle("yok@firma.com", "yanlis");

        // spec 0002, AC-2: the two answers must be indistinguishable.
        unknownEmail.Error.ShouldBe(wrongPassword.Error);
    }

    [Fact]
    public async Task HandleAsync_ShouldInvokePasswordVerification_EvenWhenUserDoesNotExist()
    {
        _participants.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Participant?>.Success(null));
        _hasher.DecoyHash.Returns("decoy-hash");

        await Handle("yok@firma.com", "bir-sifre");

        // AC-29: without this the response time would reveal whether the account exists. The
        // hasher's own decoy is used, so the cost follows the real parameters.
        _hasher.Received(1).Verify("bir-sifre", "decoy-hash");
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnInvalidCredentials_WhenPersonHasNoAccount()
    {
        _participants.FindByEmailAsync("hesapsiz@firma.com", Arg.Any<CancellationToken>())
            .Returns(Result<Participant?>.Success(
                Participant.CreateWithoutAccount("Hesapsız Kişi", Guid.CreateVersion7())));

        Result<AuthenticationResultDto> result = await Handle("hesapsiz@firma.com", "bir-sifre");

        result.Error!.Code.ShouldBe("auth.invalid_credentials");
        _hasher.Received(1).Verify("bir-sifre", Arg.Any<string>());
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnAccountLocked_WhenAccountIsLocked()
    {
        Participant person = GivenAccount("ayse@firma.com", Role.Participant, changed: true);
        GivenPasswordMatches(false);

        for (int attempt = 1; attempt <= UserAccount.MaxFailedAttempts; attempt++)
        {
            await Handle("ayse@firma.com", "yanlis");
        }

        GivenPasswordMatches(true);
        Result<AuthenticationResultDto> result = await Handle("ayse@firma.com", "dogru-sifre-123");

        result.Error!.Code.ShouldBe("auth.account_locked");
        person.Account!.IsLockedAt(Now).ShouldBeTrue();
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnAccountDisabled_WhenAccountIsDisabled()
    {
        Participant person = GivenAccount("ayse@firma.com", Role.Participant, changed: true);
        person.Account!.Disable();
        GivenPasswordMatches(true);

        Result<AuthenticationResultDto> result = await Handle("ayse@firma.com", "dogru-sifre-123");

        result.Error!.Code.ShouldBe("auth.account_disabled");
    }

    [Fact]
    public async Task HandleAsync_ShouldCountTheFailedAttemptAtomically_WhenLoginFails()
    {
        Participant person = GivenAccount("ayse@firma.com", Role.Participant, changed: true);
        GivenPasswordMatches(false);

        await Handle("ayse@firma.com", "yanlis");

        // Without a write the lock counter would reset on every request. The count is made by the
        // database in one statement; writing back the value read here would lose parallel attempts
        // and refuse some of them with a conflict nobody gets for an unknown e-mail (AC-2, AC-3).
        await _participants.Received(1).RegisterFailedLoginAsync(person.Id, Now, Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ShouldReportUnavailable_WhenTheFailedAttemptCannotBeCounted()
    {
        GivenAccount("ayse@firma.com", Role.Participant, changed: true);
        GivenPasswordMatches(false);
        Error unavailable = Error.Unavailable("database.unavailable", "Veritabanına şu anda ulaşılamıyor.");
        _participants.RegisterFailedLoginAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Result<int>.Failure(unavailable));

        Result<AuthenticationResultDto> result = await Handle("ayse@firma.com", "yanlis");

        result.Error.ShouldBe(unavailable);
    }

    [Fact]
    public async Task HandleAsync_ShouldNotStartAChain_WhenLoginFails()
    {
        GivenAccount("ayse@firma.com", Role.Participant, changed: true);
        GivenPasswordMatches(false);

        await Handle("ayse@firma.com", "yanlis");

        _chains.DidNotReceive().Add(Arg.Any<RefreshChain>());
    }

    [Fact]
    public async Task HandleAsync_ShouldReplayOnFreshState_WhenASuccessfulLoginLosesARaceOnTheRow()
    {
        Participant stale = GivenAccount("yaris@immib.org", Role.Participant, changed: true);
        Participant fresh = Participant.FromStorage(stale.Id, stale.FullName, stale.MemberFirmId,
            UserAccount.FromStorage("yaris@immib.org", "stored-hash", Role.Participant, isDisabled: false,
                mustChangePassword: false, failedAttempts: 1, lockedUntilUtc: null, sessionVersion: 2));
        _participants.FindByEmailAsync("yaris@immib.org", Arg.Any<CancellationToken>())
            .Returns(Result<Participant?>.Success(stale), Result<Participant?>.Success(fresh));
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<int>.Failure(PersistenceErrors.ConcurrentUpdate), Result<int>.Success(1));
        GivenPasswordMatches(true);

        Result<AuthenticationResultDto> result = await Handle("yaris@immib.org", "dogru-sifre-123");

        // A parallel wrong password moved the row between our read and our write; the login is
        // decided again on what is stored now instead of failing with a conflict.
        _unitOfWork.Received(1).DiscardChanges();
        result.IsSuccess.ShouldBeTrue();
        fresh.Account!.FailedAttempts.ShouldBe(0);
    }

    private Task<Result<AuthenticationResultDto>> Handle(string email, string password) =>
        new LoginCommandHandler(_participants, _chains, _hasher, _tokens, _refresh, _unitOfWork, _clock)
            .HandleAsync(new LoginCommand(email, password), TestContext.Current.CancellationToken);

    private Participant GivenAccount(string email, Role role, bool changed)
    {
        // Staff roles carry no member firm (Participant.RoleMayBelongToFirm).
        Participant person = Participant.CreateWithAccount(
            "Ayşe Yılmaz",
            Participant.RoleMayBelongToFirm(role) ? Guid.CreateVersion7() : null,
            email,
            "stored-hash",
            role);

        if (changed)
        {
            person.Account!.ChangePassword("stored-hash");
        }

        _participants.FindByEmailAsync(email, Arg.Any<CancellationToken>())
            .Returns(Result<Participant?>.Success(person));

        return person;
    }

    private void GivenPasswordMatches(bool matches) =>
        _hasher.Verify(Arg.Any<string>(), "stored-hash").Returns(matches);
}
