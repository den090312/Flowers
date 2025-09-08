using Microsoft.EntityFrameworkCore;
using Flowers.Models;

namespace Flowers.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }

        public DbSet<AuthUser> AuthUsers { get; set; }
    }
}