using ExpenseHub.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseHub.Api.Data.Configurations;

/// <summary>
/// Mapeamento do histórico de reembolsos.
/// </summary>
internal sealed class ExpenseHistoryConfiguration : IEntityTypeConfiguration<ExpenseHistory>
{
    public void Configure(EntityTypeBuilder<ExpenseHistory> builder)
    {
        builder.ToTable("ExpenseHistories");
        builder.HasKey(history => history.Id);
        builder.Property(history => history.Action).HasConversion<string>().HasMaxLength(20);
        builder.Property(history => history.ActorId).IsRequired().HasMaxLength(450);
        builder.Property(history => history.FromStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(history => history.ToStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(history => history.Justification).HasMaxLength(500);
        builder.Property(history => history.Changes).HasMaxLength(2000);
        builder.HasIndex(history => history.ExpenseId);

        builder.HasOne<Expense>()
            .WithMany()
            .HasForeignKey(history => history.ExpenseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
