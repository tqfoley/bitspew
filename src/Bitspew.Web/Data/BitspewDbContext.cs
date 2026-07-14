using Bitspew.Core;
using Microsoft.EntityFrameworkCore;

namespace Bitspew.Web.Data;

public class BitspewDbContext(DbContextOptions<BitspewDbContext> options) : DbContext(options)
{
    public DbSet<Message> Messages => Set<Message>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Message>(message =>
        {
            message.ToTable("messages");
            message.HasKey(m => m.Id);
            message.Property(m => m.Body).HasMaxLength(10_000);
            message.HasIndex(m => m.CreatedAt);
        });
    }
}
