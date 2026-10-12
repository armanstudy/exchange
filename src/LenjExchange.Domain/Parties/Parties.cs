using LenjExchange.Domain.Common;
using LenjExchange.Domain.Monetary;

namespace LenjExchange.Domain.Parties;

public class Customer : AuditableEntity, ISoftDeletable
{
    public string Code { get; private set; } = "";
    public string FullName { get; private set; } = "";
    public string? Phone { get; private set; }
    public string? Address { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; } = true;
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }

    protected Customer() { }

    public Customer(string code, string fullName, string? phone = null, string? address = null, string? notes = null)
    {
        Code = Guard.NotBlank(code, "کد مشتری");
        Update(fullName, phone, address, notes);
    }

    public void Update(string fullName, string? phone, string? address, string? notes)
    {
        FullName = Guard.NotBlank(fullName, "نام کامل");
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public void SetActive(bool active) => IsActive = active;

    public void SoftDelete(DateTime utcNow)
    {
        IsDeleted = true;
        IsActive = false;
        DeletedAtUtc = Guard.Utc(utcNow);
    }
}

/// <summary>Turkish exchange office (صرافی ترکیه).</summary>
public class Exchanger : AuditableEntity
{
    public string Name { get; private set; } = "";
    public string? Phone { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; } = true;

    protected Exchanger() { }

    public Exchanger(string name, string? phone = null, string? notes = null)
    {
        Name = Guard.NotBlank(name, "نام صرافی");
        Phone = phone;
        Notes = notes;
    }

    public void Rename(string name) => Name = Guard.NotBlank(name, "نام صرافی");
    public void SetActive(bool active) => IsActive = active;
}

/// <summary>Turkey representative who receives USD from the exchanger. Holds USD only.</summary>
public class Agent : AuditableEntity
{
    public static readonly CurrencyCode HeldCurrency = CurrencyCode.USD;

    public string Name { get; private set; } = "";
    public string? Phone { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; } = true;

    protected Agent() { }

    public Agent(string name, string? phone = null, string? notes = null)
    {
        Name = Guard.NotBlank(name, "نام نماینده");
        Phone = phone;
        Notes = notes;
    }

    public void Rename(string name) => Name = Guard.NotBlank(name, "نام نماینده");
    public void SetActive(bool active) => IsActive = active;
}

/// <summary>Final recipient in China (e.g. محمد). Accepted currencies are editable.</summary>
public class FinalRecipient : AuditableEntity
{
    public string Name { get; private set; } = "";
    public string Country { get; private set; } = "";
    public string AcceptedCurrenciesCsv { get; private set; } = "";
    public bool IsActive { get; private set; } = true;
    public string? Notes { get; private set; }

    protected FinalRecipient() { }

    public FinalRecipient(string name, string country, IEnumerable<CurrencyCode> currencies)
    {
        Name = Guard.NotBlank(name, "نام گیرنده");
        Country = Guard.NotBlank(country, "کشور");
        SetAcceptedCurrencies(currencies);
    }

    public IReadOnlyList<CurrencyCode> AcceptedCurrencies =>
        AcceptedCurrenciesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => Enum.Parse<CurrencyCode>(s)).ToList();

    public void SetAcceptedCurrencies(IEnumerable<CurrencyCode> currencies)
    {
        var list = currencies.Distinct().ToList();
        if (list.Count == 0) throw new DomainException("recipient_currency", "حداقل یک ارز برای گیرنده لازم است.");
        AcceptedCurrenciesCsv = string.Join(',', list);
    }

    public bool Accepts(CurrencyCode currency) => AcceptedCurrencies.Contains(currency);

    public void Update(string name, string country, string? notes)
    {
        Name = Guard.NotBlank(name, "نام گیرنده");
        Country = Guard.NotBlank(country, "کشور");
        Notes = notes;
    }

    public void SetActive(bool active) => IsActive = active;
}

/// <summary>Iranian bank account into which customers deposit.</summary>
public class Bank : AuditableEntity
{
    public string Name { get; private set; } = "";
    public string? AccountNumber { get; private set; }
    public bool IsActive { get; private set; } = true;

    protected Bank() { }

    public Bank(string name, string? accountNumber = null)
    {
        Name = Guard.NotBlank(name, "نام بانک");
        AccountNumber = accountNumber;
    }

    public void SetActive(bool active) => IsActive = active;
}
