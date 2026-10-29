using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StripePayments.Infrastructure.Persistence;

/// <summary>Used by `dotnet ef` only; the connection string is never used to connect.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=stripe_payments;Username=postgres;Password=postgres")
            .Options);
}
