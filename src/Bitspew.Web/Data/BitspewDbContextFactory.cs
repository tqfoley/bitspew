using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bitspew.Web.Data;

/// <summary>
/// Lets `dotnet ef migrations add` build the model without a real Neon connection string;
/// migration generation never connects to the database.
/// </summary>
public class BitspewDbContextFactory : IDesignTimeDbContextFactory<BitspewDbContext>
{
    public BitspewDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BitspewDbContext>()
            .UseNpgsql("Host=localhost;Database=bitspew-design-time")
            .Options;
        return new BitspewDbContext(options);
    }
}
