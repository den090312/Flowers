using Billing.Models;

using Microsoft.EntityFrameworkCore;

namespace Billing.Data
{
    public class BillingDbContext : DbContext
    {
        public BillingDbContext(DbContextOptions<BillingDbContext> options) : base(options) { }

        public DbSet<PaymentTransaction> PaymentTransactions { get; set; }
        public DbSet<Account> Accounts { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Account>()
                .HasIndex(a => a.UserId)
                .IsUnique();

            modelBuilder.Entity<PaymentTransaction>()
                .HasIndex(p => p.IdempotencyKey)
                .IsUnique();

            base.OnModelCreating(modelBuilder);
        }
    }
}