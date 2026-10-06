namespace SandinFinance.Core;

public static class AccountTransfers
{
    public static void Edit(AccountStore store, Guid accountId, Guid entryId, decimal amount, TimeOnly time)
    {
        Money.Validate(amount, "Сумма перевода");
        if (amount == 0) throw new ArgumentException("Сумма перевода должна быть больше нуля.");
        var accounts = store.Load();
        var selected = accounts.SingleOrDefault(a => a.Id == accountId)?.Transactions.SingleOrDefault(t => t.Id == entryId && t.IsTransfer) ?? throw new ArgumentException("Перевод не найден.");
        var legs = accounts.SelectMany(a => a.Transactions.Where(t => t.TransferId == selected.TransferId).Select(t => (Account: a, Entry: t))).ToList();
        if (legs.Count != 2) throw new ArgumentException("Редактирование перевода с удалённым счётом недоступно.");
        var timestamp = DateOnly.FromDateTime(selected.OccurredAt).ToDateTime(time);
        if (timestamp > DateTime.Now) throw new ArgumentException("Время не может быть в будущем.");
        foreach (var leg in legs)
        {
            var changed = leg.Entry with { Amount = amount, OccurredAt = timestamp };
            changed.Validate();
            var updated = BudgetOperations.PrepareUpdatedAccount(leg.Account with { Transactions = leg.Account.Transactions.Select(t => t.Id == changed.Id ? changed : t).ToList() }, timestamp, DateOnly.FromDateTime(DateTime.Today));
            accounts[accounts.FindIndex(a => a.Id == updated.Id)] = updated;
        }
        store.Save(accounts);
    }

    public static Guid Add(AccountStore store, Guid fromId, Guid toId, decimal amount, DateTime timestamp)
    {
        if (fromId == toId) throw new ArgumentException("Выберите два разных счёта.");
        var today = DateOnly.FromDateTime(DateTime.Today);
        var accounts = store.LoadAndAccrue(today);
        var source = accounts.SingleOrDefault(a => a.Id == fromId) ?? throw new ArgumentException("Выберите счёт списания.");
        var destination = accounts.SingleOrDefault(a => a.Id == toId) ?? throw new ArgumentException("Выберите счёт зачисления.");
        if (source.IsCredit && source.GraceRule == GracePeriodRule.Unspecified)
            throw new ArgumentException("Сначала настройте беспроцентный период кредитки, чтобы рассчитать срок перевода.");
        var transferId = Guid.NewGuid();
        BudgetTransaction Leg(CategoryKind kind, Account counterparty) => new()
        {
            Kind = kind, Amount = amount, OccurredAt = DateTime.SpecifyKind(timestamp, DateTimeKind.Unspecified),
            CategoryName = "Перевод", CategoryIcon = "money", CategoryColor = "#607780",
            TransferId = transferId, CounterpartyAccountId = counterparty.Id, CounterpartyAccountName = counterparty.Name,
            CreditRule = kind == CategoryKind.Expense && source.IsCredit ? source.GraceRule : GracePeriodRule.Unspecified,
            CreditGraceDays = kind == CategoryKind.Expense && source.IsCredit ? source.GraceDays : 0,
            CreditStatementDay = kind == CategoryKind.Expense && source.IsCredit ? source.StatementDay : 0
        };
        var outgoing = Leg(CategoryKind.Expense, destination);
        var incoming = Leg(CategoryKind.Income, source);
        outgoing.Validate(); incoming.Validate();
        var sourceUpdated = BudgetOperations.PrepareUpdatedAccount(source with { Transactions = [.. source.Transactions, outgoing] }, timestamp, today);
        var destinationUpdated = BudgetOperations.PrepareUpdatedAccount(destination with { Transactions = [.. destination.Transactions, incoming] }, timestamp, today);
        // Both legs and balances are committed by one atomic file replacement.
        store.Save(accounts.Select(a => a.Id == fromId ? sourceUpdated : a.Id == toId ? destinationUpdated : a).ToList());
        return transferId;
    }
}
