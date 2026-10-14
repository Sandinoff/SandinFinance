namespace SandinFinance.Core;

public sealed record CreditPurchase
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }
    public decimal Amount { get; init; }
    public DateOnly Date { get; init; }
    public TimeOnly Time { get; init; }
    // Settings are captured at purchase time; later card changes affect new purchases only.
    public GracePeriodRule Rule { get; init; }
    public int GraceDays { get; init; }
    public int StatementDay { get; init; }
    public int PaymentParts { get; init; } = 1;

    public void Validate()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 80)
            throw new ArgumentException("Укажите название покупки: от 1 до 80 символов.");
        Money.Validate(Amount, "Сумма покупки");
        if (Amount == 0) throw new ArgumentException("Сумма покупки должна быть больше нуля.");
        if (Date == default || Date.ToDateTime(Time) > DateTime.Now)
            throw new ArgumentException("Дата покупки должна быть не позднее сегодняшней.");
        if (Rule is not (GracePeriodRule.EndOfNextMonth or GracePeriodRule.MonthEndPlusDays or GracePeriodRule.StatementMonthEnd or GracePeriodRule.PurchaseMonthPlusThreeMonths) || GraceDays is < 1 or > 730 || (Rule == GracePeriodRule.StatementMonthEnd && StatementDay is < 1 or > 28))
            throw new ArgumentException("Сначала настройте правило льготного периода кредитки.");
        if (PaymentParts != 1) throw new ArgumentException("Покупки месяца погашаются одним платежом.");
    }
}

public sealed record ScheduledPayment(DateOnly DueDate, decimal Amount)
{
    public DateTime Deadline => DueDate.ToDateTime(new TimeOnly(23, 59));
}

public static class CreditGrace
{
    private sealed class DebtLot(DateTime date, decimal amount, CreditPurchase? purchase, DateOnly? dueDate = null)
    {
        public DateTime Date { get; } = date;
        public decimal Remaining { get; set; } = amount;
        public CreditPurchase? Purchase { get; } = purchase;
        public DateOnly? DueDate { get; } = dueDate;
    }

    private static List<DebtLot> RemainingLots(Account account)
    {
        if (!account.IsCredit) return [];
        var lots = new List<DebtLot>();
        foreach (var payment in account.OpeningDebtPayments.OrderBy(p => p.DueDate)) lots.Add(new(account.OpeningDate.ToDateTime(TimeOnly.MinValue), payment.Amount, null, payment.DueDate));
        var unscheduledOpening = account.OpeningBalance - account.OpeningDebtPayments.Sum(p => p.Amount);
        if (unscheduledOpening > 0) lots.Add(new(account.OpeningDate.ToDateTime(TimeOnly.MinValue), unscheduledOpening, null));
        lots.AddRange(account.Purchases.Select(p => new DebtLot(p.Date.ToDateTime(p.Time), p.Amount, p)));
        lots.AddRange(account.Transactions.Where(t => t.Kind == CategoryKind.Expense).Select(t =>
            new DebtLot(t.OccurredAt, t.Amount, t.CreditRule == GracePeriodRule.Unspecified ? null : t.AsCreditPurchase())));
        lots = lots.OrderBy(lot => lot.Date).ToList();
        foreach (var payment in account.Transactions.Where(t => t.IsTransfer && t.Kind == CategoryKind.Income).OrderBy(t => t.OccurredAt))
        {
            var amount = payment.Amount;
            foreach (var lot in lots.Where(lot => lot.Date <= payment.OccurredAt && lot.Remaining > 0))
            {
                var applied = Math.Min(amount, lot.Remaining);
                lot.Remaining -= applied;
                amount -= applied;
                if (amount == 0) break;
            }
        }
        return lots;
    }

    public static IEnumerable<PurchaseSummary> OutstandingSummaries(Account account) =>
        RemainingLots(account).Where(lot => lot.DueDate is not null && lot.Remaining > 0).Select(lot => new PurchaseSummary("Начальный долг — без процентов", $"Погасить к {lot.DueDate:dd.MM.yyyy} — {Money.Format(lot.Remaining)}"))
        .Concat(MonthlySummaries(RemainingLots(account).Where(lot => lot.Purchase is not null && lot.Remaining > 0).Select(lot => lot.Purchase! with { Amount = lot.Remaining })));

    public static IReadOnlyList<ScheduledPayment> OutstandingPayments(Account account) => !account.IsCredit ? [] : RemainingLots(account)
        .Where(lot => lot.Remaining > 0 && (lot.DueDate is not null || lot.Purchase is not null))
        .GroupBy(lot => lot.DueDate ?? LastDueDate(lot.Purchase!))
        .OrderBy(group => group.Key)
        .Select(group => new ScheduledPayment(group.Key, group.Sum(lot => lot.Remaining))).ToList();

    public static IEnumerable<PurchaseSummary> PaymentSummaries(Account account) => OutstandingPayments(account)
        .Select(payment => new PurchaseSummary($"Задолженность до — {payment.DueDate:dd.MM}", Money.Format(payment.Amount)));

    public static DateOnly LastDueDate(CreditPurchase purchase)
    {
        if (purchase.Rule == GracePeriodRule.PurchaseMonthPlusThreeMonths)
            return EndOfMonth(new DateOnly(purchase.Date.Year, purchase.Date.Month, 1).AddMonths(3));
        if (purchase.Rule == GracePeriodRule.StatementMonthEnd)
        {
            var statementMonth = new DateOnly(purchase.Date.Year, purchase.Date.Month, 1);
            if (purchase.Date.Day >= purchase.StatementDay) statementMonth = statementMonth.AddMonths(1);
            return EndOfMonth(statementMonth);
        }
        var month = purchase.Rule == GracePeriodRule.EndOfNextMonth
            ? new DateOnly(purchase.Date.Year, purchase.Date.Month, 1).AddMonths(1)
            : EndOfMonth(purchase.Date).AddDays(purchase.GraceDays);
        return EndOfMonth(month);
    }

    public static IReadOnlyList<ScheduledPayment> Schedule(CreditPurchase purchase)
    {
        purchase.Validate();
        return [new(LastDueDate(purchase), purchase.Amount)];
    }

    public static IEnumerable<PurchaseSummary> MonthlySummaries(IEnumerable<CreditPurchase> purchases) => purchases
        .GroupBy(p => (p.Date.Year, p.Date.Month, Due: Schedule(p).Single().DueDate))
        .OrderBy(group => group.Key.Due).ThenBy(group => group.Key.Year).ThenBy(group => group.Key.Month)
        .Select(group => new PurchaseSummary(
            $"Расходы за {group.Key.Month:00}.{group.Key.Year} · покупок: {group.Count()}",
            $"Оплатить до {group.Key.Due:dd.MM.yyyy} включительно — {Money.Format(group.Sum(p => p.Amount))}\n" +
            string.Join(" · ", group.Select(p => $"{p.Name}: {Money.Format(p.Amount)}"))));

    private static DateOnly EndOfMonth(DateOnly month) =>
        new(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month));

    public static string Description(Account account)
    {
        var label = account.GraceDays == 0 ? "Льготный период не настроен" : $"Льготный период: {account.GraceDays} дней";
        var rule = account.GraceRule switch
        {
            GracePeriodRule.EndOfNextMonth => "покупки месяца — до конца следующего",
            GracePeriodRule.MonthEndPlusDays => $"все покупки месяца: конец месяца + {account.GraceDays} дней, затем конец получившегося месяца",
            GracePeriodRule.StatementMonthEnd => $"выписка {account.StatementDay}-го числа, покупки по {account.StatementDay - 1}-е включительно — оплата до конца месяца выписки",
            GracePeriodRule.PurchaseMonthPlusThreeMonths => "месяц покупок и три следующих месяца на погашение",
            _ => "правило погашения не задано"
        };
        var unscheduled = RemainingLots(account).Where(lot => lot.Purchase is null && lot.DueDate is null).Sum(lot => lot.Remaining);
        return $"{label}\n{rule}" + (unscheduled > 0 ? "\nДолг без графика: " + Money.Format(unscheduled) : "");
    }
}
