using Microsoft.EntityFrameworkCore;

namespace Orders.Api.Db;

public sealed class OrdersDbContext : DbContext
{
    public OrdersDbContext(DbContextOptions<OrdersDbContext> options) : base(options) { }

    public DbSet<OrderEntity> Orders => Set<OrderEntity>();
    public DbSet<InboxMessageEntity> Inbox => Set<InboxMessageEntity>();
    public DbSet<OutboxMessageEntity> Outbox => Set<OutboxMessageEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderEntity>(b =>
        {
            b.ToTable("orders");
            b.HasKey(x => x.Id);

            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            b.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(18,2)");
            b.Property(x => x.Status).HasColumnName("status").IsRequired();
            b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

            b.HasIndex(x => new { x.UserId, x.CreatedAt }).HasDatabaseName("ix_orders_user_created");
        });

        modelBuilder.Entity<InboxMessageEntity>(b =>
        {
            b.ToTable("inbox_messages");
            b.HasKey(x => x.MessageId);

            b.Property(x => x.MessageId).HasColumnName("message_id");
            b.Property(x => x.ReceivedAt).HasColumnName("received_at").IsRequired();
        });

        modelBuilder.Entity<OutboxMessageEntity>(b =>
        {
            b.ToTable("outbox_messages");
            b.HasKey(x => x.Id);

            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.Type).HasColumnName("type").IsRequired();
            b.Property(x => x.Destination).HasColumnName("destination").IsRequired();
            b.Property(x => x.Payload).HasColumnName("payload").IsRequired();
            b.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();
            b.Property(x => x.ProcessedAt).HasColumnName("processed_at");
            b.Property(x => x.Attempts).HasColumnName("attempts").IsRequired();
            b.Property(x => x.LastError).HasColumnName("last_error");

            b.HasIndex(x => x.ProcessedAt).HasDatabaseName("ix_outbox_processed_at");
        });
    }
}
