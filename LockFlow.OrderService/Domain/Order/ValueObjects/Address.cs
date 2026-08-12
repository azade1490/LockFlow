
using LockFlow.OrderService.Domain.Common;

namespace LockFlow.OrderService.Domain.Order.ValueObjects;
public sealed class Address : ValueObject
{
    private Address() { } // EF Core

    public Address(
        string country,
        string province,
        string city,
        string street,
        string postalCode)
    {
        Country = country;
        Province = province;
        City = city;
        Street = street;
        PostalCode = postalCode;
    }

    public string Country { get; private set; } = default!;

    public string Province { get; private set; } = default!;

    public string City { get; private set; } = default!;

    public string Street { get; private set; } = default!;

    public string PostalCode { get; private set; } = default!;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Country;
        yield return Province;
        yield return City;
        yield return Street;
        yield return PostalCode;
    }
}
