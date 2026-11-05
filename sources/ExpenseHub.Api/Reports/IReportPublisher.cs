using System;
using System.Threading;
using System.Threading.Tasks;

namespace ExpenseHub.Api.Reports;

internal interface IReportPublisher
{
    Task PublishAsync(Guid reportId, CancellationToken cancellationToken);
}
