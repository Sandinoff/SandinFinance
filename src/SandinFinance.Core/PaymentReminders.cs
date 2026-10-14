namespace SandinFinance.Core;

public enum PaymentReminderKind { Recurring, Credit }
public sealed record PaymentReminder(PaymentReminderKind Kind, Guid TargetId, string Name, decimal Amount, DateTime DueAt)
{
    public string SummaryText => Kind == PaymentReminderKind.Credit
        ? $"{Name} · погасить {Money.Format(Amount)} · до {DueAt:dd.MM.yyyy}"
        : $"{Name} · {Money.Format(Amount)} · {DueAt:dd.MM HH:mm}";
}

public static class PaymentReminders
{
    public static IReadOnlyList<PaymentReminder> Calculate(IEnumerable<Account> accounts, IEnumerable<RecurringPayment> recurring, DateTime now)
    {
        var lastDay = DateOnly.FromDateTime(now).AddDays(3);
        return recurring.Where(p => p.NextDueAt is not null)
            .Select(p => new PaymentReminder(PaymentReminderKind.Recurring, p.Id, p.Name, p.Amount, p.NextDueAt!.Value))
            .Concat(accounts.Where(a => a.IsCredit).SelectMany(account => CreditGrace.OutstandingPayments(account)
                .Select(payment => new PaymentReminder(PaymentReminderKind.Credit, account.Id, account.Name, payment.Amount, payment.Deadline))))
            .Where(reminder => reminder.Amount > 0 && DateOnly.FromDateTime(reminder.DueAt) <= lastDay)
            .OrderBy(reminder => reminder.DueAt).ThenBy(reminder => reminder.Name).ToList();
    }
}
