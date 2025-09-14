using Flowers;
using Flowers.Data;
using Flowers.Models;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;

using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;

using Prometheus;

using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors();
builder.Services.AddRouting();

// Добавляем после builder.Services.AddControllers();
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["SecretKey"] ?? "default-secret-key-at-least-32-characters-long";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization();

// Добавляем хеширование паролей
builder.Services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();

// Загрузка конфигурации из /app/config
if (File.Exists("/app/config/appsettings.json"))
{
    builder.Configuration.AddJsonFile("/app/config/appsettings.json", optional: false, reloadOnChange: true);
}

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Configure DbContext with PostgreSQL
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString, options =>
    {
        options.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorCodesToAdd: null);
    });
    options.LogTo(Console.WriteLine, LogLevel.Information);
});

// Add health checks
builder.Services.AddHealthChecks()
    .AddCheck<DbHealthCheck>("Database")
    .AddCheck("Self", () => HealthCheckResult.Healthy("API is healthy"));

// Регистрируем наш кастомный health check как Scoped
builder.Services.AddScoped<DbHealthCheck>();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.SuppressModelStateInvalidFilter = true; // Отключение автоматической валидации
});

var app = builder.Build();

app.UseCors(builder => builder.AllowAnyOrigin());

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//app.UseHttpsRedirection();
//app.UseAuthorization();

// Корневой endpoint
app.MapGet("/", () => "Flowers API is running");

#region АУТЕНТИФИКАЦИЯ
var authGroup = app.MapGroup("/auth");

// Регистрация
authGroup.MapPost("/register", async (RegisterRequest request, AppDbContext context, IPasswordHasher passwordHasher, IConfiguration configuration) =>
{
    // Проверяем, существует ли пользователь
    if (await context.AuthUsers.AnyAsync(u => u.Username == request.Username))
    {
        return Results.BadRequest("Username already exists");
    }

    if (await context.Users.AnyAsync(u => u.Email == request.Email))
    {
        return Results.BadRequest("Email already exists");
    }

    // Создаем пользователя
    var user = new User
    {
        Username = request.Username,
        FirstName = request.FirstName,
        LastName = request.LastName,
        Email = request.Email,
        Phone = request.Phone
    };

    context.Users.Add(user);
    await context.SaveChangesAsync();

    // Создаем запись аутентификации
    var authUser = new AuthUser
    {
        UserId = user.Id,
        Username = request.Username,
        PasswordHash = passwordHasher.HashPassword(request.Password)
    };

    context.AuthUsers.Add(authUser);
    await context.SaveChangesAsync();

    // Генерируем токен
    var token = Jwt.GenerateJwtToken(user.Id, user.Username, configuration);

    return Results.Ok(new AuthResponse
    {
        Token = token,
        UserId = user.Id,
        Username = user.Username
    });
});

// Логин
authGroup.MapPost("/login", async (LoginRequest request, AppDbContext context, IPasswordHasher passwordHasher, IConfiguration configuration) =>
{
    var authUser = await context.AuthUsers
        .Include(au => au.User)
        .FirstOrDefaultAsync(u => u.Username == request.Username);

    if (authUser == null || !passwordHasher.VerifyPassword(request.Password, authUser.PasswordHash))
    {
        return Results.Unauthorized();
    }

    var token = Jwt.GenerateJwtToken(authUser.UserId, authUser.Username, configuration);

    return Results.Ok(new AuthResponse
    {
        Token = token,
        UserId = authUser.UserId,
        Username = authUser.Username
    });
});
#endregion

#region ПОЛЬЗОВАТЕЛИ

var userGroup = app.MapGroup("/user");

// Получение всех пользователей
userGroup.MapGet("/", async (AppDbContext context) =>
{
    var users = await context.Users.ToListAsync();
    return Results.Ok(users);
});

// Создание пользователя
userGroup.MapPost("/", async (User user, AppDbContext context) =>
{
    context.Users.Add(user);
    await context.SaveChangesAsync();
    return Results.Created($"/user/{user.Id}", user);
});

// Получение пользователя по ID с проверкой авторизации
userGroup.MapGet("/{userId:long}", async (long userId, AppDbContext context, HttpContext httpContext) =>
{
    var currentUserId = long.Parse(httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

    if (currentUserId != userId)
    {
        return Results.Forbid();
    }

    var user = await context.Users.FindAsync(userId);
    return user is null ? Results.NotFound() : Results.Ok(user);
}).RequireAuthorization();

// Обновление пользователя с проверкой авторизации
userGroup.MapPut("/{userId:long}", async (long userId, User updatedUser, AppDbContext context, HttpContext httpContext) =>
{
    var currentUserId = long.Parse(httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

    if (currentUserId != userId)
    {
        return Results.Forbid();
    }

    var user = await context.Users.FindAsync(userId);
    if (user is null) return Results.NotFound();

    if (updatedUser.Username != null) user.Username = updatedUser.Username;
    if (updatedUser.FirstName != null) user.FirstName = updatedUser.FirstName;
    if (updatedUser.LastName != null) user.LastName = updatedUser.LastName;
    if (updatedUser.Email != null) user.Email = updatedUser.Email;
    if (updatedUser.Phone != null) user.Phone = updatedUser.Phone;

    await context.SaveChangesAsync();
    return Results.Ok(user);
}).RequireAuthorization();

// Получение профиля текущего пользователя
userGroup.MapGet("/profile", async (AppDbContext context, HttpContext httpContext) =>
{
    var currentUserId = long.Parse(httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
    var user = await context.Users.FindAsync(currentUserId);
    return user is null ? Results.NotFound() : Results.Ok(user);
}).RequireAuthorization();

// Обновление профиля текущего пользователя
userGroup.MapPut("/profile", async (User updatedUser, AppDbContext context, HttpContext httpContext) =>
{
    var currentUserId = long.Parse(httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
    var user = await context.Users.FindAsync(currentUserId);
    if (user is null) return Results.NotFound();

    if (updatedUser.Username != null) user.Username = updatedUser.Username;
    if (updatedUser.FirstName != null) user.FirstName = updatedUser.FirstName;
    if (updatedUser.LastName != null) user.LastName = updatedUser.LastName;
    if (updatedUser.Email != null) user.Email = updatedUser.Email;
    if (updatedUser.Phone != null) user.Phone = updatedUser.Phone;

    await context.SaveChangesAsync();
    return Results.Ok(user);
}).RequireAuthorization();

// Удаление пользователя
userGroup.MapDelete("/{userId:long}", async (long userId, AppDbContext context) =>
{
    var user = await context.Users.FindAsync(userId);
    if (user is null) return Results.NotFound();

    context.Users.Remove(user);
    await context.SaveChangesAsync();
    return Results.NoContent();
});
#endregion

// Health checks endpoints
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/detailed", new HealthCheckOptions()
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var response = new
        {
            Status = report.Status.ToString(),
            Checks = report.Entries.Select(e => new
            {
                Component = e.Key,
                Status = e.Value.Status.ToString(),
                Description = e.Value.Description
            }),
            Duration = report.TotalDuration
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
});

app.Use(async (context, next) =>
{
    Console.WriteLine($"Received {context.Request.Method} {context.Request.Path}");
    await next();
});

app.UseHttpMetrics();
app.MapMetrics();
app.Run();