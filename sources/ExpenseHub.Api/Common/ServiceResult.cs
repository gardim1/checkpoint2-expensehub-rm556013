using System.Diagnostics.CodeAnalysis;

namespace ExpenseHub.Api.Common;

/// <summary>
/// Resultado de uma operação de serviço: sucesso com valor ou falha tipada.
/// </summary>
/// <typeparam name="T">Tipo do valor retornado em caso de sucesso.</typeparam>
internal sealed class ServiceResult<T>
{
    private ServiceResult(T? value, ServiceError? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }

    public ServiceError? Error { get; }

    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool Succeeded => Error is null;

    public static ServiceResult<T> Success(T value) => new(value, null);

    public static ServiceResult<T> Failure(ServiceError error) => new(default, error);

    public static implicit operator ServiceResult<T>(ServiceError error) => Failure(error);
}
