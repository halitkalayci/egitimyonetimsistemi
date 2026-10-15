using Application.Abstractions;
using Application.Common;
using Application.Identity;

using Domain.People;

using NSubstitute;

using Shouldly;

namespace Application.Tests.Identity;

public sealed class ChangePasswordCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    private readonly IParticipantRepository _participants = Substitute.For<IParticipantRepository>();
    private readonly IRefreshChainRepository _chains = Substitute.For<IRefreshChainRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ITokenIssuer _tokens = Substitute.For<ITokenIssuer>();
    private readonly IRefreshTokenGenerator _refresh = Substitute.For<IRefreshTokenGenerator>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public ChangePasswordCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result<int>.Success(1));
        _chains.ListActiveForParticipantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<RefreshChain>>.Success([]));
        _tokens.IssueAccessToken(Arg.Any<Guid>(), Arg.Any<Role>(), Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<bool>())
            .Returns(new AccessToken("fresh-access", Now.AddMinutes(15)));
        _refresh.Create().Returns(new RefreshTokenPair("fresh-secret", "fresh-hash"));
    }

    [Fact]
    public async Task HandleAsync_ShouldReplaceHashAndClearTemporaryFlag_WhenCurrentPasswordIsCorrect()
    {
        Participant person = GivenAccount();
        _hasher.Verify("gecici-sifre", "temp-hash").Returns(true);
        _hasher.Hash("yeni-sifre-1234").Returns("new-hash");

        Result<AuthenticationResultDto> result = await Handle(person.Id, "gecici-sifre", "yeni-sifre-1234");

        result.IsSuccess.ShouldBeTrue();
        person.Account!.PasswordHash.ShouldBe("new-hash");
        person.Account.MustChangePassword.ShouldBeFalse();
    }

    [Fact]
    public async Task HandleAsync_ShouldRevokeOldChainsAndStartAFreshSession_WhenPasswordChanges()
    {
        Participant person = GivenAccount();
        RefreshChain old = RefreshChain.Start(person.Id, "old-hash", person.Account!.SessionVersion, Now.AddHours(-1));
        _chains.ListActiveForParticipantAsync(person.Id, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<RefreshChain>>.Success([old]));
        RefreshChain? started = null;
        _chains.When(repository => repository.Add(Arg.Any<RefreshChain>()))
            .Do(call => started = call.Arg<RefreshChain>());
        _hasher.Verify("gecici-sifre", "temp-hash").Returns(true);
        _hasher.Hash(Arg.Any<string>()).Returns("new-hash");

        Result<AuthenticationResultDto> result = await Handle(person.Id, "gecici-sifre", "yeni-sifre-1234");

        // The device that changed the password continues; every other session is over.
        old.IsRevoked.ShouldBeTrue();
        started!.AccountSessionVersion.ShouldBe(person.Account.SessionVersion);
        started.CurrentTokenHash.ShouldBe("fresh-hash");
        result.Value.AccessToken.ShouldBe("fresh-access");
        result.Value.RefreshToken.ShouldBe("fresh-secret");
        result.Value.MustChangePassword.ShouldBeFalse();
        _tokens.Received(1).IssueAccessToken(
            person.Id, Role.Participant, person.MemberFirmId, person.Account.SessionVersion, false);
    }

    [Fact]
    public async Task HandleAsync_ShouldEndExistingSessions_WhenPasswordChanges()
    {
        Participant person = GivenAccount();
        int before = person.Account!.SessionVersion;
        _hasher.Verify("gecici-sifre", "temp-hash").Returns(true);
        _hasher.Hash(Arg.Any<string>()).Returns("new-hash");

        await Handle(person.Id, "gecici-sifre", "yeni-sifre-1234");

        person.Account.SessionVersion.ShouldBe(before + 1);
    }

    [Fact]
    public async Task HandleAsync_ShouldRequireCurrentPassword_WhenChanging()
    {
        Participant person = GivenAccount();
        _hasher.Verify("yanlis", "temp-hash").Returns(false);

        Result<AuthenticationResultDto> result = await Handle(person.Id, "yanlis", "yeni-sifre-1234");

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("auth.invalid_credentials");
        person.Account!.PasswordHash.ShouldBe("temp-hash");
    }

    [Fact]
    public async Task HandleAsync_ShouldCountAWrongCurrentPasswordTowardsTheLock()
    {
        Participant person = GivenAccount();
        _hasher.Verify("yanlis", "temp-hash").Returns(false);

        await Handle(person.Id, "yanlis", "yeni-sifre-1234");

        person.Account!.FailedAttempts.ShouldBe(1);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ShouldRefuseEvenTheRightCurrentPassword_WhenWrongGuessesLockedTheAccount()
    {
        Participant person = GivenAccount();
        _hasher.Verify("yanlis", "temp-hash").Returns(false);
        _hasher.Verify("gecici-sifre", "temp-hash").Returns(true);

        for (int attempt = 0; attempt < UserAccount.MaxFailedAttempts; attempt++)
        {
            await Handle(person.Id, "yanlis", "yeni-sifre-1234");
        }

        Result<AuthenticationResultDto> result = await Handle(person.Id, "gecici-sifre", "yeni-sifre-1234");

        result.Error!.Code.ShouldBe("auth.account_locked");
        person.Account!.PasswordHash.ShouldBe("temp-hash");
    }

    [Theory]
    [InlineData("kisa")]
    [InlineData("123456789")]
    public async Task HandleAsync_ShouldRejectWithPolicyViolation_WhenNewPasswordIsShorterThanTenCharacters(
        string newPassword)
    {
        Participant person = GivenAccount();
        _hasher.Verify("gecici-sifre", "temp-hash").Returns(true);

        Result<AuthenticationResultDto> result = await Handle(person.Id, "gecici-sifre", newPassword);

        result.Error!.Code.ShouldBe("auth.password_too_short");
        result.Error.Message.ShouldContain("10");
        _hasher.DidNotReceive().Hash(newPassword);
    }

    [Fact]
    public async Task HandleAsync_ShouldReject_WhenNewPasswordEqualsCurrent()
    {
        Participant person = GivenAccount();
        _hasher.Verify(Arg.Any<string>(), "temp-hash").Returns(true);

        Result<AuthenticationResultDto> result = await Handle(person.Id, "gecici-sifre", "gecici-sifre-ayni");

        result.Error!.Code.ShouldBe("auth.password_same_as_current");
    }

    [Fact]
    public async Task HandleAsync_ShouldReject_WhenPersonHasNoAccount()
    {
        Participant person = Participant.CreateWithoutAccount("Hesapsız", null);
        _participants.FindByIdAsync(person.Id, Arg.Any<CancellationToken>())
            .Returns(Result<Participant?>.Success(person));

        Result<AuthenticationResultDto> result = await Handle(person.Id, "x", "yeni-sifre-1234");

        result.Error!.Code.ShouldBe("users.no_account");
    }

    [Fact]
    public async Task HandleAsync_ShouldReject_WhenPersonDoesNotExist()
    {
        Guid missing = Guid.CreateVersion7();
        _participants.FindByIdAsync(missing, Arg.Any<CancellationToken>())
            .Returns(Result<Participant?>.Success(null));

        Result<AuthenticationResultDto> result = await Handle(missing, "x", "yeni-sifre-1234");

        result.Error!.Code.ShouldBe("users.no_account");
    }

    private Task<Result<AuthenticationResultDto>> Handle(Guid personId, string current, string next) =>
        new ChangePasswordCommandHandler(_participants, _chains, _hasher, _tokens, _refresh, _unitOfWork, _clock)
            .HandleAsync(
                new ChangePasswordCommand(personId, current, next),
                TestContext.Current.CancellationToken);

    private Participant GivenAccount()
    {
        Participant person = Participant.CreateWithAccount(
            "Ayşe Yılmaz", Guid.CreateVersion7(), "ayse@firma.com", "temp-hash", Role.Participant);

        _participants.FindByIdAsync(person.Id, Arg.Any<CancellationToken>())
            .Returns(Result<Participant?>.Success(person));

        return person;
    }
}
