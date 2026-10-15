using Domain.People;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

internal sealed class RefreshChainConfiguration : IEntityTypeConfiguration<RefreshChain>
{
    public void Configure(EntityTypeBuilder<RefreshChain> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("refresh_chains");
        builder.HasKey(chain => chain.Id);

        builder.Property(chain => chain.Id).HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(chain => chain.ParticipantId).HasColumnType("uuid").IsRequired();
        builder.Property(chain => chain.CurrentTokenHash).HasMaxLength(128).IsRequired();
        builder.Property(chain => chain.AccountSessionVersion).IsRequired();
        builder.Property(chain => chain.StartedAtUtc).HasColumnType("timestamptz").IsRequired();
        builder.Property(chain => chain.ExpiresAtUtc).HasColumnType("timestamptz").IsRequired();
        builder.Property(chain => chain.RevokedAtUtc).HasColumnType("timestamptz");

        // The spent links are a primitive collection on the row: they are only ever read together
        // with their chain, and the reuse lookup needs a containment check, not a join.
        builder.PrimitiveCollection<IReadOnlyList<string>>("RotatedTokenHashes")
            .HasColumnName("rotated_token_hashes")
            .HasField("_rotatedTokenHashes")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .IsRequired();

        // Optimistic concurrency on PostgreSQL's xmin system column: two requests that read the
        // same row and both write it can no longer silently overwrite each other (lost update).
        builder.Property<uint>("RowVersion").HasColumnName("xmin").HasColumnType("xid").IsRowVersion();

        builder.HasIndex(chain => chain.CurrentTokenHash);
        builder.HasIndex(chain => chain.ParticipantId);

        builder.HasOne<Participant>()
            .WithMany()
            .HasForeignKey(chain => chain.ParticipantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
