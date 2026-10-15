namespace ExpenseHub.Api.Common;

/// <summary>
/// Falha de negócio ou autorização, convertida em ProblemDetails pela camada HTTP.
/// </summary>
internal sealed record ServiceError(ServiceErrorKind Kind, string Message)
{
    public static ServiceError Validation(string message) => new(ServiceErrorKind.Validation, message);

    public static ServiceError NotFound(string message) => new(ServiceErrorKind.NotFound, message);

    public static ServiceError Forbidden(string message) => new(ServiceErrorKind.Forbidden, message);

    public static ServiceError Conflict(string message) => new(ServiceErrorKind.Conflict, message);
}
