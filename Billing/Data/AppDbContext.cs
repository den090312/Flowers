using Billing.Models;

using Microsoft.EntityFrameworkCore;

using System.Security.Principal;

namespace Billing.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
        
        public DbSet<Account> Accounts { get; set; }

        public DbSet<PaymentTransaction> PaymentTransactions { get; set; }
    }
}