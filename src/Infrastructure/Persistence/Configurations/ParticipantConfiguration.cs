using Domain.People;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

internal sealed class ParticipantConfiguration : IEntityTypeConfiguration<Participant>
{
    public void Configure(EntityTypeBuilder<Participant> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("participants");
        builder.HasKey(person => person.Id);

        builder.Property(person => person.Id).HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(person => person.FullName).HasMaxLength(Participant.MaxFullNameLength).IsRequired();
        builder.Property(person => person.MemberFirmId).HasColumnType("uuid");

        // The account is part of the person row (docs/domain.md, tek kayıt modeli): one record,
        // with the credential columns left null for someone who never logs in.
        builder.OwnsOne(person => person.Account, account =>
        {
            account.Property(value => value.Email).HasColumnName("email").HasMaxLength(UserAccount.MaxEmailLength).IsRequired();
            account.Property(value => value.PasswordHash).HasColumnName("password_hash").HasMaxLength(256).IsRequired();
            account.Property(value => value.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(40).IsRequired();
            account.Property(value => value.IsDisabled).HasColumnName("is_disabled").IsRequired();
            account.Property(value => value.MustChangePassword).HasColumnName("must_change_password").IsRequired();
            account.Property(value => value.FailedAttempts).HasColumnName("failed_attempts").IsRequired();
            account.Property(value => value.LockedUntilUtc).HasColumnName("locked_until").HasColumnType("timestamptz");
            account.Property(value => value.SessionVersion).HasColumnName("session_version").IsRequired();

            // One e-mail belongs to at most one account, system wide (docs/domain.md invariant).
            account.HasIndex(value => value.Email).IsUnique();
        });

        // Optimistic concurrency on PostgreSQL's xmin system column: two requests that read the
        // same row and both write it can no longer silently overwrite each other (lost update).
        builder.Property<uint>("RowVersion").HasColumnName("xmin").HasColumnType("xid").IsRowVersion();

        builder.Navigation(person => person.Account).IsRequired(false);
        builder.HasIndex(person => person.MemberFirmId);
    }
}
