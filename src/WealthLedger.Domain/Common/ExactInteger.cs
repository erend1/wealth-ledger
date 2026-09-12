namespace WealthLedger.Domain.Common
{
    /// <summary>
    /// Exact integer arithmetic helpers for financial derivation.
    /// </summary>
    /// <remarks>
    /// Financial values are stored as integer minor units and E8 fixed point.
    /// Deriving a proportional share therefore needs a division that states
    /// its rounding rule instead of relying on binary floating point or an
    /// implicit decimal conversion.
    /// </remarks>
    public static class ExactInteger
    {
        /// <summary>
        /// Divides two non-negative integers and rounds the result to the
        /// nearest integer, resolving an exact midpoint to the even result.
        /// </summary>
        /// <remarks>
        /// Midpoint-to-even is used so repeated derivation over many values
        /// does not drift upwards the way midpoint-away-from-zero does.
        ///
        /// The tie test compares the remainder against its complement rather
        /// than doubling the remainder, because doubling can overflow when the
        /// denominator is close to the width of <see cref="Int128"/>.
        /// </remarks>
        public static Int128 DivideRoundHalfToEven(
            Int128 numerator,
            Int128 denominator)
        {
            if (numerator < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(numerator),
                    "Exact division requires a non-negative numerator.");
            }

            if (denominator <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(denominator),
                    "Exact division requires a positive denominator.");
            }

            var quotient = numerator / denominator;

            var remainder =
                numerator - quotient * denominator;

            if (remainder == 0)
            {
                return quotient;
            }

            var complement = denominator - remainder;

            if (remainder > complement)
            {
                return quotient + 1;
            }

            if (remainder < complement)
            {
                return quotient;
            }

            // Exact midpoint: keep the even quotient.
            return Int128.IsEvenInteger(quotient)
                ? quotient
                : quotient + 1;
        }

        /// <summary>
        /// Narrows an exact intermediate to <see cref="long"/>, failing
        /// closed rather than silently truncating an out-of-range result.
        /// </summary>
        public static long ToInt64(
            Int128 value,
            string overflowMessage)
        {
            if (value < long.MinValue
                || value > long.MaxValue)
            {
                throw new DomainRuleViolationException(
                    overflowMessage);
            }

            return (long)value;
        }

        /// <summary>
        /// Returns 10 raised to the given non-negative exponent.
        /// </summary>
        public static Int128 Pow10(int exponent)
        {
            if (exponent is < 0 or > 38)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(exponent));
            }

            Int128 result = 1;

            for (var index = 0; index < exponent; index++)
            {
                result *= 10;
            }

            return result;
        }
    }
}
