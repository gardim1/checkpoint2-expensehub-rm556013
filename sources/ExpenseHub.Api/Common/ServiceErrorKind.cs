namespace ExpenseHub.Api.Common;

/// <summary>
/// Tipos de falha mapeados para 400, 404, 403 e 409.
/// </summary>
internal enum ServiceErrorKind
{
    Validation = 0,
    NotFound = 1,
    Forbidden = 2,
    Conflict = 3,
}
