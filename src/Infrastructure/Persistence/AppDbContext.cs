using Domain.People;
using Domain.System;

using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<SystemInfoEntry> SystemInfoEntries => Set<SystemInfoEntry>();

    public DbSet<Participant> Participants => Set<Participant>();

    public DbSet<RefreshChain> RefreshChains => Set<RefreshChain>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Table and column names are snake_case (docs/conventions.md, Naming); the convention is
        // applied centrally by UseSnakeCaseNamingConvention in DependencyInjection.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
