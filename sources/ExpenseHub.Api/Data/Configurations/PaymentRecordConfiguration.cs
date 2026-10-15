using ExpenseHub.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseHub.Api.Data.Configurations;

/// <summary>
/// Mapeamento dos pagamentos simulados.
/// </summary>
internal sealed class PaymentRecordConfiguration : IEntityTypeConfiguration<PaymentRecord>
{
    public void Configure(EntityTypeBuilder<PaymentRecord> builder)
    {
        builder.ToTable("PaymentRecords");
        builder.HasKey(payment => payment.Id);
        builder.Property(payment => payment.Id).ValueGeneratedNever();
        builder.Property(payment => payment.Amount).HasPrecision(12, 2);
        builder.Property(payment => payment.PaidById).IsRequired().HasMaxLength(450);

        // Garante no banco que um reembolso possui no máximo um pagamento.
        builder.HasIndex(payment => payment.ExpenseId).IsUnique();
    }
}
