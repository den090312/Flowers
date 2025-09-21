#region ЮЗИНГИ
using Flowers;
using Flowers.Data;
using Flowers.Models;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
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
#endregion

#region БАЗОВАЯ ИНИЦИАЛИЗАЦИЯ
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
app.UseAuthentication();
app.UseAuthorization();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Корневой endpoint
app.MapGet("/", () => "Flowers API is running");
#endregion

#region АУТЕНТИФИКАЦИЯ
var authGroup = app.MapGroup("/auth");

// Регистрация
authGroup.MapPost("/register", async (RegisterRequest request, AppDbContext context,
    IPasswordHasher passwordHasher, IConfiguration configuration) =>
{
    if (await context.AuthUsers.AnyAsync(u => u.Username == request.Username))
        return Results.BadRequest("Username already exists");

    if (await context.Users.AnyAsync(u => u.Email == request.Email))
        return Results.BadRequest("Email already exists");

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

    var authUser = new AuthUser
    {
        UserId = user.Id,
        Username = request.Username,
        PasswordHash = passwordHasher.HashPassword(request.Password)
    };

    context.AuthUsers.Add(authUser);
    await context.SaveChangesAsync();

    var account = new Account
    {
        UserId = user.Id,
        Balance = 0
    };

    context.Accounts.Add(account);
    await context.SaveChangesAsync();

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
        return Results.Unauthorized();

    var token = Jwt.GenerateJwtToken(authUser.UserId, authUser.Username, configuration);

    return Results.Ok(new AuthResponse
    {
        Token = token,
        UserId = authUser.UserId,
        Username = authUser.Username
    });
});
#endregion

#region СЕРВИС БИЛЛИНГ
var billingGroup = app.MapGroup("/bill");

// Пополнение счета
billingGroup.MapPost("/deposit", async (DepositRequest request, AppDbContext context) =>
{
    var account = await context.Accounts.FirstOrDefaultAsync(a => a.UserId == request.UserId);
    if (account == null) return Results.NotFound("Account not found");

    account.Balance += request.Amount;
    await context.SaveChangesAsync();

    return Results.Ok(new { NewBalance = account.Balance });
}).RequireAuthorization();

// Снятие денег
billingGroup.MapPost("/withdraw", async (WithdrawRequest request, AppDbContext context) =>
{
    var account = await context.Accounts.FirstOrDefaultAsync(a => a.UserId == request.UserId);
    if (account == null) return Results.NotFound("Account not found");

    if (account.Balance < request.Amount)
        return Results.BadRequest("Insufficient funds");

    account.Balance -= request.Amount;
    await context.SaveChangesAsync();

    return Results.Ok(new { NewBalance = account.Balance });
}).RequireAuthorization();

// Получение баланса
billingGroup.MapGet("/balance/{userId:long}", async (long userId, AppDbContext context) =>
{
    var account = await context.Accounts.FirstOrDefaultAsync(a => a.UserId == userId);
    if (account == null) return Results.NotFound("Account not found");

    return Results.Ok(new { Balance = account.Balance });
}).RequireAuthorization();
#endregion

#region СЕРВИС ЗАКАЗОВ
var ordersGroup = app.MapGroup("/orders");

// Создание заказа
ordersGroup.MapPost("/", async (CreateOrderRequest request, AppDbContext context, HttpContext httpContext) =>
{
    var userId = long.Parse(httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
    if (userId == default) return Results.NotFound("User not found");

    var user = await context.Users.FindAsync(userId);
    if (user == default) return Results.NotFound("User not found");

    // Проверяем баланс
    var account = await context.Accounts.FirstOrDefaultAsync(a => a.UserId == userId);
    if (account == null) return Results.NotFound("Account not found");

    if (account.Balance < request.Amount)
    {
        // Отправляем уведомление
        context.Notifications.Add(new Notification
        {
            UserId = userId,
            Email = user.Email,
            Message = "Order create failed. Insufficient funds",
            CreatedAt = DateTime.UtcNow,
            Status = "Not sent"
        });
        await context.SaveChangesAsync();

        return Results.BadRequest("Insufficient funds");
    }

    // Снимаем деньги
    account.Balance -= request.Amount;

    // Создаем заказ
    var order = new Order
    {
        UserId = userId,
        Amount = request.Amount,
        Status = "Completed",
        CreatedAt = DateTime.UtcNow
    };

    context.Orders.Add(order);

    // Отправляем уведомление
    context.Notifications.Add(new Notification
    {
        UserId = userId,
        Email = user.Email,
        Message = $"Your order #{order.Id} for ${request.Amount} has been completed successfully!",
        CreatedAt = DateTime.UtcNow,
        Status = "Sent"
    });
    await context.SaveChangesAsync();

    return Results.Ok(new { OrderId = order.Id, Status = order.Status });
}).RequireAuthorization();

// Получение заказов пользователя
ordersGroup.MapGet("/", async (AppDbContext context, HttpContext httpContext) =>
{
    var userId = long.Parse(httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
    var orders = await context.Orders
        .Where(o => o.UserId == userId)
        .OrderByDescending(o => o.CreatedAt)
        .ToListAsync();

    return Results.Ok(orders);
}).RequireAuthorization();
#endregion

#region СЕРВИС УВЕДОМЛЕНИЙ
var notifGroup = app.MapGroup("/notif");

// Получение уведомлений пользователя
notifGroup.MapGet("/notifications/{userId:long}", async (long userId, AppDbContext context) =>
{
    var notifications = await context.Notifications
        .Where(n => n.UserId == userId)
        .OrderByDescending(n => n.CreatedAt)
        .ToListAsync();

    return Results.Ok(notifications);
}).RequireAuthorization();
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

#region ПРОВЕРКА ЗДОРОВЬЯ
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
#endregion

#region ОБРАБОТКА ОШИБОК
app.Use(async (context, next) =>
{
    Console.WriteLine($"Received {context.Request.Method} {context.Request.Path}");
    await next();
});
#endregion

#region ЗАПУСК
app.UseExceptionHandler(a => a.Run(async context =>
{
    var exceptionHandlerPathFeature = context.Features.Get<IExceptionHandlerPathFeature>();
    var exception = exceptionHandlerPathFeature.Error;

    Console.WriteLine($"Unhandled exception: {exception.Message}");
    Console.WriteLine($"Stack trace: {exception.StackTrace}");

    context.Response.StatusCode = 500;
    await context.Response.WriteAsync("An unexpected error occurred. Please try again later.");
}));

app.UseHttpMetrics();
app.MapMetrics();
app.Run();
#endregion
