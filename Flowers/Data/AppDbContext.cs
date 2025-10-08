using Microsoft.EntityFrameworkCore;
using Flowers.Models;

namespace Flowers.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
        public DbSet<User> Users { get; set; }
        public DbSet<AuthUser> AuthUsers { get; set; }
        public DbSet<Account> Accounts { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<CompletedSaga> CompletedSagas { get; set; }
        public DbSet<PaymentTransaction> PaymentTransactions { get; set; }
    }
}