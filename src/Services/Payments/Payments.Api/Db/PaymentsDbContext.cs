using Microsoft.EntityFrameworkCore;

namespace Payments.Api.Db;

public sealed class PaymentsDbContext : DbContext
{
    public PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : base(options) { }

    public DbSet<AccountEntity> Accounts => Set<AccountEntity>();
    public DbSet<PaymentOperationEntity> Operations => Set<PaymentOperationEntity>();
    public DbSet<InboxMessageEntity> Inbox => Set<InboxMessageEntity>();
    public DbSet<OutboxMessageEntity> Outbox => Set<OutboxMessageEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AccountEntity>(b =>
        {
            b.ToTable("accounts");
            b.HasKey(x => x.UserId);

            b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            b.Property(x => x.Balance).HasColumnName("balance").HasColumnType("numeric(18,2)");
            b.Property(x => x.Version).HasColumnName("version").IsRequired();
            b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

            b.HasIndex(x => x.UserId).IsUnique().HasDatabaseName("ux_accounts_user_id");
        });

        modelBuilder.Entity<PaymentOperationEntity>(b =>
        {
            b.ToTable("payment_operations");
            b.HasKey(x => x.Id);

            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.OrderId).HasColumnName("order_id").IsRequired();
            b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            b.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(18,2)");
            b.Property(x => x.Status).HasColumnName("status").IsRequired();
            b.Property(x => x.PaymentId).HasColumnName("payment_id");
            b.Property(x => x.Reason).HasColumnName("reason");
            b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

            b.HasIndex(x => x.OrderId).IsUnique().HasDatabaseName("ux_payment_operations_order_id");
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
