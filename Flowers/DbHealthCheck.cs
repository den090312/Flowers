using Flowers.Data;

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Flowers
{
    public class DbHealthCheck : IHealthCheck
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public DbHealthCheck(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                await dbContext.Database.CanConnectAsync(cancellationToken);
                return HealthCheckResult.Healthy("Database is available");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Database is unavailable", ex);
            }
        }
    }
}
