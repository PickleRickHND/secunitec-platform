namespace Secunitec.Billing.Domain;

public sealed class BillingRuleException(string message) : Exception(message);
