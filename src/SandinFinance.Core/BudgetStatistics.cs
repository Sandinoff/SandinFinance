using System.Globalization;

namespace SandinFinance.Core;

public enum BudgetPeriod { Day, Week, Month, Year, Custom }
public sealed record DateRange(DateOnly Start, DateOnly End)
{
    public bool Contains(DateTime date) => DateOnly.FromDateTime(date) >= Start && DateOnly.FromDateTime(date) <= End;
    public static DateRange For(BudgetPeriod period, DateOnly anchor, DateOnly? start = null, DateOnly? end = null)
    {
        var monthStart = new DateOnly(anchor.Year, anchor.Month, 1);
        var monday = anchor.AddDays(-((int)anchor.DayOfWeek + 6) % 7);
        DateRange range = period switch
        {
            BudgetPeriod.Day => new(anchor, anchor),
            BudgetPeriod.Week => new(monday, monday.AddDays(6)),
            BudgetPeriod.Month => new(monthStart, new(anchor.Year, anchor.Month, DateTime.DaysInMonth(anchor.Year, anchor.Month))),
            BudgetPeriod.Year => new(new(anchor.Year, 1, 1), new(anchor.Year, 12, 31)),
            BudgetPeriod.Custom when start is not null && end is not null => new(start.Value, end.Value),
            _ => throw new ArgumentException("Выберите начало и конец периода.")
        };
        if (range.Start > range.End) throw new ArgumentException("Начало периода должно быть не позже его конца.");
        return range;
    }
}

public sealed record BudgetEntry(Guid Id, CategoryKind Kind, decimal Amount, DateTime OccurredAt, string AccountName,
    Guid CategoryId, string CategoryName, string ColorHex, string IconGlyph, string MerchantName, string? PngIconBase64 = null, Guid AccountId = default, bool IsTransfer = false, bool IsInterest = false, DateOnly? InterestDate = null, bool IsExternalTransfer = false)
{
    public bool CanDeleteExpense => Kind == CategoryKind.Expense && !IsTransfer && !IsInterest;
    public bool CanMakeRecurring => CanDeleteExpense;
    public bool CanEdit => !IsExternalTransfer;
    public string AmountText => Money.Format(Amount);
    public string DateText => OccurredAt.ToString("dd.MM.yyyy HH:mm", CultureInfo.GetCultureInfo("ru-RU"));
    public string DetailText => IsInterest ? $"{AccountName} · Начисленные проценты" : $"{AccountName}" + (string.IsNullOrEmpty(MerchantName) ? "" : $" · {MerchantName}");
    public string IconLabel => string.IsNullOrEmpty(MerchantName) ? IconGlyph : System.Globalization.StringInfo.GetNextTextElement(MerchantName);
}

public sealed record MerchantTotal(string Name, decimal Amount, int Count, string? PngIconBase64 = null)
{
    public string AmountText => Money.Format(Amount);
    public string IconLabel => System.Globalization.StringInfo.GetNextTextElement(Name);
}

public sealed record CategoryTotal(Guid Id, string Name, string ColorHex, string IconGlyph, decimal Amount, decimal Share,
    int Count, IReadOnlyList<MerchantTotal> Merchants)
{
    public string AmountText => Money.Format(Amount);
    public string ShareText => Share.ToString("0.#", CultureInfo.GetCultureInfo("ru-RU")) + "%";
    public string DetailText => $"{ShareText} · операций: {Count}";
    public string Tooltip => $"{Name}\n{AmountText} · {DetailText}\n" + string.Join("\n", Merchants.Take(5).Select(m => $"{m.Name}: {m.AmountText}"));
}

public sealed record BudgetSummary(decimal Total, IReadOnlyList<CategoryTotal> Categories, IReadOnlyList<BudgetEntry> Entries);

public static class BudgetStatistics
{
    public static readonly Guid AutomaticInterestCategoryId = new("c72a338b-b3d2-4e47-918f-d61f96b26a33");

    public static BudgetSummary Calculate(IEnumerable<Account> accounts, IEnumerable<Category> categories, CategoryKind kind, DateRange range)
    {
        var catalog = categories.ToDictionary(c => c.Id);
        var interestCategory = catalog.Values.FirstOrDefault(c => c.Kind == CategoryKind.Income && c.Name.Trim().Equals("Проценты", StringComparison.OrdinalIgnoreCase))
            ?? new Category { Id = AutomaticInterestCategoryId, Kind = CategoryKind.Income, Name = "Проценты", IconKey = "interest", ColorHex = "#789BD4" };
        var entries = accounts.SelectMany(account => account.Transactions.Select(transaction =>
        {
            catalog.TryGetValue(transaction.CategoryId, out var category);
            var merchant = category?.Merchants.FirstOrDefault(m => m.Id == transaction.MerchantId)
                ?? category?.Merchants.FirstOrDefault(m => string.Equals(m.Name, transaction.MerchantName, StringComparison.OrdinalIgnoreCase));
            return new BudgetEntry(transaction.Id, transaction.Kind, transaction.Amount, transaction.OccurredAt, account.Name,
                transaction.CategoryId, transaction.IsTransfer ? $"Перевод {(transaction.Kind == CategoryKind.Expense ? "на" : "от")} «{transaction.CounterpartyAccountName}»" : category?.Name ?? transaction.CategoryName, category?.ColorHex ?? transaction.CategoryColor,
                category?.IconGlyph ?? CategoryAppearance.Icons.Single(i => i.Key == transaction.CategoryIcon).Glyph, transaction.MerchantName, merchant?.PngIconBase64, account.Id, transaction.IsTransfer || transaction.IsExternalTransfer, IsExternalTransfer: transaction.IsExternalTransfer);
        }).Concat(account.Purchases.Select(p => new BudgetEntry(p.Id, CategoryKind.Expense, p.Amount,
            p.Date.ToDateTime(p.Time), account.Name, Guid.Empty, "Без категории", "#607780", "★", p.Name, AccountId: account.Id)))
            .Concat(account.IsSavings ? SavingsInterest.History(account, DateOnly.FromDateTime(DateTime.Today)).Where(a => a.Amount > 0).Select(a => new BudgetEntry(Guid.Empty, CategoryKind.Income, a.Amount, a.OccurredAt ?? a.Date.AddDays(1).ToDateTime(TimeOnly.MinValue), account.Name, interestCategory.Id, interestCategory.Name, interestCategory.ColorHex, interestCategory.IconGlyph, account.Name, AccountId: account.Id, IsInterest: true, InterestDate: a.Date)) : []))
            .Where(e => e.Kind == kind && range.Contains(e.OccurredAt)).OrderByDescending(e => e.OccurredAt).ToList();
        var statistical = entries.Where(e => !e.IsTransfer && e.Amount > 0).ToList();
        var total = statistical.Sum(e => e.Amount);
        var groups = statistical.GroupBy(e => e.CategoryId).Select(group =>
        {
            var first = group.First();
            var amount = group.Sum(e => e.Amount);
            var merchants = group.GroupBy(e => string.IsNullOrEmpty(e.MerchantName) ? "Не указан" : e.MerchantName, StringComparer.OrdinalIgnoreCase)
                .Select(m => new MerchantTotal(m.Key, m.Sum(e => e.Amount), m.Count(), m.FirstOrDefault(e => e.PngIconBase64 is not null)?.PngIconBase64)).OrderByDescending(m => m.Amount).ThenBy(m => m.Name).ToList();
            return new CategoryTotal(first.CategoryId, first.CategoryName, first.ColorHex, first.IconGlyph, amount,
                total == 0 ? 0 : amount / total * 100m, group.Count(), merchants);
        }).OrderByDescending(c => c.Amount).ThenBy(c => c.Name).ToList();
        return new(total, groups, entries);
    }
}
