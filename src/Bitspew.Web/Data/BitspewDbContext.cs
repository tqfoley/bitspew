using Bitspew.Core;
using Microsoft.EntityFrameworkCore;

namespace Bitspew.Web.Data;

public class BitspewDbContext(DbContextOptions<BitspewDbContext> options) : DbContext(options)
{
    public DbSet<MessageThread> Threads => Set<MessageThread>();
    public DbSet<Post> Posts => Set<Post>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MessageThread>(thread =>
        {
            thread.ToTable("threads");
            thread.HasKey(t => t.Id);
            thread.Property(t => t.Id).HasMaxLength(64);
            thread.Property(t => t.Title).HasMaxLength(200);
            thread.Property(t => t.Address).HasMaxLength(100);
            thread.HasMany(t => t.Posts).WithOne(p => p.Thread).HasForeignKey(p => p.ThreadId);
        });

        modelBuilder.Entity<Post>(post =>
        {
            post.ToTable("posts");
            post.HasKey(p => p.Id);
            post.Property(p => p.Id).HasMaxLength(64);
            post.Property(p => p.ThreadId).HasMaxLength(64);
            post.Property(p => p.Address).HasMaxLength(100);
            post.Property(p => p.Body).HasMaxLength(10_000);
            post.Property(p => p.SignatureBase64).HasMaxLength(120);
            post.HasIndex(p => p.ThreadId);
            post.HasIndex(p => p.Address);
        });
    }
}
