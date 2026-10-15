using Microsoft.AspNetCore.Http;

namespace ExpenseHub.Api.Common;

/// <summary>
/// Converte falhas de serviço em respostas ProblemDetails consistentes.
/// </summary>
internal static class ServiceErrorHttpExtensions
{
    public static IResult ToProblem(this ServiceError error)
    {
        (int statusCode, string title) = error.Kind switch
        {
            ServiceErrorKind.Validation => (StatusCodes.Status400BadRequest, "Entrada inválida"),
            ServiceErrorKind.NotFound => (StatusCodes.Status404NotFound, "Recurso não encontrado"),
            ServiceErrorKind.Forbidden => (StatusCodes.Status403Forbidden, "Operação não permitida"),
            _ => (StatusCodes.Status409Conflict, "Conflito de estado"),
        };

        return Results.Problem(detail: error.Message, statusCode: statusCode, title: title);
    }
}
