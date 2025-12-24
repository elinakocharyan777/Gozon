using Microsoft.EntityFrameworkCore;
using Payments.Api;
using Payments.Api.Api;
using Payments.Api.Db;
using Payments.Api.Messaging;
using Payments.Api.Swagger;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
       c.OperationFilter<UserIdOperationFilter>();
});

builder.Services.Configure<RabbitOptions>(builder.Configuration.GetSection("RabbitMq"));
builder.Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RabbitOptions>>().Value;
    return new RabbitMqBus(opts, sp.GetRequiredService<ILogger<RabbitMqBus>>());
});

builder.Services.AddDbContext<PaymentsDbContext>(opt =>
{
    var cs = builder.Configuration.GetConnectionString("Db");
    opt.UseNpgsql(cs);
});

builder.Services.AddHostedService<OutboxPublisherHostedService>();
builder.Services.AddHostedService<PaymentRequestsConsumerHostedService>();

var app = builder.Build();

app.UseUserId();
app.UseSwagger();
app.UseSwaggerUI();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
    db.Database.EnsureCreated();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// созлать акк
app.MapPost("/accounts", async (HttpContext ctx, PaymentsDbContext db, CancellationToken ct) =>
{
    var userId = ctx.GetRequiredUserId();

    var exists = await db.Accounts.AnyAsync(x => x.UserId == userId, ct);
    if (exists) return Results.Conflict("Account already exists for this user.");

    db.Accounts.Add(new AccountEntity
    {
        UserId = userId,
        Balance = 0m,
        Version = 0,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    });

    await db.SaveChangesAsync(ct);
    return Results.Created("/accounts", new { userId, balance = 0m });
})
.WithName("CreateAccount");

// Top up 
app.MapPost("/accounts/topup", async (HttpContext ctx, TopUpRequest req, PaymentsDbContext db, CancellationToken ct) =>
{
    var userId = ctx.GetRequiredUserId();
    if (req.Amount <= 0) return Results.BadRequest("Amount must be > 0.");

    var amount = decimal.Round(req.Amount, 2);

    for (var attempt = 0; attempt < 10; attempt++)
    {
        var acc = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (acc == null) return Results.NotFound("Account not found.");

        var rows = await db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE accounts
SET balance = balance + {amount},
    version = version + 1,
    updated_at = {DateTimeOffset.UtcNow}
WHERE user_id = {userId}
  AND version = {acc.Version};
", ct);

        if (rows == 1)
        {
            var updated = await db.Accounts.AsNoTracking().FirstAsync(x => x.UserId == userId, ct);
            return Results.Ok(new { userId, balance = updated.Balance });
        }
    }

    return Results.StatusCode(409);
})
.WithName("TopUp");

// узнать баланс
app.MapGet("/accounts/balance", async (HttpContext ctx, PaymentsDbContext db, CancellationToken ct) =>
{
    var userId = ctx.GetRequiredUserId();
    var acc = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
    if (acc == null) return Results.NotFound("Account not found.");
    return Results.Ok(new { userId, balance = acc.Balance });
})
.WithName("GetBalance");

app.Run();
