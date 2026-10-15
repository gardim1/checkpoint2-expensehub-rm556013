namespace ExpenseHub.Api.Domain;

/// <summary>
/// Estados possíveis de um reembolso. Rejected e Paid são finais.
/// </summary>
internal enum ExpenseStatus
{
    Draft = 0,
    Submitted = 1,
    Approved = 2,
    Rejected = 3,
    Paid = 4,
}
