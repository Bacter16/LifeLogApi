using Microsoft.EntityFrameworkCore;
using LifeLog.Domain.Entities;

namespace LifeLog.Infrastructure.Data;

public class LifeLogDbContext : DbContext
{
    public LifeLogDbContext(DbContextOptions<LifeLogDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Transcript> Transcripts => Set<Transcript>();
    public DbSet<GeminiJob> GeminiJobs => Set<GeminiJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Email).IsUnique();
        });

        modelBuilder.Entity<Transcript>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ClientEventId).IsUnique();
        });

        modelBuilder.Entity<GeminiJob>(entity =>
        {
            entity.HasKey(e => e.Id);
        });
    }
}
