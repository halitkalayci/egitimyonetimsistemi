using Domain.System;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

internal sealed class SystemInfoEntryConfiguration : IEntityTypeConfiguration<SystemInfoEntry>
{
    public void Configure(EntityTypeBuilder<SystemInfoEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("system_info_entries");

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Id)
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(entry => entry.Key)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(entry => entry.Value)
            .HasMaxLength(500)
            .IsRequired();

        // timestamptz: every instant is stored in UTC (docs/conventions.md, Data rules).
        builder.Property(entry => entry.RecordedAt)
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.HasIndex(entry => entry.Key)
            .IsUnique();
    }
}
