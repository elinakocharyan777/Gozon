using System.Text.Json;
using Gozon.Contracts;
using Microsoft.EntityFrameworkCore;
using Orders.Api;
using Orders.Api.Api;
using Orders.Api.Db;
using Orders.Api.Messaging;
using Orders.Api.Swagger;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{ 
    // считывает заголовок X-User-Id или параметр запроса user_id 
    c.OperationFilter<UserIdOperationFilter>();
});

builder.Services.Configure<RabbitOptions>(builder.Configuration.GetSection("RabbitMq"));
builder.Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RabbitOptions>>().Value;
    return new RabbitMqBus(opts, sp.GetRequiredService<ILogger<RabbitMqBus>>());
});

builder.Services.AddDbContext<OrdersDbContext>(opt =>
{
    var cs = builder.Configuration.GetConnectionString("Db");
    opt.UseNpgsql(cs);
});

builder.Services.AddHostedService<OutboxPublisherHostedService>();
builder.Services.AddHostedService<PaymentResultsConsumerHostedService>();

var app = builder.Build();

app.UseUserId();
app.UseSwagger();
app.UseSwaggerUI();

// схема базы данных существует
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
    db.Database.EnsureCreated();
}

// Health
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// создает заказ
app.MapPost("/orders", async (HttpContext ctx, CreateOrderRequest req, OrdersDbContext db, CancellationToken ct) =>
{
    var userId = ctx.GetRequiredUserId();
    if (req.Amount <= 0) return Results.BadRequest("Amount must be > 0.");

    var order = new OrderEntity
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Amount = decimal.Round(req.Amount, 2),
        Status = OrderStatus.PendingPayment,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    var evt = new OrderPaymentRequested(
        MessageId: Guid.NewGuid(),
        OrderId: order.Id,
        UserId: userId,
        Amount: order.Amount,
        OccurredAt: DateTimeOffset.UtcNow
    );

    var outbox = new OutboxMessageEntity
    {
        Id = Guid.NewGuid(),
        Type = nameof(OrderPaymentRequested),
        Destination = Queues.PaymentRequests,
        Payload = JsonSerializer.Serialize(evt),
        OccurredAt = evt.OccurredAt,
        Attempts = 0
    };

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    db.Orders.Add(order);
    db.Outbox.Add(outbox);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);

    return Results.Created($"/orders/{order.Id}", new
    {
        orderId = order.Id,
        status = order.Status.ToString(),
        amount = order.Amount
    });
})
.WithName("CreateOrder");

// список заказов
app.MapGet("/orders", async (HttpContext ctx, OrdersDbContext db, CancellationToken ct) =>
{
    var userId = ctx.GetRequiredUserId();
    var orders = await db.Orders
        .Where(o => o.UserId == userId)
        .OrderByDescending(o => o.CreatedAt)
        .Select(o => new
        {
            orderId = o.Id,
            amount = o.Amount,
            status = o.Status.ToString(),
            createdAt = o.CreatedAt,
            updatedAt = o.UpdatedAt
        })
        .ToListAsync(ct);

    return Results.Ok(orders);
})
.WithName("GetOrders");

// получение статуса заказа
app.MapGet("/orders/{orderId:guid}", async (HttpContext ctx, Guid orderId, OrdersDbContext db, CancellationToken ct) =>
{
    var userId = ctx.GetRequiredUserId();
    var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId, ct);
    if (order is null) return Results.NotFound();

    return Results.Ok(new
    {
        orderId = order.Id,
        amount = order.Amount,
        status = order.Status.ToString(),
        createdAt = order.CreatedAt,
        updatedAt = order.UpdatedAt
    });
})
.WithName("GetOrder");

app.Run();
