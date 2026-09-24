using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Portfolio.Infrastructure;

/// <summary>
/// Lets `dotnet ef migrations add` build the model without starting the app. Adding a
/// migration never connects, so the connection string is a placeholder; commands that
/// do connect (`database update`) take `--connection`.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PortfolioDbContext>
{
    public PortfolioDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PortfolioDbContext>();
        PortfolioDbContext.Configure(options, "Host=design-time-only");
        return new PortfolioDbContext(options.Options);
    }
}
