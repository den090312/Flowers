using Flowers.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Flowers.Models;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

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

// User endpoints group
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

// Получение пользователя по ID
userGroup.MapGet("/{userId:long}", async (long userId, AppDbContext context) =>
{
    var user = await context.Users.FindAsync(userId);
    return user is null ? Results.NotFound() : Results.Ok(user);
});

// Обновление пользователя
userGroup.MapPut("/{userId:long}", async (long userId, User updatedUser, AppDbContext context) =>
{
    var user = await context.Users.FindAsync(userId);
    if (user is null) return Results.NotFound();

    if (updatedUser.Username != null) user.Username = updatedUser.Username;
    if (updatedUser.FirstName != null) user.FirstName = updatedUser.FirstName;
    if (updatedUser.LastName != null) user.LastName = updatedUser.LastName;
    if (updatedUser.Email != null) user.Email = updatedUser.Email;
    if (updatedUser.Phone != null) user.Phone = updatedUser.Phone;

    await context.SaveChangesAsync();
    return Results.Ok(user);
});

// Удаление пользователя
userGroup.MapDelete("/{userId:long}", async (long userId, AppDbContext context) =>
{
    var user = await context.Users.FindAsync(userId);
    if (user is null) return Results.NotFound();

    context.Users.Remove(user);
    await context.SaveChangesAsync();
    return Results.NoContent();
});

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

// Кастомная проверка здоровья БД
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