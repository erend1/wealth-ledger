namespace WealthLedger.Application.Common;

public sealed class ApplicationRuleViolationException : InvalidOperationException
{
    public ApplicationRuleViolationException(string message)
        : base(message)
    {
    }
}
