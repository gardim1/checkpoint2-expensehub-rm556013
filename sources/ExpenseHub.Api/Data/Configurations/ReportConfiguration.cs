using ExpenseHub.Api.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseHub.Api.Data.Configurations;

internal sealed class ReportConfiguration : IEntityTypeConfiguration<ReportRequest>
{
    public void Configure(EntityTypeBuilder<ReportRequest> builder)
    {
        builder.ToTable("Reports");
        builder.HasKey(report => report.Id);
        builder.Property(report => report.Id).ValueGeneratedNever();
        builder.Property(report => report.OwnerId).IsRequired().HasMaxLength(450);
        builder.Property(report => report.Scope).IsRequired().HasMaxLength(10);
        builder.Property(report => report.Status).IsRequired().HasMaxLength(20);
        builder.HasIndex(report => report.OwnerId);
    }
}
