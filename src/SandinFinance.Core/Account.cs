using System.Globalization;
using System.Text.Json.Serialization;

namespace SandinFinance.Core;

public enum AccountType { Cash, DebitCard, CreditCard, Savings }
public enum InterestPayout { Daily, Monthly }
public enum GracePeriodRule { Unspecified, EndOfNextMonth, DaysThenMonthEnd, MonthEndPlusDays, StatementMonthEnd, PurchaseMonthPlusThreeMonths }

public sealed record Account
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }
    public AccountType Type { get; init; }
    public decimal OpeningBalance { get; init; }
    public decimal CreditLimit { get; init; }
    public decimal MonthlySpendingLimit { get; init; }
    public DateOnly OpeningDate { get; init; }
    public decimal AnnualInterestRate { get; init; }
    public InterestPayout InterestPayout { get; init; }
    public decimal CreditedInterest { get; init; }
    public decimal PendingInterest { get; init; }
    // Closed days before this date have already been processed.
    public DateOnly? InterestAsOf { get; init; }
    public int GraceDays { get; init; }
    public int StatementDay { get; init; }
    public List<OpeningDebtPayment> OpeningDebtPayments { get; init; } = [];
    public GracePeriodRule GraceRule { get; init; }
    public int PaymentParts { get; init; } = 1;
    public List<CreditPurchase> Purchases { get; init; } = [];
    public bool IsHidden { get; init; }
    public List<BudgetTransaction> Transactions { get; init; } = [];
    public DateOnly? InterestHistoryStart { get; init; }
    public decimal InterestBaseCredited { get; init; }
    public decimal InterestBasePending { get; init; }
    public InterestPayout InterestBasePayout { get; init; }
    public List<SavingsRate> InterestRates { get; init; } = [];
    public List<DateOnly> InterestSettlementDates { get; init; } = [];
    public List<SavingsAccrual> InterestCorrections { get; init; } = [];
    public TimeOnly? DailyInterestPostingTime { get; init; }
    public bool InitialInterestPosted { get; init; }

    [JsonIgnore] public bool IsCredit => Type == AccountType.CreditCard;
    [JsonIgnore] public bool IsSavings => Type == AccountType.Savings;
    [JsonIgnore] public bool CanAddPurchase => IsCredit && GraceRule != GracePeriodRule.Unspecified;
    [JsonIgnore] public decimal TransactionDelta => Transactions.Sum(t => t.SignedAmount);
    [JsonIgnore] public decimal CurrentBalance => IsCredit ? Debt : OpeningBalance + CreditedInterest + TransactionDelta;
    [JsonIgnore] public decimal OwnMoney => IsCredit ? 0 : CurrentBalance;
    [JsonIgnore] public decimal Debt => IsCredit ? OpeningBalance + Purchases.Sum(p => p.Amount) - TransactionDelta : 0;
    [JsonIgnore] public IEnumerable<PurchaseSummary> PurchaseSummaries => CreditGrace.OutstandingSummaries(this);
    [JsonIgnore] public IEnumerable<PurchaseSummary> PaymentSummaries => CreditGrace.PaymentSummaries(this);
    [JsonIgnore] public IEnumerable<AccountHistoryRow> History => Transactions.Select(t => new AccountHistoryRow(
        t.IsTransfer ? $"Перевод {(t.Kind == CategoryKind.Income ? "от" : "на")} «{t.CounterpartyAccountName}»" : t.CategoryName,
        t.OccurredAt, t.SignedAmount, t.IsTransfer ? "" : t.MerchantName))
        .Concat(Purchases.Select(p => new AccountHistoryRow("Покупка", p.Date.ToDateTime(p.Time), -p.Amount, p.Name)))
        .OrderByDescending(row => row.Date);
    [JsonIgnore] public string TypeLabel => Type switch
    {
        AccountType.Cash => "Наличные",
        AccountType.DebitCard => "Дебетовая карта",
        AccountType.CreditCard => "Кредитная карта",
        AccountType.Savings => "Накопительный счёт",
        _ => throw new ArgumentOutOfRangeException()
    };
    [JsonIgnore] public decimal AvailableBalance => IsCredit ? CreditLimit - Debt : CurrentBalance;
    [JsonIgnore] public string BalanceLabel => IsCredit ? "Доступный остаток" : "Остаток";
    [JsonIgnore] public string BalanceText => Money.Format(AvailableBalance);
    [JsonIgnore] public decimal? DisplayBudgetRemaining { get; init; }
    [JsonIgnore] public bool HasBudgetDisplay => DisplayBudgetRemaining is not null;
    [JsonIgnore] public string CardBalanceText => DisplayBudgetRemaining is { } remaining ? Money.Format(remaining) : BalanceText;
    [JsonIgnore] public string CardBalanceLabel => HasBudgetDisplay ? "Остаток лимита трат на месяц" : BalanceLabel;
    [JsonIgnore] public string FullCardBalanceText => $"На карте: {BalanceText}";
    [JsonIgnore] public string DebtText => $"Долг: {Money.Format(Debt)}";
    [JsonIgnore] public string VisibilityAction => IsHidden ? "Показать счёт" : "Скрыть счёт";
    [JsonIgnore] public string DetailText => IsCredit
        ? $"Лимит {Money.Format(CreditLimit)}"
        : IsSavings ? $"Ставка {AnnualInterestRate.ToString("0.####", CultureInfo.GetCultureInfo("ru-RU"))}% годовых · {(InterestPayout == InterestPayout.Daily ? "ежедневная выплата" : "выплата в конце месяца")}" : "Начальный остаток";
    [JsonIgnore] public string AdditionalText => IsSavings
        ? $"Зачислено процентов: {Money.Format(CreditedInterest)}" + (InterestPayout == InterestPayout.Monthly ? $" · к выплате: {Money.Format(PendingInterest)}" : "")
        : IsCredit ? CreditGrace.Description(this) : "";
    [JsonIgnore] public string DateText => $"Учёт с {OpeningDate:dd.MM.yyyy}";

    public void Validate()
    {
        if (OpeningDebtPayments is null || OpeningDebtPayments.Any(p => p is null || !IsCredit || p.DueDate == default || p.Amount <= 0 || decimal.Round(p.Amount, 2) != p.Amount) || OpeningDebtPayments.Sum(p => p.Amount) > OpeningBalance)
            throw new ArgumentException("Некорректный график начального долга.");
        if (StatementDay is < 0 or > 28 || (GraceRule == GracePeriodRule.StatementMonthEnd && StatementDay == 0) || (!IsCredit && StatementDay != 0)) throw new ArgumentException("День выписки: от 1 до 28.");
        if (Id == Guid.Empty) throw new ArgumentException("У счёта отсутствует идентификатор.");
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 80)
            throw new ArgumentException("Введите название счёта: от 1 до 80 символов.");
        if (!Enum.IsDefined(Type)) throw new ArgumentException("Выберите тип счёта.");
        Money.Validate(OpeningBalance, IsCredit ? "Долг" : "Начальный остаток");
        Money.Validate(CreditLimit, "Кредитный лимит");
        Money.Validate(MonthlySpendingLimit, "Лимит трат на месяц");
        if (!IsCredit && MonthlySpendingLimit != 0) throw new ArgumentException("Лимит трат задаётся только кредитке.");
        Money.Validate(CreditedInterest, "Зачисленные проценты");
        if (Purchases is null) throw new ArgumentException("Не удалось прочитать покупки по карте.");
        var purchaseIds = new HashSet<Guid>();
        foreach (var purchase in Purchases)
        {
            if (purchase is null) throw new ArgumentException("Пустая покупка в файле счетов.");
            purchase.Validate();
            if (purchase.Date < OpeningDate) throw new ArgumentException("Покупка должна быть не раньше начала учёта.");
            if (!purchaseIds.Add(purchase.Id)) throw new ArgumentException("Повторяющаяся покупка.");
        }
        if (!IsCredit && Purchases.Count != 0) throw new ArgumentException("Покупки по кредитке допустимы только для кредитной карты.");
        if (Transactions is null || InterestRates is null || InterestSettlementDates is null || InterestCorrections is null) throw new ArgumentException("Не удалось прочитать историю операций или ставок.");
        if (DailyInterestPostingTime is not null && !IsSavings) throw new ArgumentException("Время начисления задаётся только накопительному счёту.");
        if ((InterestCorrections.Count > 0 && InterestHistoryStart is null) || InterestCorrections.Any(c => c is null || !IsSavings || c.Date < InterestHistoryStart || (DailyInterestPostingTime is null ? c.Date >= DateOnly.FromDateTime(DateTime.Today) : c.Date > DateOnly.FromDateTime(DateTime.Today)) || c.Amount < 0 || c.Amount > 1_000_000_000_000m || decimal.Round(c.Amount, 2) != c.Amount) || InterestCorrections.Select(c => c.Date).Distinct().Count() != InterestCorrections.Count)
            throw new ArgumentException("Некорректная история уточнения процентов.");
        if (InterestCorrections.Any(c => c.OccurredAt is { } timestamp && (timestamp > DateTime.Now || DateOnly.FromDateTime(timestamp) < OpeningDate))) throw new ArgumentException("Некорректная дата поступления процентов.");
        var transactionIds = new HashSet<Guid>(purchaseIds);
        foreach (var transaction in Transactions)
        {
            if (transaction is null) throw new ArgumentException("Пустая операция.");
            transaction.Validate();
            if (!transactionIds.Add(transaction.Id)) throw new ArgumentException("Повторяющаяся операция.");
            if (DateOnly.FromDateTime(transaction.OccurredAt) < OpeningDate) throw new ArgumentException("Операция раньше начала учёта.");
            if (IsCredit && transaction.Kind == CategoryKind.Income && !transaction.IsTransfer) throw new ArgumentException("Погашение кредитки не является доходом.");
            if (transaction.IsTransfer && transaction.CounterpartyAccountId == Id) throw new ArgumentException("Перевод на тот же счёт.");
            if (!IsCredit && transaction.CreditRule != GracePeriodRule.Unspecified) throw new ArgumentException("Кредитные условия на обычном счёте.");
        }
        Money.Validate(InterestBaseCredited, "Ранее начисленные проценты");
        if (InterestBasePending < 0 || InterestBasePending > 1_000_000_000_000m) throw new ArgumentException("Неверная база процентов.");
        if (InterestHistoryStart is { } historyStart)
        {
            if (!IsSavings || historyStart < OpeningDate || InterestRates.Count == 0 || InterestRates.Any(r => r is null) || InterestRates.Min(r => r.From) != historyStart)
                throw new ArgumentException("Повреждена история ставок.");
            if (InterestRates.Any(r => r is null || r.From < historyStart || r.AnnualRate < 0 || r.AnnualRate > 100 || !Enum.IsDefined(r.Payout)))
                throw new ArgumentException("Недопустимая ставка в истории.");
            if (!Enum.IsDefined(InterestBasePayout) || InterestSettlementDates.Any(d => d < historyStart || d > DateOnly.FromDateTime(DateTime.Today)))
                throw new ArgumentException("Повреждена история выплат процентов.");
        }
        else if (InterestRates.Count != 0 || InterestSettlementDates.Count != 0 || InterestBaseCredited != 0 || InterestBasePending != 0) throw new ArgumentException("Отсутствует начало истории процентов.");
        Money.Validate(CurrentBalance, "Остаток с процентами");
        if (PendingInterest < 0 || PendingInterest > 1_000_000_000_000m)
            throw new ArgumentException("Неверная сумма ожидающих выплаты процентов.");
        if (AnnualInterestRate < 0 || AnnualInterestRate > 100 || decimal.Round(AnnualInterestRate, 4) != AnnualInterestRate)
            throw new ArgumentException("Годовая ставка: от 0 до 100%, не более четырёх знаков после запятой.");
        if (!Enum.IsDefined(InterestPayout) || !Enum.IsDefined(GraceRule))
            throw new ArgumentException("Неизвестное правило расчёта.");
        if (!IsSavings && (AnnualInterestRate != 0 || CreditedInterest != 0 || PendingInterest != 0 || InterestAsOf is not null))
            throw new ArgumentException("Проценты задаются только для накопительного счёта.");
        if (InterestPayout == InterestPayout.Daily && PendingInterest != 0)
            throw new ArgumentException("При ежедневной выплате не должно быть ожидающих процентов.");
        if (InterestAsOf is { } interestDate && interestDate < OpeningDate)
            throw new ArgumentException("Дата расчёта процентов раньше открытия счёта.");
        if (GraceDays < 0 || GraceDays > 730 || PaymentParts != 1 || GraceRule == GracePeriodRule.DaysThenMonthEnd)
            throw new ArgumentException("Льготный период: от 0 до 730 дней, одним платежом за месяц.");
        if (!IsCredit && (GraceDays != 0 || GraceRule != GracePeriodRule.Unspecified || PaymentParts != 1))
            throw new ArgumentException("Льготный период задаётся только для кредитки.");
        if (GraceRule != GracePeriodRule.Unspecified && GraceDays == 0)
            throw new ArgumentException("Укажите количество дней льготного периода.");
        if (IsCredit && Debt > CreditLimit)
            throw new ArgumentException("Долг не может превышать кредитный лимит.");
        if (!IsCredit && CreditLimit != 0)
            throw new ArgumentException("Лимит задаётся только для кредитной карты.");
        if (OpeningDate == default || OpeningDate > DateOnly.FromDateTime(DateTime.Today))
            throw new ArgumentException("Дата начала учёта должна быть не позднее сегодняшней.");
    }
}

public sealed record PurchaseSummary(string Heading, string Payments);
public sealed record OpeningDebtPayment(DateOnly DueDate, decimal Amount);
public sealed record AccountHistoryRow(string Heading, DateTime Date, decimal Amount, string Detail)
{
    public string DateText => Date.ToString("dd.MM.yyyy HH:mm");
    public string AmountText => (Amount >= 0 ? "+ " : "− ") + Money.Format(Math.Abs(Amount));
    public bool IsIncoming => Amount >= 0;
}

public static class Money
{
    public static string Format(decimal value) => value.ToString("N2", CultureInfo.GetCultureInfo("ru-RU")) + " ₽";

    public static bool TryParse(string text, out decimal value)
    {
        // Group separators are intentionally rejected to avoid ambiguous amounts.
        return decimal.TryParse(text.Trim().Replace(',', '.'),
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out value);
    }

    public static void Validate(decimal value, string label)
    {
        if (value < 0 || value > 1_000_000_000_000m || decimal.Round(value, 2) != value)
            throw new ArgumentException($"{label}: укажите сумму от 0 до 1 трлн ₽, не более двух знаков после запятой.");
    }
}

public sealed record AccountTotals(decimal OwnMoney, decimal Debt)
{
    public decimal Net => OwnMoney - Debt;
    public static AccountTotals From(IEnumerable<Account> accounts)
    {
        var list = accounts.ToList();
        return new(list.Sum(a => a.OwnMoney), list.Sum(a => a.Debt));
    }
}
