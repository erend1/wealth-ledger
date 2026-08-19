namespace WealthLedger.Domain.Common
{
    public sealed class DomainRuleViolationException : Exception
    {
        public DomainRuleViolationException(string message)
            : base(message)
        {
        }
    }
}
