#region ЮЗИНГИ
using Billing.Data;
using Billing.Interfaces;
using Billing.Models;
using Billing.Services;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;

using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;

using Prometheus;

using System;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using static System.Runtime.InteropServices.JavaScript.JSType;
#endregion

#region БАЗОВАЯ ИНИЦИАЛИЗАЦИЯ
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors();
builder.Services.AddRouting();

// Добавляем после builder.Services.AddControllers();
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["SecretKey"] ?? "default-secret-key-at-least-32-characters-long-Billing";

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
app.MapGet("/", () => "Billing API is running");
#endregion

#region СЕРВИС БИЛЛИНГ
var billingGroup = app.MapGroup("/bill");

// Пополнение счета
billingGroup.MapPost("/deposit", async (DepositRequest request, IBillingService service) =>
{
    try
    {
        var result = await service.DepositAsync(request);

        if (result)
        {
            var newBalance = await service.GetBalanceAsync(request.UserId);

            return Results.Ok(new { newBalance });
        }

        return Results.BadRequest(new { error = "Deposit failed" });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization()
.WithName("Deposit");

// Получение баланса
billingGroup.MapGet("/balance/{userId}", async (long userId, IBillingService service) =>
{
    try
    {
        var balance = await service.GetBalanceAsync(userId);

        return Results.Ok(new { balance });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization()
.WithName("GetBalance");

// Снятие денег
billingGroup.MapPost("/withdraw", async (WithdrawRequest request, IBillingService service) =>
{
    try
    {
        var result = await service.WithdrawAsync(request);

        if (result)
        {
            var newBalance = await service.GetBalanceAsync(request.UserId);

            return Results.Ok(new { newBalance });
        }

        return Results.BadRequest(new { error = "Withdrawal failed - insufficient funds" });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization()
.WithName("Withdraw");
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
    var exception = exceptionHandlerPathFeature?.Error;

    Console.WriteLine($"Unhandled exception: {exception?.Message}");
    Console.WriteLine($"Stack trace: {exception?.StackTrace}");

    context.Response.StatusCode = 500;

    await context.Response.WriteAsync("An unexpected error occurred. Please try again later.");
}));

app.UseHttpMetrics();
app.MapMetrics();
app.Run();
#endregion
