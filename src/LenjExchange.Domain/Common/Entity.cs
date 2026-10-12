namespace LenjExchange.Domain.Common;

public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
}

public abstract class AuditableEntity : Entity
{
    public DateTime CreatedAtUtc { get; protected set; } = DateTime.UtcNow;
    public string? CreatedBy { get; protected set; }

    public void StampCreated(string? user, DateTime utcNow)
    {
        CreatedBy = user;
        CreatedAtUtc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
    }
}

/// <summary>Soft-deletable entity: rows are never physically removed because ledger lines may reference them.</summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; }
    DateTime? DeletedAtUtc { get; }
}

public class DomainException : Exception
{
    public string Code { get; }

    public DomainException(string code, string message) : base(message)
    {
        Code = code;
    }
}

public static class Guard
{
    public static string NotBlank(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException("required", $"مقدار «{field}» الزامی است.");
        return value.Trim();
    }

    public static decimal Positive(decimal value, string field)
    {
        if (value <= 0)
            throw new DomainException("positive", $"مقدار «{field}» باید بزرگ‌تر از صفر باشد.");
        return value;
    }

    public static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
