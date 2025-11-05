using UserService.Data;
using UserService.Models;
using UserService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace UserService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly UserDbContext _context;
        private readonly IUserService _userService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthController> _logger;

        public AuthController(UserDbContext context, IUserService userService,
            IConfiguration configuration, ILogger<AuthController> logger)
        {
            _context = context;
            _userService = userService;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            try
            {
                // Проверяем существование пользователя
                if (await _context.AuthUsers.AnyAsync(u => u.Username == request.Username))
                    return BadRequest("Username already exists");

                if (await _context.Users.AnyAsync(u => u.Email == request.Email))
                    return BadRequest("Email already exists");

                // Создаем пользователя
                var user = new User
                {
                    Username = request.Username,
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    Email = request.Email,
                    Phone = request.Phone
                };

                var createdUser = await _userService.CreateUserAsync(user);
                if (createdUser == null)
                    return BadRequest("Failed to create user");

                // Создаем auth запись
                var authUser = new AuthUser
                {
                    UserId = createdUser.Id,
                    Username = request.Username,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password)
                };

                _context.AuthUsers.Add(authUser);
                await _context.SaveChangesAsync();

                // Генерируем JWT токен
                var token = GenerateJwtToken(createdUser.Id, createdUser.Username);

                return Ok(new AuthResponse
                {
                    Token = token,
                    UserId = createdUser.Id,
                    Username = createdUser.Username
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in register");
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            try
            {
                var authUser = await _context.AuthUsers
                    .Include(au => au.User)
                    .FirstOrDefaultAsync(u => u.Username == request.Username);

                if (authUser == null || !BCrypt.Net.BCrypt.Verify(request.Password, authUser.PasswordHash))
                    return Unauthorized("Invalid credentials");

                var token = GenerateJwtToken(authUser.UserId, authUser.Username);

                return Ok(new AuthResponse
                {
                    Token = token,
                    UserId = authUser.UserId,
                    Username = authUser.Username
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in login");
                return Unauthorized("Login failed");
            }
        }

        private string GenerateJwtToken(long userId, string username)
        {
            var jwtSettings = _configuration.GetSection("JwtSettings");
            var secretKey = jwtSettings["SecretKey"] ?? "default-secret-key-at-least-32-characters-long";
            var issuer = jwtSettings["Issuer"] ?? "user-service";
            var audience = jwtSettings["Audience"] ?? "flowers-client";

            var tokenHandler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
            var key = System.Text.Encoding.UTF8.GetBytes(secretKey);

            var tokenDescriptor = new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
            {
                Subject = new System.Security.Claims.ClaimsIdentity(new[]
                {
                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString()),
                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, username)
                }),
                Expires = DateTime.UtcNow.AddHours(24),
                Issuer = issuer,
                Audience = audience,
                SigningCredentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
                    new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(key),
                    Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
    }
}