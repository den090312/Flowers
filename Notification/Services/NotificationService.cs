using NotificationService.Data;
using NotificationService.Models;
using Microsoft.EntityFrameworkCore;

namespace NotificationService.Services
{
    public interface INotificationService
    {
        Task<bool> SendNotificationAsync(Notification notification);
        Task<List<Notification>> GetUserNotificationsAsync(long userId);
    }

    public class NotificationService : INotificationService
    {
        private readonly NotificationDbContext _context;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(NotificationDbContext context, ILogger<NotificationService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<bool> SendNotificationAsync(Notification notification)
        {
            try
            {
                _context.Notifications.Add(notification);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Notification sent to user {notification.UserId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending notification to user {notification.UserId}");
                return false;
            }
        }

        public async Task<List<Notification>> GetUserNotificationsAsync(long userId)
        {
            return await _context.Notifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();
        }
    }
}