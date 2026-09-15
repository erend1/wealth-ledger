using WealthLedger.Domain.Common;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Ledger
{
    /// <summary>
    /// The deterministic money amount implied by executed quantity and
    /// executed unit price, together with its exact rounding residual.
    /// </summary>
    /// <remarks>
    /// This is review evidence only. Executed quantity, executed unit price
    /// and entered cash consideration remain independent source facts; the
    /// implied amount never overwrites any of them.
    /// </remarks>
    public sealed record PriceImpliedAmount(
        Money RoundedAmount,
        bool IsExact)
    {
        /// <summary>
        /// True when the implied amount required rounding to reach whole
        /// minor units, so the displayed amount is not the exact product.
        /// </summary>
        public bool IsRounded => !IsExact;
    }

    /// <summary>
    /// Computes the price-implied gross amount for a trade line.
    /// </summary>
    public static class PriceImpliedAmountCalculator
    {
        /// <summary>
        /// Computes executed quantity times executed unit price, expressed in
        /// whole currency minor units.
        /// </summary>
        /// <remarks>
        /// Quantity and unit price are both E8, so their product carries 16
        /// implied decimal places and the result needs
        /// <c>quantity * price / 10^(16 - minorUnitDigits)</c>.
        ///
        /// The algebraically identical <c>q * p * 10^d / 10^16</c> must not be
        /// used: <c>q * p</c> alone already reaches about 8.5e37 for extreme
        /// inputs, and multiplying that by <c>10^d</c> overflows even
        /// <see cref="Int128"/>. Minor-unit digits are constrained to 0-8, so
        /// the divisor exponent here is always 8-16 and the whole calculation
        /// stays inside <see cref="Int128"/> for every valid input.
        /// </remarks>
        public static PriceImpliedAmount Compute(
            Quantity quantity,
            UnitPrice unitPrice,
            int minorUnitDigits)
        {
            ArgumentNullException.ThrowIfNull(unitPrice);

            if (minorUnitDigits is < 0 or > 8)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minorUnitDigits),
                    "Currency minor-unit digits must be between zero and eight.");
            }

            var scaledProduct =
                (Int128)quantity.RawE8
                * unitPrice.RawE8;

            var divisor =
                ExactInteger.Pow10(16 - minorUnitDigits);

            var rounded =
                ExactInteger.DivideRoundHalfToEven(
                    scaledProduct,
                    divisor);

            var minorUnits =
                ExactInteger.ToInt64(
                    rounded,
                    "The price-implied amount does not fit the supported money range.");

            var isExact =
                scaledProduct % divisor == 0;

            return new PriceImpliedAmount(
                Money.FromMinorUnits(
                    minorUnits,
                    unitPrice.Currency),
                isExact);
        }
    }
}
