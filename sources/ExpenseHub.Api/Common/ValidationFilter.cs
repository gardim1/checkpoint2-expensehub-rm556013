using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ExpenseHub.Api.Common;

/// <summary>
/// Valida os atributos declarativos do DTO de entrada e responde 400 (ValidationProblemDetails).
/// </summary>
/// <typeparam name="T">Tipo do DTO recebido pelo endpoint.</typeparam>
internal sealed class ValidationFilter<T> : IEndpointFilter
    where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        T? argument = context.Arguments.OfType<T>().FirstOrDefault();
        if (argument is null)
        {
            return Results.Problem(
                detail: "O corpo da requisição é obrigatório.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Entrada inválida");
        }

        List<ValidationResult> results = [];
        if (!Validator.TryValidateObject(argument, new ValidationContext(argument), results, validateAllProperties: true))
        {
            Dictionary<string, string[]> errors = results
                .SelectMany(result => (result.MemberNames.Any() ? result.MemberNames : [string.Empty])
                    .Select(member => (Member: member, Message: result.ErrorMessage ?? "Valor inválido.")))
                .GroupBy(error => error.Member)
                .ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray());

            return Results.ValidationProblem(errors);
        }

        return await next(context);
    }
}

/// <summary>
/// Extensão para aplicar <see cref="ValidationFilter{T}"/> a um endpoint.
/// </summary>
internal static class ValidationFilterExtensions
{
    public static RouteHandlerBuilder WithValidation<T>(this RouteHandlerBuilder builder)
        where T : class
    {
        return builder.AddEndpointFilter(new ValidationFilter<T>());
    }
}
