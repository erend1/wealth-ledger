using WealthLedger.Application.Common;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.CoreLedger;

internal static class CurrencyAmountConverter
{
    internal static long ToQuantityRawE8(
        Money amount,
        CurrencyReference currency)
    {
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentNullException.ThrowIfNull(currency);

        if (amount.Currency != currency.Code)
        {
            throw new ApplicationRuleViolationException(
                "The monetary amount and currency metadata must use the same currency.");
        }

        if (currency.MinorUnitDigits is < 0 or > 8)
        {
            throw new ApplicationRuleViolationException(
                "Currency minor-unit digits must be between zero and eight.");
        }

        long multiplier = 1;

        for (var digit = currency.MinorUnitDigits; digit < 8; digit++)
        {
            multiplier = checked(multiplier * 10);
        }

        return checked(amount.MinorUnits * multiplier);
    }
}
