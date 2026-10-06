namespace SandinFinance.Core;

public sealed record BudgetTransaction
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public CategoryKind Kind { get; init; }
    public decimal Amount { get; init; }
    public DateTime OccurredAt { get; init; }
    public Guid CategoryId { get; init; }
    public required string CategoryName { get; init; }
    public string CategoryColor { get; init; } = "#607780";
    public string CategoryIcon { get; init; } = "star";
    public Guid? MerchantId { get; init; }
    public string MerchantName { get; init; } = "";
    public GracePeriodRule CreditRule { get; init; }
    public int CreditGraceDays { get; init; }
    public int CreditStatementDay { get; init; }
    public Guid? TransferId { get; init; }
    public Guid? CounterpartyAccountId { get; init; }
    public string CounterpartyAccountName { get; init; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public bool IsTransfer => TransferId is not null;

    [System.Text.Json.Serialization.JsonIgnore] public decimal SignedAmount => Kind == CategoryKind.Income ? Amount : -Amount;

    public void Validate()
    {
        if (Id == Guid.Empty || (!IsTransfer && CategoryId == Guid.Empty) || !Enum.IsDefined(Kind)) throw new ArgumentException("Неизвестная категория или тип операции.");
        if (IsTransfer && (TransferId == Guid.Empty || CounterpartyAccountId is null || CounterpartyAccountId == Guid.Empty || string.IsNullOrWhiteSpace(CounterpartyAccountName) || CounterpartyAccountName.Length > 80 || CategoryId != Guid.Empty))
            throw new ArgumentException("Повреждена связь перевода между счетами.");
        if (!IsTransfer && (CounterpartyAccountId is not null || CounterpartyAccountName != "")) throw new ArgumentException("У обычной операции указан другой счёт.");
        Money.Validate(Amount, "Сумма операции");
        if (Amount == 0) throw new ArgumentException("Сумма операции должна быть больше нуля.");
        if (OccurredAt == default || OccurredAt > DateTime.Now) throw new ArgumentException("Дата и время операции должны быть не позднее текущих.");
        new Category { Name = CategoryName, ColorHex = CategoryColor, IconKey = CategoryIcon }.Validate();
        if (MerchantName is null || MerchantName.Length > 80 || MerchantId == Guid.Empty) throw new ArgumentException("Название магазина или источника: до 80 символов.");
        if (CreditRule != GracePeriodRule.Unspecified && (Kind != CategoryKind.Expense || CreditRule is not (GracePeriodRule.EndOfNextMonth or GracePeriodRule.MonthEndPlusDays or GracePeriodRule.StatementMonthEnd or GracePeriodRule.PurchaseMonthPlusThreeMonths) || CreditGraceDays is < 1 or > 730 || (CreditRule == GracePeriodRule.StatementMonthEnd && CreditStatementDay is < 1 or > 28)))
            throw new ArgumentException("Недопустимые условия покупки по кредитке.");
    }

    public CreditPurchase AsCreditPurchase() => new()
    {
        Id = Id, Name = IsTransfer ? $"Перевод на {CounterpartyAccountName}"[..Math.Min(80, CounterpartyAccountName.Length + 11)] : string.IsNullOrEmpty(MerchantName) ? CategoryName : $"{CategoryName}: {MerchantName}"[..Math.Min(80, CategoryName.Length + MerchantName.Length + 2)],
        Amount = Amount, Date = DateOnly.FromDateTime(OccurredAt), Time = TimeOnly.FromDateTime(OccurredAt), Rule = CreditRule, GraceDays = CreditGraceDays, StatementDay = CreditStatementDay
    };
}

public sealed record SavingsRate(DateOnly From, decimal AnnualRate, InterestPayout Payout);

public static class BudgetOperations
{
    public static void EditIncome(AccountStore store, Guid accountId, Guid transactionId, decimal amount, TimeOnly time)
    {
        Money.Validate(amount, "Сумма поступления");
        if (amount == 0) throw new ArgumentException("Сумма должна быть больше нуля.");
        var accounts = store.Load();
        var account = accounts.SingleOrDefault(a => a.Id == accountId) ?? throw new ArgumentException("Счёт не найден.");
        var transaction = account.Transactions.SingleOrDefault(t => t.Id == transactionId && t.Kind == CategoryKind.Income && !t.IsTransfer) ?? throw new ArgumentException("Поступление не найдено.");
        var timestamp = DateOnly.FromDateTime(transaction.OccurredAt).ToDateTime(time);
        if (timestamp > DateTime.Now) throw new ArgumentException("Время не может быть в будущем.");
        var changed = transaction with { Amount = amount, OccurredAt = timestamp };
        changed.Validate();
        var updated = PrepareUpdatedAccount(account with { Transactions = account.Transactions.Select(t => t.Id == transactionId ? changed : t).ToList() }, timestamp, DateOnly.FromDateTime(DateTime.Today));
        store.Save(accounts.Select(a => a.Id == accountId ? updated : a).ToList());
    }

    public static void EditExpense(AccountStore store, Guid accountId, Guid expenseId, decimal amount, TimeOnly time)
    {
        Money.Validate(amount, "Сумма расхода");
        if (amount == 0) throw new ArgumentException("Сумма расхода должна быть больше нуля.");
        var accounts = store.Load();
        var account = accounts.SingleOrDefault(a => a.Id == accountId)
            ?? throw new ArgumentException("Счёт расхода не найден.");
        var transaction = account.Transactions.SingleOrDefault(t => t.Id == expenseId);
        var purchase = account.Purchases.SingleOrDefault(p => p.Id == expenseId);
        if (transaction is not null && (transaction.Kind != CategoryKind.Expense || transaction.IsTransfer))
            throw new ArgumentException("Изменить можно только расход, не перевод или доход.");
        if (transaction is null && purchase is null) throw new ArgumentException("Расход удалён или не найден.");
        var date = transaction is not null ? DateOnly.FromDateTime(transaction.OccurredAt) : purchase!.Date;
        var timestamp = date.ToDateTime(time);
        if (timestamp > DateTime.Now) throw new ArgumentException("Время расхода не может быть в будущем.");
        var updated = account with
        {
            Transactions = account.Transactions.Select(t => t.Id == expenseId ? t with { Amount = amount, OccurredAt = timestamp } : t).ToList(),
            Purchases = account.Purchases.Select(p => p.Id == expenseId ? p with { Amount = amount, Time = time } : p).ToList()
        };
        updated = PrepareUpdatedAccount(updated, timestamp, DateOnly.FromDateTime(DateTime.Today));
        store.Save(accounts.Select(a => a.Id == accountId ? updated : a).ToList());
    }

    public static void DeleteExpense(AccountStore store, Guid accountId, Guid expenseId)
    {
        // Read without accrual so a rejected deletion leaves the file untouched.
        var accounts = store.Load();
        var account = accounts.SingleOrDefault(a => a.Id == accountId)
            ?? throw new ArgumentException("Счёт расхода не найден.");
        var transaction = account.Transactions.SingleOrDefault(t => t.Id == expenseId);
        var purchase = account.Purchases.SingleOrDefault(p => p.Id == expenseId);
        if (transaction is not null && (transaction.Kind != CategoryKind.Expense || transaction.IsTransfer))
            throw new ArgumentException("Этой кнопкой можно удалить только расход, не перевод или доход.");
        if (transaction is null && purchase is null) throw new ArgumentException("Расход уже удалён или не найден.");
        var timestamp = transaction?.OccurredAt ?? purchase!.Date.ToDateTime(purchase.Time);
        var updated = account with
        {
            Transactions = account.Transactions.Where(t => t.Id != expenseId).ToList(),
            Purchases = account.Purchases.Where(p => p.Id != expenseId).ToList()
        };
        try { updated = PrepareUpdatedAccount(updated, timestamp, DateOnly.FromDateTime(DateTime.Today)); }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"Нельзя удалить расход без нарушения последующих операций. {ex.Message}", ex);
        }
        store.Save(accounts.Select(a => a.Id == accountId ? updated : a).ToList());
    }

    public static BudgetTransaction Add(AccountStore store, CategoryStore categories, Guid accountId, Guid categoryId,
        CategoryKind kind, decimal amount, DateTime timestamp, string merchantName)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var accounts = store.LoadAndAccrue(today);
        var account = accounts.SingleOrDefault(a => a.Id == accountId) ?? throw new ArgumentException("Выберите счёт.");
        var category = categories.Load().SingleOrDefault(c => c.Id == categoryId && c.Kind == kind)
            ?? throw new ArgumentException("Выберите категорию выбранного типа.");
        if (kind == CategoryKind.Income && account.IsCredit)
            throw new ArgumentException("Доход добавляется на наличные, дебетовую карту или накопительный счёт. Для погашения кредитки используйте перевод на неё.");
        merchantName = merchantName.Trim();
        var transaction = new BudgetTransaction
        {
            Kind = kind, Amount = amount, OccurredAt = DateTime.SpecifyKind(timestamp, DateTimeKind.Unspecified),
            CategoryId = category.Id, CategoryName = category.Name, CategoryColor = category.ColorHex, CategoryIcon = category.IconKey,
            MerchantId = category.Merchants.FirstOrDefault(m => string.Equals(m.Name, merchantName, StringComparison.OrdinalIgnoreCase))?.Id,
            MerchantName = merchantName, CreditRule = account.IsCredit && kind == CategoryKind.Expense ? account.GraceRule : GracePeriodRule.Unspecified,
            CreditGraceDays = account.IsCredit ? account.GraceDays : 0, CreditStatementDay = account.IsCredit ? account.StatementDay : 0
        };
        transaction.Validate();
        if (DateOnly.FromDateTime(timestamp) < account.OpeningDate) throw new ArgumentException("Операция не может быть раньше даты начала учёта счёта.");
        var updated = account with { Transactions = [.. account.Transactions, transaction] };
        updated = PrepareUpdatedAccount(updated, timestamp, today);
        store.Save(accounts.Select(a => a.Id == accountId ? updated : a).ToList());
        return transaction;
    }

    public static Account PrepareUpdatedAccount(Account updated, DateTime timestamp, DateOnly today)
    {
        if (DateOnly.FromDateTime(timestamp) < updated.OpeningDate) throw new ArgumentException("Операция не может быть раньше даты начала учёта счёта.");
        if (updated.IsSavings)
        {
            updated = SavingsInterest.EnsureHistory(updated);
            if (DateOnly.FromDateTime(timestamp) < updated.InterestHistoryStart)
                throw new ArgumentException($"Для этого счёта история ставок сохранена с {updated.InterestHistoryStart:dd.MM.yyyy}. Более раннюю операцию нельзя учесть без пересчёта прежней истории банка.");
            updated = SavingsInterest.Recalculate(updated, today);
        }
        else ValidateTimeline(updated);
        updated.Validate();
        return updated;
    }

    public static void ValidateTimeline(Account account)
    {
        var balance = account.OpeningBalance;
        var movements = account.Transactions.Select(t => (Time: t.OccurredAt, Delta: account.IsCredit ? -t.SignedAmount : t.SignedAmount))
            .Concat(account.Purchases.Select(p => (Time: p.Date.ToDateTime(p.Time), Delta: p.Amount))).OrderBy(m => m.Time);
        foreach (var movement in movements)
        {
            balance += movement.Delta;
            if (balance < 0) throw new ArgumentException("На дату операции на счёте недостаточно денег.");
            if (account.IsCredit && balance > account.CreditLimit) throw new ArgumentException("Покупка превышает доступный кредитный остаток на эту дату.");
        }
    }
}
