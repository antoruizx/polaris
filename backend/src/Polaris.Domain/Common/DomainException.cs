namespace Polaris.Domain.Common;

public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict
}

/// <summary>
/// Violación de una regla de negocio. <see cref="Code"/> es estable y la web lo usa para decidir qué mostrar.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(ErrorKind kind, string code, string message) : base(message)
    {
        Kind = kind;
        Code = code;
    }

    public ErrorKind Kind { get; }
    public string Code { get; }

    public static DomainException Validation(string code, string message) => new(ErrorKind.Validation, code, message);
    public static DomainException NotFound(string code, string message) => new(ErrorKind.NotFound, code, message);
    public static DomainException Conflict(string code, string message) => new(ErrorKind.Conflict, code, message);
}
