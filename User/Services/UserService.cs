using UserService.Data;
using UserService.Models;
using Microsoft.EntityFrameworkCore;

namespace UserService.Services
{
    public interface IUserService
    {
        Task<User?> CreateUserAsync(User user);
        Task<User?> GetUserAsync(long userId);
        Task<User?> GetUserByUsernameAsync(string username);
        Task<List<User>> GetAllUsersAsync();
        Task<bool> UpdateUserAsync(User user);
        Task<bool> DeleteUserAsync(long userId);
    }

    public class UserService : IUserService
    {
        private readonly UserDbContext _context;
        private readonly ILogger<UserService> _logger;

        public UserService(UserDbContext context, ILogger<UserService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<User?> CreateUserAsync(User user)
        {
            try
            {
                // Проверяем уникальность username и email
                if (await _context.Users.AnyAsync(u => u.Username == user.Username))
                    throw new Exception("Username already exists");

                if (await _context.Users.AnyAsync(u => u.Email == user.Email))
                    throw new Exception("Email already exists");

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"User {user.Username} created with ID {user.Id}");
                return user;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating user");
                throw;
            }
        }

        public async Task<User?> GetUserAsync(long userId)
        {
            return await _context.Users.FindAsync(userId);
        }

        public async Task<User?> GetUserByUsernameAsync(string username)
        {
            return await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
        }

        public async Task<List<User>> GetAllUsersAsync()
        {
            return await _context.Users.ToListAsync();
        }

        public async Task<bool> UpdateUserAsync(User user)
        {
            try
            {
                var existingUser = await _context.Users.FindAsync(user.Id);
                if (existingUser == null) return false;

                // Обновляем поля
                existingUser.Username = user.Username;
                existingUser.FirstName = user.FirstName;
                existingUser.LastName = user.LastName;
                existingUser.Email = user.Email;
                existingUser.Phone = user.Phone;

                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating user {user.Id}");
                return false;
            }
        }

        public async Task<bool> DeleteUserAsync(long userId)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null) return false;

                _context.Users.Remove(user);
                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting user {userId}");
                return false;
            }
        }
    }
}