namespace App.Domain.Aggregates.TenantAggregate;

/// <summary>
/// Status subskrypcji salonu. Zastąpił dawny <c>SubscriptionPlan</c> (Trial/Free/Pro).
/// Free został usunięty z modelu biznesowego — po wygaśnięciu trialu salon przechodzi
/// w PastDue dopóki nie opłaci kolejnego okresu.
/// </summary>
public enum SubscriptionStatus
{
  Trial = 0,
  Active = 1,
  PastDue = 2,
  Canceled = 3,
}
