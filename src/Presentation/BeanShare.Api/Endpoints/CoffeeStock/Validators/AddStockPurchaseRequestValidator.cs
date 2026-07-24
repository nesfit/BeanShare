using BeanShare.Contracts.CoffeeStock;
using BeanShare.Domain.ValueObjects;
using FastEndpoints;
using FluentValidation;

namespace BeanShare.Api.Endpoints.CoffeeStock.Validators;

public sealed class AddStockPurchaseRequestValidator : Validator<AddStockPurchaseRequest>
{
    public AddStockPurchaseRequestValidator()
    {
        RuleFor(x => x.BodySpaceId)
            .Must(BeValidGuidIfProvided)
            .WithMessage("Space ID must be a valid GUID if provided");

        RuleFor(x => x.ProductName)
            .NotEmpty()
            .WithMessage("Product name is required")
            .MaximumLength(100)
            .WithMessage("Product name cannot exceed 100 characters");

        RuleFor(x => x.ProductBrand)
            .NotEmpty()
            .WithMessage("Product brand is required")
            .MaximumLength(50)
            .WithMessage("Product brand cannot exceed 50 characters");

        RuleFor(x => x.ProductType)
            .NotEmpty()
            .WithMessage("Product type is required")
            .Must(BeValidCoffeeType)
            .WithMessage("Product type must be one of: Espresso, Filter, Instant, Decaf, Specialty");

        RuleFor(x => x.QuantityGrams)
            .GreaterThan(0)
            .WithMessage("Quantity must be positive")
            .LessThanOrEqualTo(50000)
            .WithMessage("Quantity cannot exceed 50kg");

        RuleFor(x => x.CostAmount)
            .GreaterThan(0)
            .WithMessage("Cost must be positive")
            .LessThanOrEqualTo(10000)
            .WithMessage("Cost cannot exceed 10,000");

        RuleFor(x => x.CostCurrency)
            .NotEmpty()
            .WithMessage("Currency is required")
            .Length(3)
            .WithMessage("Currency must be 3 characters")
            .Must(BeValidCurrency)
            .WithMessage("Currency must be valid");

        RuleFor(x => x.Vendor)
            .NotEmpty()
            .WithMessage("Vendor is required")
            .MaximumLength(100)
            .WithMessage("Vendor cannot exceed 100 characters");

        RuleFor(x => x.BagCount)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Bag count must be at least 1")
            .LessThanOrEqualTo(100)
            .WithMessage("Bag count cannot exceed 100");

        RuleFor(x => x.PurchasedAt)
            .NotEmpty()
            .WithMessage("Purchase date is required")
            .LessThanOrEqualTo(DateTime.UtcNow.AddDays(1))
            .WithMessage("Purchase date cannot be in the future")
            .GreaterThanOrEqualTo(DateTime.UtcNow.AddYears(-10))
            .WithMessage("Purchase date cannot be more than 10 years ago");
    }

    private static bool BeValidGuidIfProvided(Guid? guid)
    {
        return !guid.HasValue || guid.Value != Guid.Empty;
    }

    private static bool BeValidCoffeeType(string coffeeType)
    {
        return Enum.TryParse<CoffeeType>(coffeeType, ignoreCase: true, out _);
    }

    private static bool BeValidCurrency(string currency)
    {
        try
        {
            Currency.Create(currency);
            return true;
        }
        catch
        {
            return false;
        }
    }
}