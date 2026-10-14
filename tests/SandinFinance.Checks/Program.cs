using SandinFinance.Core;

try
{
var passed = 0;
var root = Path.Combine(Directory.GetCurrentDirectory(), ".local", "checks", Guid.NewGuid().ToString("N"));
var today = DateOnly.FromDateTime(DateTime.Today);
Account Cash(string name = "Наличные", decimal amount = 5000.50m) =>
    new() { Name = name, Type = AccountType.Cash, OpeningBalance = amount, OpeningDate = today };
var credit = Cash("Кредитка", 2500.25m) with { Type = AccountType.CreditCard, CreditLimit = 100000m };

Check("Own funds exclude credit limit; debt reduces net worth", () =>
{
    var totals = AccountTotals.From([Cash(), Cash("Накопления", 10000m) with { Type = AccountType.Savings }, credit]);
    Assert(totals.OwnMoney == 15000.50m && totals.Debt == 2500.25m && totals.Net == 12500.25m);
    Assert(AccountTotals.From([credit]).Net == -2500.25m);
});
Check("Amounts accept comma and dot without interpreting thousands separators", () =>
{
    Assert(Money.TryParse("5000,50", out var comma) && comma == 5000.50m);
    Assert(Money.TryParse(" 5000.50 ", out var dot) && dot == comma);
    Assert(!Money.TryParse("1,000.50", out _) && !Money.TryParse("1 000", out _));
    Assert(!Money.TryParse("NaN", out _) && !Money.TryParse("1e3", out _));
});
Check("Invalid balances, dates, names and credit limits are rejected", () =>
{
    Reject<ArgumentException>(() => Cash(amount: -1).Validate());
    Reject<ArgumentException>(() => Cash(amount: 0.001m).Validate());
    Reject<ArgumentException>(() => Cash(amount: 1_000_000_000_001m).Validate());
    Reject<ArgumentException>(() => Cash(" ").Validate());
    Reject<ArgumentException>(() => (Cash() with { OpeningDate = today.AddDays(1) }).Validate());
    Reject<ArgumentException>(() => (Cash() with { CreditLimit = 10 }).Validate());
    Reject<ArgumentException>(() => (credit with { CreditLimit = 2000 }).Validate());
    (credit with { OpeningBalance = 0, CreditLimit = 0 }).Validate();
    Cash(amount: 0).Validate();
});
Check("Saving and reopening preserves all four types and kopecks", () =>
{
    var store = new AccountStore(Path.Combine(root, "roundtrip"));
    Assert(store.Load().Count == 0);
    Account[] accounts = [Cash(), Cash("Дебетовая", 123.45m) with { Type = AccountType.DebitCard }, credit,
        Cash("Накопительный", 0) with { Type = AccountType.Savings }];
    store.Save(accounts);
    Assert(System.Text.Json.JsonSerializer.Serialize(new AccountStore(Path.GetDirectoryName(store.FilePath)!).Load()) == System.Text.Json.JsonSerializer.Serialize(accounts));
    store.Save([.. accounts, Cash("Вторая наличность", 1)]);
    Assert(store.Load().Count == 5 && File.Exists(store.BackupPath));
    Assert(!File.Exists(store.FilePath + ".tmp"));
});
Check("Duplicate account names are rejected without overwriting data", () =>
{
    var store = new AccountStore(Path.Combine(root, "duplicates"));
    var original = Cash();
    store.Save([original]);
    var content = File.ReadAllText(store.FilePath);
    Reject<ArgumentException>(() => store.Save([original, Cash(" наличные ")]));
    Reject<ArgumentException>(() => store.Save([original, original with { Name = "Другой" }]));
    Assert(File.ReadAllText(store.FilePath) == content);
});
Check("Malformed and unsupported files fail visibly", () =>
{
    var store = new AccountStore(Path.Combine(root, "corrupt"));
    Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
    File.WriteAllText(store.FilePath, "broken json");
    Reject<System.Text.Json.JsonException>(() => store.Load());
    Assert(File.ReadAllText(store.FilePath) == "broken json");
    File.WriteAllText(store.FilePath, "{\"Version\":99,\"Accounts\":[]}");
    Reject<InvalidDataException>(() => store.Load());
});
Check("Daily interest compounds once per completed day, including leap years", () =>
{
    var savings = Cash("Проценты", 10000) with { Type = AccountType.Savings, OpeningDate = new(2024, 2, 28), AnnualInterestRate = 36.6m };
    var oneDay = SavingsInterest.Accrue(savings, new(2024, 2, 29));
    Assert(oneDay.CreditedInterest == 10m && oneDay.CurrentBalance == 10010m);
    var twoDays = SavingsInterest.Accrue(savings, new(2024, 3, 1));
    Assert(twoDays.CreditedInterest == 20.01m);
    Assert(System.Text.Json.JsonSerializer.Serialize(SavingsInterest.Accrue(oneDay, new(2024, 3, 1))) == System.Text.Json.JsonSerializer.Serialize(twoDays));
    Assert(SavingsInterest.Accrue(twoDays, new(2024, 3, 1)) == twoDays);
    Assert(SavingsInterest.Accrue(twoDays, new(2024, 2, 29)) == twoDays);
    Assert(SavingsInterest.Accrue(savings, savings.OpeningDate).CreditedInterest == 0);
    var yearEnd = savings with { OpeningDate = new(2023, 12, 31), AnnualInterestRate = 36.5m };
    Assert(SavingsInterest.Accrue(yearEnd, new(2024, 1, 2)).CreditedInterest == 19.98m);
});
Check("Rate edits keep past earnings and apply the new rate from effective date", () =>
{
    var savings = Cash("Ставка", 10000) with { Type = AccountType.Savings, OpeningDate = new(2023, 1, 1), AnnualInterestRate = 36.5m };
    var changed = SavingsInterest.ChangeSettings(savings, 73m, InterestPayout.Daily, new(2023, 1, 2));
    Assert(changed.CreditedInterest == 10m);
    Assert(SavingsInterest.Accrue(changed, new(2023, 1, 3)).CreditedInterest == 30.02m);
    Reject<ArgumentException>(() => SavingsInterest.ChangeSettings(changed, 10m, InterestPayout.Daily, new(2023, 1, 1)));
    Reject<ArgumentException>(() => (savings with { AnnualInterestRate = -1m }).Validate());
    Reject<ArgumentException>(() => (savings with { AnnualInterestRate = 100.01m }).Validate());
});
Check("Monthly payouts keep pending interest separate and settle at month end", () =>
{
    var savings = Cash("Ежемесячный", 10000) with { Type = AccountType.Savings, OpeningDate = new(2023, 1, 30), AnnualInterestRate = 36.5m, InterestPayout = InterestPayout.Monthly };
    var pending = SavingsInterest.Accrue(savings, new(2023, 1, 31));
    Assert(pending.CreditedInterest == 0 && pending.PendingInterest == 10m && pending.OwnMoney == 10000m);
    var paid = SavingsInterest.Accrue(pending, new(2023, 2, 1));
    Assert(paid.CreditedInterest == 20m && paid.PendingInterest == 0);
    Assert(SavingsInterest.ChangeSettings(pending, 36.5m, InterestPayout.Daily, new(2023, 1, 31)).CreditedInterest == 10m);
});
Check("Version 1 accounts migrate without losing opening balances or identities", () =>
{
    var store = new AccountStore(Path.Combine(root, "migration"));
    Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
    var old = Cash("Старый накопительный", 10000) with { Type = AccountType.Savings };
    File.WriteAllText(store.FilePath, System.Text.Json.JsonSerializer.Serialize(new { Version = 1, Accounts = new[] {
        new { old.Id, old.Name, Type = old.Type.ToString(), old.OpeningBalance, old.CreditLimit, old.OpeningDate }
    } }));
    var migrated = store.LoadAndAccrue(today).Single();
    Assert(migrated.Id == old.Id && migrated.OpeningBalance == old.OpeningBalance && migrated.CreditedInterest == 0);
    var bytes = File.ReadAllText(store.FilePath);
    store.LoadAndAccrue(today);
    Assert(bytes == File.ReadAllText(store.FilePath));
    Assert(File.ReadAllText(store.BackupPath).Contains("\"Version\":1"));
});
Check("All purchases in a month share one due date based on the month closing", () =>
{
    var purchase = new CreditPurchase { Name = "Покупка", Amount = 100.01m, Date = new(2025, 10, 10), Rule = GracePeriodRule.EndOfNextMonth, GraceDays = 55 };
    var nextMonth = CreditGrace.Schedule(purchase);
    Assert(nextMonth.Single().DueDate == new DateOnly(2025, 11, 30) && nextMonth.Single().Amount == 100.01m);
    var longPurchase = purchase with { Rule = GracePeriodRule.MonthEndPlusDays, GraceDays = 120 };
    var payments = CreditGrace.Schedule(longPurchase);
    Assert(payments.Single().DueDate == new DateOnly(2026, 2, 28) && payments.Single().Amount == 100.01m);
    var earlyMarch = longPurchase with { Date = new(2025, 3, 1) };
    var lateMarch = earlyMarch with { Id = Guid.NewGuid(), Date = new(2025, 3, 31), Amount = 200m };
    Assert(CreditGrace.Schedule(earlyMarch).Single().DueDate == new DateOnly(2025, 7, 31));
    Assert(CreditGrace.Schedule(lateMarch).Single().DueDate == new DateOnly(2025, 7, 31));
    var summaries = CreditGrace.MonthlySummaries([earlyMarch, lateMarch, earlyMarch with { Id = Guid.NewGuid(), Date = new(2025, 4, 1) }]).ToList();
    Assert(summaries.Count == 2 && summaries[0].Payments.Contains(Money.Format(300.01m)) && summaries[1].Payments.Contains("31.08.2025"));
    Reject<ArgumentException>(() => (longPurchase with { PaymentParts = 4 }).Validate());
    var leap = purchase with { Date = new(2024, 1, 15) };
    Assert(CreditGrace.Schedule(leap).Single().DueDate == new DateOnly(2024, 2, 29));
});
Check("Purchase debts affect totals and retain their original schedule after card edits", () =>
{
    var purchase = new CreditPurchase { Name = "Покупка", Amount = 100, Date = today, Rule = GracePeriodRule.EndOfNextMonth, GraceDays = 55 };
    var card = credit with { Purchases = [purchase], GraceDays = 55, GraceRule = GracePeriodRule.EndOfNextMonth };
    Assert(card.Debt == 2600.25m && AccountTotals.From([card]).Debt == 2600.25m);
    Reject<ArgumentException>(() => (card with { CreditLimit = 2600 }).Validate());
    var edited = card with { Name = "Новое название", GraceDays = 120, GraceRule = GracePeriodRule.MonthEndPlusDays };
    Assert(CreditGrace.Schedule(edited.Purchases.Single()).SequenceEqual(CreditGrace.Schedule(purchase)));
    var store = new AccountStore(Path.Combine(root, "purchases"));
    store.Save([edited]);
    Assert(store.Load().Single().Purchases.Single() == purchase);
});
Check("Version 2 installment settings upgrade to a single monthly payment without changing debt", () =>
{
    var store = new AccountStore(Path.Combine(root, "old-credit-rule"));
    Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
    var oldPurchase = new CreditPurchase { Name = "Старая покупка", Amount = 100.01m, Date = today,
        Rule = GracePeriodRule.DaysThenMonthEnd, GraceDays = 120, PaymentParts = 4 };
    var oldCard = credit with { GraceDays = 120, GraceRule = GracePeriodRule.DaysThenMonthEnd, PaymentParts = 4, Purchases = [oldPurchase] };
    var options = new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    File.WriteAllText(store.FilePath, System.Text.Json.JsonSerializer.Serialize(new { Version = 2, Accounts = new[] { oldCard } }, options));
    var upgraded = store.LoadAndAccrue(today).Single();
    Assert(upgraded.Id == oldCard.Id && upgraded.Debt == oldCard.Debt && upgraded.CreditLimit == oldCard.CreditLimit);
    Assert(upgraded.GraceRule == GracePeriodRule.MonthEndPlusDays && upgraded.PaymentParts == 1);
    Assert(upgraded.Purchases.Single().Id == oldPurchase.Id && CreditGrace.Schedule(upgraded.Purchases.Single()).Count == 1);
    Assert(File.Exists(store.BackupPath) && File.ReadAllText(store.FilePath).Contains("\"Version\": 8"));
});
Check("Credit card primary amount is the available balance, without changing financial totals", () =>
{
    Assert(credit.AvailableBalance == 97499.75m && credit.BalanceText == Money.Format(97499.75m));
    Assert(credit.DebtText.Contains(Money.Format(2500.25m)) && credit.OwnMoney == 0);
    Assert(Cash().AvailableBalance == 5000.50m);
});
Check("Reordering and hiding persist all accounts and do not change balances", () =>
{
    var first = Cash("Первый");
    var hidden = Cash("Скрытый", 10) with { IsHidden = true };
    var third = Cash("Третий", 20);
    var fourth = credit;
    Account[] original = [first, hidden, third, fourth];
    var moved = AccountArrangement.Move(original, fourth.Id, first.Id, after: false);
    Assert(moved.Select(a => a.Id).SequenceEqual(new[] { fourth.Id, first.Id, hidden.Id, third.Id }));
    moved = AccountArrangement.Move(moved, first.Id, third.Id, after: true);
    Assert(moved.Select(a => a.Id).SequenceEqual(new[] { fourth.Id, hidden.Id, third.Id, first.Id }));
    Assert(AccountTotals.From(moved) == AccountTotals.From(original));
    var store = new AccountStore(Path.Combine(root, "arrangement"));
    store.Save(moved);
    var reopened = store.LoadAndAccrue(today);
    Assert(reopened.Select(a => a.Id).SequenceEqual(moved.Select(a => a.Id)) && reopened[1].IsHidden);
    Assert(AccountArrangement.Move(reopened, first.Id, first.Id, true).SequenceEqual(reopened));
    Reject<ArgumentException>(() => AccountArrangement.Move(reopened, Guid.NewGuid(), first.Id, false));
});
Check("Expense and income categories persist colors, icons and merchant identities", () =>
{
    var directory = Path.Combine(root, "categories");
    var store = new CategoryStore(directory);
    Assert(store.Load().Count == 0);
    var merchant = new Merchant { Name = "Пятёрочка" };
    var expense = new Category { Name = "Продукты", Kind = CategoryKind.Expense, ColorHex = "#2478A8", IconKey = "food", Merchants = [merchant] };
    var income = new Category { Name = "Продукты", Kind = CategoryKind.Income, IconKey = "work", Merchants = [new() { Name = "Работодатель" }] };
    var accountStore = new AccountStore(directory);
    accountStore.Save([Cash()]);
    var accountBytes = File.ReadAllText(accountStore.FilePath);
    store.Save([expense, income]);
    var loaded = new CategoryStore(directory).Load();
    Assert(loaded.Count == 2 && loaded[0].Id == expense.Id && loaded[0].Merchants.Single().Id == merchant.Id);
    Assert(loaded[0].ColorHex == "#2478A8" && loaded[0].IconKey == "food" && loaded[1].Kind == CategoryKind.Income);
    store.Save([expense with { Name = "Питание", ColorHex = "#BC4545" }, income]);
    Assert(store.Load()[0].Merchants.Single().Id == merchant.Id && File.Exists(store.BackupPath));
    Assert(File.ReadAllText(accountStore.FilePath) == accountBytes);
});
Check("Invalid categories and duplicate merchants do not overwrite saved data", () =>
{
    var store = new CategoryStore(Path.Combine(root, "invalid-categories"));
    var expense = new Category { Name = "Продукты", Merchants = [new() { Name = "Магазин" }] };
    store.Save([expense]);
    var bytes = File.ReadAllText(store.FilePath);
    Reject<ArgumentException>(() => store.Save([expense, expense with { Id = Guid.NewGuid(), Name = " продукты " }]));
    Reject<ArgumentException>(() => (expense with { ColorHex = "red" }).Validate());
    Reject<ArgumentException>(() => (expense with { IconKey = "unknown" }).Validate());
    Reject<ArgumentException>(() => (expense with { Name = " " }).Validate());
    Reject<ArgumentException>(() => (expense with { Merchants = [.. expense.Merchants, new() { Name = " магазин " }] }).Validate());
    Assert(File.ReadAllText(store.FilePath) == bytes);
    File.WriteAllText(store.FilePath, "broken json");
    Reject<System.Text.Json.JsonException>(() => store.Load());
    Assert(File.ReadAllText(store.FilePath) == "broken json");
});
Check("Expense and income update the balance atomically, preserve categories and reject overspending", () =>
{
    var directory = Path.Combine(root, "operations");
    var store = new AccountStore(directory);
    var catalog = new CategoryStore(directory);
    var expense = new Category { Name = "Продукты", Merchants = [new() { Name = "Магнит" }] };
    var income = new Category { Name = "Зарплата", Kind = CategoryKind.Income, IconKey = "work" };
    catalog.Save([expense, income]);
    var account = Cash("Бюджет", 100) with { OpeningDate = today.AddDays(-2) };
    store.Save([account]);
    var timestamp = today.AddDays(-1).ToDateTime(new(12, 0));
    var spent = BudgetOperations.Add(store, catalog, account.Id, expense.Id, CategoryKind.Expense, 25.50m, timestamp, "Магнит");
    Assert(store.Load().Single().CurrentBalance == 74.50m && spent.MerchantId == expense.Merchants.Single().Id);
    BudgetOperations.Add(store, catalog, account.Id, income.Id, CategoryKind.Income, 50m, timestamp.AddHours(1), "Работа");
    var saved = store.Load().Single();
    Assert(saved.CurrentBalance == 124.50m && saved.Transactions.Count == 2);
    var bytes = File.ReadAllText(store.FilePath);
    Reject<ArgumentException>(() => BudgetOperations.Add(store, catalog, account.Id, expense.Id, CategoryKind.Expense, 200m, timestamp.AddHours(2), ""));
    Reject<ArgumentException>(() => BudgetOperations.Add(store, catalog, account.Id, income.Id, CategoryKind.Expense, 1m, timestamp, ""));
    Assert(bytes == File.ReadAllText(store.FilePath));
});
Check("A later deposit cannot fund an earlier expense; credit expenses affect debt and grace schedules once", () =>
{
    var directory = Path.Combine(root, "timeline");
    var store = new AccountStore(directory);
    var catalog = new CategoryStore(directory);
    var expense = new Category { Name = "Еда" };
    var income = new Category { Name = "Доход", Kind = CategoryKind.Income };
    catalog.Save([expense, income]);
    var cash = Cash(amount: 100) with { OpeningDate = today.AddDays(-2) };
    var card = credit with { OpeningDate = today.AddDays(-2), GraceRule = GracePeriodRule.MonthEndPlusDays, GraceDays = 120 };
    store.Save([cash, card]);
    BudgetOperations.Add(store, catalog, cash.Id, income.Id, CategoryKind.Income, 100m, today.AddDays(-1).ToDateTime(new(12, 0)), "");
    Reject<ArgumentException>(() => BudgetOperations.Add(store, catalog, cash.Id, expense.Id, CategoryKind.Expense, 150m, today.AddDays(-2).ToDateTime(new(12, 0)), ""));
    BudgetOperations.Add(store, catalog, card.Id, expense.Id, CategoryKind.Expense, 100m, today.AddDays(-1).ToDateTime(new(12, 0)), "Магазин");
    var saved = store.Load().Single(a => a.Id == card.Id);
    Assert(saved.Debt == 2600.25m && saved.AvailableBalance == 97399.75m && saved.Purchases.Count == 0 && saved.PurchaseSummaries.Count() == 1);
});
Check("Backdated savings operations replay interest and preserved rate changes without duplication", () =>
{
    var directory = Path.Combine(root, "savings-operations");
    var store = new AccountStore(directory);
    var catalog = new CategoryStore(directory);
    var expense = new Category { Name = "Расход" };
    var income = new Category { Name = "Доход", Kind = CategoryKind.Income };
    catalog.Save([expense, income]);
    var start = today.AddDays(-2);
    var savings = Cash("Накопительный", 10000) with { Type = AccountType.Savings, OpeningDate = start, AnnualInterestRate = (DateTime.IsLeapYear(start.Year) ? 366m : 365m) / 10m };
    store.Save([savings]);
    BudgetOperations.Add(store, catalog, savings.Id, income.Id, CategoryKind.Income, 10000m, start.ToDateTime(new(12, 0)), "");
    Assert(store.Load().Single().CreditedInterest == 40.02m);
    BudgetOperations.Add(store, catalog, savings.Id, expense.Id, CategoryKind.Expense, 5000m, start.AddDays(1).ToDateTime(new(12, 0)), "");
    var recalculated = store.Load().Single();
    Assert(recalculated.CreditedInterest == 35.02m && recalculated.CurrentBalance == 15035.02m);
    Assert(store.LoadAndAccrue(today).Single().CreditedInterest == 35.02m);
    var rates = SavingsInterest.ChangeSettings(savings with { OpeningDate = new(2023, 1, 1), AnnualInterestRate = 36.5m }, 73m, InterestPayout.Daily, new(2023, 1, 2));
    var deposit = new BudgetTransaction { Kind = CategoryKind.Income, Amount = 10000, OccurredAt = new(2023, 1, 1, 12, 0, 0), CategoryId = income.Id, CategoryName = income.Name };
    Assert(SavingsInterest.Recalculate(rates with { Transactions = [deposit] }, new(2023, 1, 3)).CreditedInterest == 60.04m);
    var another = savings with { Id = Guid.NewGuid(), Name = "Проценты можно тратить" };
    store.Save([another]);
    BudgetOperations.Add(store, catalog, another.Id, expense.Id, CategoryKind.Expense, 10010m, DateTime.Today, "");
    Assert(store.Load().Single().CurrentBalance == 10.01m);
    var monthlyBase = savings with { OpeningDate = today, InterestAsOf = today, InterestPayout = InterestPayout.Monthly, PendingInterest = 10m };
    var dailyChanged = SavingsInterest.ChangeSettings(monthlyBase, 15m, InterestPayout.Daily, today);
    Assert(SavingsInterest.Recalculate(dailyChanged, today).CreditedInterest == 10m);
    var monthlyAgain = SavingsInterest.ChangeSettings(dailyChanged, 15m, InterestPayout.Monthly, today);
    Assert(SavingsInterest.Recalculate(monthlyAgain, today).CreditedInterest == 10m);
});
Check("Periods include boundary days; analytics sort categories and split stores without counting opening funds", () =>
{
    var week = DateRange.For(BudgetPeriod.Week, new(2025, 1, 1));
    Assert(week.Start == new DateOnly(2024, 12, 30) && week.End == new DateOnly(2025, 1, 5));
    Assert(DateRange.For(BudgetPeriod.Month, new(2024, 2, 10)).End == new DateOnly(2024, 2, 29));
    Reject<ArgumentException>(() => DateRange.For(BudgetPeriod.Custom, today, today, today.AddDays(-1)));
    var food = new Category { Name = "Питание", IconKey = "food", ColorHex = "#2478A8" };
    var travel = new Category { Name = "Поездки", IconKey = "car" };
    var at = new DateTime(2025, 1, 2, 12, 30, 0);
    BudgetTransaction Entry(Guid id, string name, decimal amount, string merchant) => new() { CategoryId = id, CategoryName = name, Amount = amount, OccurredAt = at, MerchantName = merchant };
    var account = Cash() with { OpeningDate = new(2025, 1, 1), Transactions = [Entry(food.Id, "Прежнее название", 30, "Магнит"), Entry(food.Id, "Прежнее название", 50, "Лента"), Entry(travel.Id, travel.Name, 20, "Такси")] };
    var summary = BudgetStatistics.Calculate([account], [food, travel], CategoryKind.Expense, new(new(2025, 1, 2), new(2025, 1, 2)));
    Assert(summary.Total == 100 && summary.Categories[0].Name == "Питание" && summary.Categories[0].Share == 80m && summary.Categories[0].Merchants.Count == 2);
    Assert(summary.Categories[0].Tooltip.Contains("Лента") && summary.Categories[1].Share == 20m);
    Assert(BudgetStatistics.Calculate([account], [food, travel], CategoryKind.Income, week).Total == 0);
    Assert(BudgetStatistics.Calculate([account], [food, travel], CategoryKind.Expense, new(new(2025, 1, 3), new(2025, 1, 3))).Total == 0);
});
Check("Legacy savings earnings remain a fixed base and earlier unknown rate history is not invented", () =>
{
    var directory = Path.Combine(root, "legacy-savings-base");
    var store = new AccountStore(directory);
    var catalog = new CategoryStore(directory);
    var income = new Category { Name = "Пополнение", Kind = CategoryKind.Income };
    catalog.Save([income]);
    var legacy = Cash("Старый накопительный", 10000) with
    {
        Type = AccountType.Savings, OpeningDate = today.AddDays(-5), InterestAsOf = today,
        AnnualInterestRate = 15m, CreditedInterest = 123.45m
    };
    store.Save([legacy]);
    BudgetOperations.Add(store, catalog, legacy.Id, income.Id, CategoryKind.Income, 100m, DateTime.Today, "");
    var updated = store.Load().Single();
    Assert(updated.CreditedInterest == 123.45m && updated.CurrentBalance == 10223.45m && updated.InterestHistoryStart == today);
    var bytes = File.ReadAllText(store.FilePath);
    Reject<ArgumentException>(() => BudgetOperations.Add(store, catalog, legacy.Id, income.Id, CategoryKind.Income, 1m,
        today.AddDays(-1).ToDateTime(new(12, 0)), ""));
    Assert(File.ReadAllText(store.FilePath) == bytes);
});
Check("Transfers persist a linked minus and plus atomically without becoming expense or income", () =>
{
    var directory = Path.Combine(root, "transfers");
    var store = new AccountStore(directory);
    var from = Cash("Откуда", 1000) with { OpeningDate = today.AddDays(-2) };
    var to = Cash("Куда", 200) with { Type = AccountType.DebitCard, OpeningDate = today.AddDays(-2) };
    store.Save([from, to]);
    var id = AccountTransfers.Add(store, from.Id, to.Id, 100.25m, today.AddDays(-1).ToDateTime(new(12, 0)));
    var saved = store.Load();
    Assert(saved[0].CurrentBalance == 899.75m && saved[1].CurrentBalance == 300.25m && AccountTotals.From(saved).OwnMoney == 1200);
    Assert(saved.All(a => a.Transactions.Single().TransferId == id));
    Assert(saved[0].History.Single().AmountText.StartsWith("−") && saved[1].History.Single().AmountText.StartsWith("+"));
    var period = DateRange.For(BudgetPeriod.Custom, today, today.AddDays(-2), today);
    Assert(BudgetStatistics.Calculate(saved, [], CategoryKind.Expense, period).Total == 0 && BudgetStatistics.Calculate(saved, [], CategoryKind.Income, period).Total == 0);
    var bytes = File.ReadAllText(store.FilePath);
    Reject<ArgumentException>(() => AccountTransfers.Add(store, from.Id, from.Id, 1, DateTime.Today));
    Reject<ArgumentException>(() => AccountTransfers.Add(store, from.Id, to.Id, 10000, DateTime.Today));
    Reject<ArgumentException>(() => store.Save([saved[0]]));
    Assert(File.ReadAllText(store.FilePath) == bytes);
});
Check("Credit transfers get purchase grace periods and repayment reduces the outstanding schedule", () =>
{
    var store = new AccountStore(Path.Combine(root, "credit-transfers"));
    var card = Cash("Кредитка", 0) with { Type = AccountType.CreditCard, CreditLimit = 1000, GraceDays = 120, GraceRule = GracePeriodRule.MonthEndPlusDays, OpeningDate = today.AddDays(-2) };
    var cash = Cash("Наличные", 20) with { OpeningDate = today.AddDays(-2) };
    store.Save([card, cash]);
    AccountTransfers.Add(store, card.Id, cash.Id, 200, today.AddDays(-1).ToDateTime(new(12, 0)));
    var saved = store.Load();
    Assert(saved[0].Debt == 200 && saved[1].CurrentBalance == 220 && AccountTotals.From(saved).Net == 20);
    var purchase = saved[0].Transactions.Single().AsCreditPurchase();
    Assert(purchase.Rule == GracePeriodRule.MonthEndPlusDays && CreditGrace.Schedule(purchase).Count == 1);
    AccountTransfers.Add(store, cash.Id, card.Id, 80, DateTime.Today);
    saved = store.Load();
    Assert(saved[0].Debt == 120 && saved[1].CurrentBalance == 140 && saved[0].PurchaseSummaries.Single().Payments.Contains(Money.Format(120)));
    var bytes = File.ReadAllText(store.FilePath);
    Reject<ArgumentException>(() => AccountTransfers.Add(store, cash.Id, card.Id, 130, DateTime.Today));
    Assert(File.ReadAllText(store.FilePath) == bytes);
});
Check("Backdated transfers into and out of savings recalculate interest for both balances", () =>
{
    var store = new AccountStore(Path.Combine(root, "savings-transfers"));
    var start = today.AddDays(-2);
    var cash = Cash("Кошелёк", 10000) with { OpeningDate = start };
    var savings = Cash("Накопительный", 10000) with { Type = AccountType.Savings, OpeningDate = start, AnnualInterestRate = (DateTime.IsLeapYear(start.Year) ? 366m : 365m) / 10m };
    store.Save([cash, savings]);
    AccountTransfers.Add(store, cash.Id, savings.Id, 5000, start.ToDateTime(new(12, 0)));
    Assert(store.Load()[1].CreditedInterest == 30.02m);
    AccountTransfers.Add(store, savings.Id, cash.Id, 1000, start.AddDays(1).ToDateTime(new(12, 0)));
    var saved = store.Load();
    Assert(saved[0].CurrentBalance == 6000 && saved[1].CurrentBalance == 14029.02m && saved[1].CreditedInterest == 29.02m);
    Assert(saved.All(a => a.Transactions.Count == 2));
});
Check("Merchant PNG copies persist, appear in purchase summaries and reject invalid image data", () =>
{
    var png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/L1sAAAAASUVORK5CYII=";
    var merchant = new Merchant { Name = "Магазин", PngIconBase64 = png };
    merchant.Validate();
    var category = new Category { Name = "Еда", Merchants = [merchant] };
    var store = new CategoryStore(Path.Combine(root, "png"));
    store.Save([category]);
    Assert(store.Load().Single().Merchants.Single().PngIconBase64 == png);
    var transaction = new BudgetTransaction { Amount = 10, OccurredAt = DateTime.Today, CategoryId = category.Id, CategoryName = category.Name, MerchantId = merchant.Id, MerchantName = merchant.Name };
    var summary = BudgetStatistics.Calculate([Cash() with { Transactions = [transaction] }], [category], CategoryKind.Expense, new(today, today));
    Assert(summary.Entries.Single().PngIconBase64 == png && summary.Categories.Single().Merchants.Single().PngIconBase64 == png);
    var bytes = File.ReadAllText(store.FilePath);
    Reject<ArgumentException>(() => store.Save([category with { Merchants = [merchant with { PngIconBase64 = "not-png" }] }]));
    Reject<ArgumentException>(() => PngIcon.Validate(new byte[PngIcon.MaximumBytes + 1]));
    Assert(File.ReadAllText(store.FilePath) == bytes);
});
Check("First profile copies all legacy data into Documents/SandinFinance/Sandin without overwriting or deleting originals", () =>
{
    var legacy = Path.Combine(root, "profile-legacy");
    var docs = Path.Combine(root, "documents");
    var accountStore = new AccountStore(legacy);
    var categoryStore = new CategoryStore(legacy);
    var account = Cash("Мой счёт", 1234.50m);
    accountStore.Save([account]);
    var category = new Category { Name = "Продукты", Merchants = [new() { Name = "Магазин" }] };
    categoryStore.Save([category]);
    var directory = ProfileStore.DefaultDirectory(docs);
    var profile = ProfileStore.Initialize(directory, legacy);
    Assert(directory == Path.Combine(Path.GetFullPath(docs), "SandinFinance", "Sandin"));
    Assert(profile.Load().Name == "Sandin" && !profile.Load().HasPin);
    Assert(File.ReadAllText(accountStore.FilePath) == File.ReadAllText(new AccountStore(directory).FilePath));
    Assert(File.ReadAllText(categoryStore.FilePath) == File.ReadAllText(new CategoryStore(directory).FilePath));
    accountStore.Save([account with { OpeningBalance = 9 }]);
    ProfileStore.Initialize(directory, legacy);
    Assert(new AccountStore(directory).Load().Single().OpeningBalance == 1234.50m && File.Exists(accountStore.FilePath));
});
Check("PIN setup, verification and change use salted hashes and preserve financial data", () =>
{
    var profile = ProfileStore.Initialize(Path.Combine(root, "profile-pin"));
    var accounts = new AccountStore(profile.DirectoryPath);
    accounts.Save([Cash()]);
    var financeBytes = File.ReadAllText(accounts.FilePath);
    Reject<ArgumentException>(() => profile.SetInitialPin("123"));
    Reject<ArgumentException>(() => profile.SetInitialPin("abcd"));
    profile.SetInitialPin("2468");
    var initial = profile.Load();
    Assert(initial.HasPin && initial.PinHash != "2468" && initial.PinSalt is not null);
    Assert(!profile.VerifyPin("1111") && profile.VerifyPin("2468") && profile.Load().FailedAttempts == 0);
    Reject<ArgumentException>(() => profile.ChangePin("1111", "1357"));
    Assert(profile.Load().PinHash == initial.PinHash);
    profile.ChangePin("2468", "1357");
    Assert(!profile.VerifyPin("2468") && profile.VerifyPin("1357"));
    Reject<InvalidOperationException>(() => profile.SetInitialPin("9999"));
    Assert(File.ReadAllText(accounts.FilePath) == financeBytes);
    var other = ProfileStore.Initialize(Path.Combine(root, "profile-other"));
    other.SetInitialPin("1357");
    Assert(other.Load().PinSalt != profile.Load().PinSalt && other.Load().PinHash != profile.Load().PinHash);
});
Check("Repeated wrong PIN attempts persist cooldown across restarts and expire without resetting the profile", () =>
{
    var profile = ProfileStore.Initialize(Path.Combine(root, "profile-lock"));
    profile.SetInitialPin("2468");
    var now = DateTimeOffset.UtcNow;
    for (var i = 0; i < 5; i++) Assert(!profile.VerifyPin("1111", now));
    var reopened = new ProfileStore(profile.DirectoryPath);
    Reject<InvalidOperationException>(() => reopened.VerifyPin("2468", now));
    Assert(reopened.VerifyPin("2468", now.AddSeconds(31)) && reopened.Load().FailedAttempts == 0 && reopened.Load().LockedUntil is null);
    File.WriteAllText(profile.FilePath, "broken json");
    Reject<System.Text.Json.JsonException>(() => ProfileStore.Initialize(profile.DirectoryPath));
    Assert(File.ReadAllText(profile.FilePath) == "broken json");
});
Check("Category order persists independently for expense and income tabs", () =>
{
    var a = new Category { Name = "A" };
    var b = new Category { Name = "B" };
    var c = new Category { Name = "C" };
    var income = new Category { Name = "Income", Kind = CategoryKind.Income };
    var list = new List<Category> { a, income, b, c };
    var moved = CategoryArrangement.Move(list, c.Id, a.Id, false);
    Assert(moved.Select(x => x.Id).SequenceEqual(new[] { c.Id, income.Id, a.Id, b.Id }));
    moved = CategoryArrangement.Move(moved, c.Id, b.Id, true);
    Assert(moved.Select(x => x.Id).SequenceEqual(new[] { a.Id, income.Id, b.Id, c.Id }));
    Reject<ArgumentException>(() => CategoryArrangement.Move(list, a.Id, income.Id, false));
    var categoryStore = new CategoryStore(Path.Combine(root, "category-order"));
    categoryStore.Save(moved);
    Assert(categoryStore.Load().Select(x => x.Id).SequenceEqual(moved.Select(x => x.Id)));
});
Check("Account deletion preserves linked transfers and surviving balances across future saves", () =>
{
    var store = new AccountStore(Path.Combine(root, "deletion"));
    var sender = Cash("Sender", 1000);
    var recipient = Cash("Recipient", 200);
    store.Save([sender, recipient]);
    AccountTransfers.Add(store, sender.Id, recipient.Id, 100, DateTime.Now);
    var before = store.Load().Single(a => a.Id == recipient.Id);
    Assert(store.Delete(sender.Id).Id == sender.Id);
    var survivors = store.Load();
    Assert(survivors.Count == 1 && survivors[0].CurrentBalance == before.CurrentBalance && survivors[0].Transactions.Single().IsTransfer);
    var replacement = Cash("Sender", 500);
    store.Save([.. survivors, replacement]);
    AccountTransfers.Add(store, recipient.Id, replacement.Id, 50, DateTime.Now);
    Assert(store.Load().Single(a => a.Id == recipient.Id).CurrentBalance == 250);
    Assert(File.ReadAllText(store.FilePath).Contains("DeletedAccounts"));
    var bytes = File.ReadAllText(store.FilePath);
    Reject<ArgumentException>(() => store.Delete(sender.Id));
    Assert(File.ReadAllText(store.FilePath) == bytes);
    store.Delete(recipient.Id);
    store.Delete(replacement.Id);
    Assert(store.Load().Count == 0);
    store.Save([Cash("Recipient", 0)]);
    Assert(store.Load().Single().CurrentBalance == 0);
});
Check("Deleting expenses restores balances and recalculates savings interest and credit schedules", () =>
{
    var directory = Path.Combine(root, "expense-deletion");
    var store = new AccountStore(directory);
    var categories = new CategoryStore(directory);
    var category = new Category { Name = "Расход" };
    categories.Save([category]);
    var cash = Cash("Cash deletion", 1000);
    var start = today.AddDays(-2);
    var savings = SavingsInterest.EnsureHistory(Cash("Savings deletion", 1000) with { Type = AccountType.Savings, OpeningDate = start, InterestAsOf = start, AnnualInterestRate = 36.5m });
    var card = Cash("Credit deletion", 0) with { Type = AccountType.CreditCard, CreditLimit = 1000, GraceRule = GracePeriodRule.EndOfNextMonth, GraceDays = 55 };
    store.Save([cash, savings, card]);
    var expense = BudgetOperations.Add(store, categories, cash.Id, category.Id, CategoryKind.Expense, 100, DateTime.Today, "Ошибка");
    BudgetOperations.DeleteExpense(store, cash.Id, expense.Id);
    Assert(store.Load().Single(a => a.Id == cash.Id).CurrentBalance == 1000);
    var savingsExpense = BudgetOperations.Add(store, categories, savings.Id, category.Id, CategoryKind.Expense, 100, start.ToDateTime(TimeOnly.MinValue), "Ошибка");
    BudgetOperations.DeleteExpense(store, savings.Id, savingsExpense.Id);
    Assert(store.Load().Single(a => a.Id == savings.Id).CurrentBalance == SavingsInterest.Recalculate(savings, today).CurrentBalance);
    var creditExpense = BudgetOperations.Add(store, categories, card.Id, category.Id, CategoryKind.Expense, 200, DateTime.Today, "Ошибка");
    BudgetOperations.DeleteExpense(store, card.Id, creditExpense.Id);
    var restored = store.Load().Single(a => a.Id == card.Id);
    Assert(restored.Debt == 0 && !restored.PurchaseSummaries.Any());
    var legacy = new CreditPurchase { Name = "Старая покупка", Amount = 50, Date = today, Rule = GracePeriodRule.EndOfNextMonth, GraceDays = 55 };
    store.Save(store.Load().Select(a => a.Id == card.Id ? a with { Purchases = [legacy] } : a).ToList());
    BudgetOperations.DeleteExpense(store, card.Id, legacy.Id);
    Assert(store.Load().Single(a => a.Id == card.Id).Debt == 0);
    Assert(BudgetStatistics.Calculate(store.Load(), categories.Load(), CategoryKind.Expense, DateRange.For(BudgetPeriod.Month, today)).Total == 0);
});
Check("Invalid expense deletions leave repayments, transfer pairs and the saved file untouched", () =>
{
    var directory = Path.Combine(root, "expense-deletion-rejected");
    var store = new AccountStore(directory);
    var categories = new CategoryStore(directory);
    var category = new Category { Name = "Расход" };
    categories.Save([category]);
    var cash = Cash("Cash", 1000);
    var card = Cash("Credit", 0) with { Type = AccountType.CreditCard, CreditLimit = 1000, GraceRule = GracePeriodRule.EndOfNextMonth, GraceDays = 55 };
    store.Save([cash, card]);
    var expense = BudgetOperations.Add(store, categories, card.Id, category.Id, CategoryKind.Expense, 200, DateTime.Today, "Ошибка");
    AccountTransfers.Add(store, cash.Id, card.Id, 150, DateTime.Today.AddMinutes(1));
    var bytes = File.ReadAllText(store.FilePath);
    Reject<ArgumentException>(() => BudgetOperations.DeleteExpense(store, card.Id, expense.Id));
    var transfer = store.Load().Single(a => a.Id == cash.Id).Transactions.Single();
    Reject<ArgumentException>(() => BudgetOperations.DeleteExpense(store, cash.Id, transfer.Id));
    Reject<ArgumentException>(() => BudgetOperations.DeleteExpense(store, cash.Id, Guid.NewGuid()));
    Assert(File.ReadAllText(store.FilePath) == bytes);
});
Check("Editing an expense changes only amount and time and replays savings and credit balances", () =>
{
    var directory = Path.Combine(root, "expense-edit");
    var store = new AccountStore(directory);
    var categories = new CategoryStore(directory);
    var category = new Category { Name = "Продукты", Merchants = [new Merchant { Name = "Магазин" }] };
    categories.Save([category]);
    var cash = Cash("Cash", 1000);
    var start = today.AddDays(-2);
    var savings = SavingsInterest.EnsureHistory(Cash("Savings", 1000) with { Type = AccountType.Savings, OpeningDate = start, InterestAsOf = start, AnnualInterestRate = 36.5m });
    var card = Cash("Card", 0) with { Type = AccountType.CreditCard, CreditLimit = 1000, GraceRule = GracePeriodRule.EndOfNextMonth, GraceDays = 55 };
    store.Save([cash, savings, card]);
    var expense = BudgetOperations.Add(store, categories, cash.Id, category.Id, CategoryKind.Expense, 100, DateTime.Today, "Магазин");
    BudgetOperations.EditExpense(store, cash.Id, expense.Id, 150, new TimeOnly(0, 1));
    var edited = store.Load().Single(a => a.Id == cash.Id).Transactions.Single();
    Assert(edited == expense with { Amount = 150, OccurredAt = DateTime.Today.AddMinutes(1) });
    Assert(store.Load().Single(a => a.Id == cash.Id).CurrentBalance == 850);
    var savingsExpense = BudgetOperations.Add(store, categories, savings.Id, category.Id, CategoryKind.Expense, 100, start.ToDateTime(TimeOnly.MinValue), "");
    BudgetOperations.EditExpense(store, savings.Id, savingsExpense.Id, 200, new TimeOnly(12, 30));
    var expected = SavingsInterest.Recalculate(savings with { Transactions = [savingsExpense with { Amount = 200, OccurredAt = start.ToDateTime(new TimeOnly(12, 30)) }] }, today);
    Assert(store.Load().Single(a => a.Id == savings.Id).CurrentBalance == expected.CurrentBalance);
    var creditExpense = BudgetOperations.Add(store, categories, card.Id, category.Id, CategoryKind.Expense, 100, DateTime.Today, "");
    BudgetOperations.EditExpense(store, card.Id, creditExpense.Id, 150, new TimeOnly(0, 1));
    Assert(store.Load().Single(a => a.Id == card.Id).Debt == 150);
    Assert(store.Load().Single(a => a.Id == card.Id).Transactions.Single().CreditRule == creditExpense.CreditRule);
    var legacy = new CreditPurchase { Name = "Покупка", Amount = 50, Date = today, Rule = GracePeriodRule.EndOfNextMonth, GraceDays = 55 };
    store.Save(store.Load().Select(a => a.Id == card.Id ? a with { Purchases = [legacy] } : a).ToList());
    BudgetOperations.EditExpense(store, card.Id, legacy.Id, 75, new TimeOnly(0, 2));
    var editedLegacy = store.Load().Single(a => a.Id == card.Id).Purchases.Single();
    Assert(editedLegacy == legacy with { Amount = 75, Time = new TimeOnly(0, 2) });
    Assert(BudgetStatistics.Calculate(store.Load(), categories.Load(), CategoryKind.Expense, DateRange.For(BudgetPeriod.Month, today)).Entries.Single(e => e.Id == legacy.Id).OccurredAt == today.ToDateTime(new TimeOnly(0, 2)));
});
Check("Rejected expense edits do not change the saved file or break transfer repayments", () =>
{
    var directory = Path.Combine(root, "expense-edit-rejected");
    var store = new AccountStore(directory);
    var categories = new CategoryStore(directory);
    var expenseCategory = new Category { Name = "Расход" };
    var incomeCategory = new Category { Name = "Доход", Kind = CategoryKind.Income };
    categories.Save([expenseCategory, incomeCategory]);
    var cash = Cash("Cash", 1000);
    var card = Cash("Card", 0) with { Type = AccountType.CreditCard, CreditLimit = 1000, GraceRule = GracePeriodRule.EndOfNextMonth, GraceDays = 55 };
    store.Save([cash, card]);
    var expense = BudgetOperations.Add(store, categories, card.Id, expenseCategory.Id, CategoryKind.Expense, 200, DateTime.Today, "");
    var income = BudgetOperations.Add(store, categories, cash.Id, incomeCategory.Id, CategoryKind.Income, 50, DateTime.Today, "");
    AccountTransfers.Add(store, cash.Id, card.Id, 150, DateTime.Today.AddMinutes(1));
    var bytes = File.ReadAllText(store.FilePath);
    Reject<ArgumentException>(() => BudgetOperations.EditExpense(store, card.Id, expense.Id, 100, TimeOnly.MinValue));
    Reject<ArgumentException>(() => BudgetOperations.EditExpense(store, card.Id, expense.Id, 1200, TimeOnly.MinValue));
    Reject<ArgumentException>(() => BudgetOperations.EditExpense(store, card.Id, expense.Id, 200, new TimeOnly(0, 2)));
    Reject<ArgumentException>(() => BudgetOperations.EditExpense(store, card.Id, expense.Id, 0, TimeOnly.MinValue));
    Reject<ArgumentException>(() => BudgetOperations.EditExpense(store, cash.Id, income.Id, 100, TimeOnly.MinValue));
    var transfer = store.Load().Single(a => a.Id == cash.Id).Transactions.Single(t => t.IsTransfer);
    Reject<ArgumentException>(() => BudgetOperations.EditExpense(store, cash.Id, transfer.Id, 100, TimeOnly.MinValue));
    Assert(File.ReadAllText(store.FilePath) == bytes);
});
Check("Statement dates and scheduled opening debt follow explicit bank rules without adding debt", () =>
{
    CreditPurchase Purchase(DateOnly date) => new() { Name = "Покупка", Amount = 100, Date = date, Rule = GracePeriodRule.StatementMonthEnd, GraceDays = 55, StatementDay = 8 };
    Assert(CreditGrace.LastDueDate(Purchase(new(2026, 10, 7))) == new DateOnly(2026, 10, 31));
    Assert(CreditGrace.LastDueDate(Purchase(new(2026, 10, 8))) == new DateOnly(2026, 11, 30));
    Assert(CreditGrace.LastDueDate(Purchase(new(2026, 12, 31))) == new DateOnly(2027, 1, 31));
    var store = new AccountStore(Path.Combine(root, "opening-debt-schedule"));
    var card = Cash("Card", 3000m) with { Type = AccountType.CreditCard, CreditLimit = 10000, OpeningDebtPayments = [new(new(2026, 11, 1), 1000m)] };
    var cash = Cash("Cash", 20000);
    store.Save([card, cash]);
    Assert(store.Load()[0].Debt == 3000m && store.Load()[0].PurchaseSummaries.Single().Payments.Contains(Money.Format(1000m)));
    AccountTransfers.Add(store, cash.Id, card.Id, 500, DateTime.Today);
    Assert(store.Load()[0].Debt == 2500m && store.Load()[0].PurchaseSummaries.Single().Payments.Contains(Money.Format(500m)));
});
Check("Actual savings interest corrections survive replay and resume compounding without duplication", () =>
{
    var start = today.AddDays(-2);
    var store = new AccountStore(Path.Combine(root, "actual-interest"));
    var savings = SavingsInterest.EnsureHistory(Cash("Savings", 1000) with { Type = AccountType.Savings, OpeningDate = start, InterestAsOf = start, AnnualInterestRate = 36.5m });
    store.Save([SavingsInterest.Accrue(savings, today)]);
    SavingsInterest.Correct(store, savings.Id, start, 0.50m);
    var corrected = store.Load().Single();
    Assert(corrected.CurrentBalance == 1001.50m && corrected.CreditedInterest == 1.50m);
    Assert(SavingsInterest.Recalculate(corrected, today).CurrentBalance == 1001.50m);
    Assert(SavingsInterest.Accrue(corrected, today.AddDays(1)).CurrentBalance == 1002.50m);
    SavingsInterest.Correct(store, savings.Id, start, 0.25m);
    Assert(store.Load().Single().CurrentBalance == 1001.25m && store.Load().Single().InterestCorrections.Count == 1);
    var summary = BudgetStatistics.Calculate(store.Load(), [], CategoryKind.Income, new(start, today));
    Assert(summary.Entries.Count(e => e.IsInterest) == 2 && summary.Total == 1.25m);
    Assert(summary.Categories.Single().Id == BudgetStatistics.AutomaticInterestCategoryId && summary.Categories.Single().Name == "Проценты");
    var bytes = File.ReadAllText(store.FilePath);
    Reject<ArgumentException>(() => SavingsInterest.Correct(store, savings.Id, today, 1));
    Assert(File.ReadAllText(store.FilePath) == bytes);
});
Check("Income statistics include corrected interest once, reuse the interest category and exclude transfers", () =>
{
    var directory = Path.Combine(root, "interest-statistics");
    var store = new AccountStore(directory);
    var categories = new CategoryStore(directory);
    var start = today.AddDays(-2);
    var cash = Cash("Cash", 1000) with { OpeningDate = start };
    var savings = SavingsInterest.EnsureHistory(Cash("Savings", 1000) with { Type = AccountType.Savings, OpeningDate = start, InterestAsOf = start, AnnualInterestRate = 36.5m });
    var otherSavings = SavingsInterest.EnsureHistory(Cash("Other savings", 2000) with { Type = AccountType.Savings, OpeningDate = start, InterestAsOf = start, AnnualInterestRate = 36.5m });
    store.Save([cash, SavingsInterest.Accrue(savings, today), SavingsInterest.Accrue(otherSavings, today)]);
    var interest = new Category { Name = "Проценты", Kind = CategoryKind.Income, IconKey = "interest", ColorHex = "#2478A8" };
    var salary = new Category { Name = "Зарплата", Kind = CategoryKind.Income, IconKey = "work" };
    categories.Save([interest, salary]);
    BudgetOperations.Add(store, categories, cash.Id, salary.Id, CategoryKind.Income, 20, DateTime.Today, "Работа");
    BudgetOperations.Add(store, categories, cash.Id, interest.Id, CategoryKind.Income, 3, DateTime.Today, "Банк");
    AccountTransfers.Add(store, cash.Id, savings.Id, 500, DateTime.Now);
    SavingsInterest.Correct(store, savings.Id, start, 0.25m);
    var accounts = store.Load();
    var summary = BudgetStatistics.Calculate(accounts, categories.Load(), CategoryKind.Income, new(start, today));
    Assert(summary.Total == 28.25m && summary.Categories.Count == 2);
    var group = summary.Categories.Single(c => c.Id == interest.Id);
    Assert(group.Amount == 8.25m && group.ColorHex == interest.ColorHex && group.Merchants.Count == 3 && group.Count == 5);
    Assert(group.Merchants.Single(m => m.Name == savings.Name).Amount == 1.25m && group.Merchants.Single(m => m.Name == otherSavings.Name).Amount == 4);
    Assert(summary.Entries.Count(e => e.IsTransfer) == 1 && summary.Categories.Sum(c => c.Amount) == summary.Total);
    var boundary = BudgetStatistics.Calculate(accounts, categories.Load(), CategoryKind.Income, new(start.AddDays(1), start.AddDays(1)));
    Assert(boundary.Total == 2.25m && boundary.Entries.All(e => e.IsInterest));
});
Check("Transfer history and editable income preserve both legs and do not inflate spending totals", () =>
{
    var directory = Path.Combine(root, "editable-incoming");
    var store = new AccountStore(directory);
    var categories = new CategoryStore(directory);
    var category = new Category { Name = "Доход", Kind = CategoryKind.Income };
    categories.Save([category]);
    var cash = Cash("Cash", 1000);
    var savings = Cash("Savings", 500) with { Type = AccountType.Savings, InterestAsOf = today };
    store.Save([cash, savings]);
    AccountTransfers.Add(store, cash.Id, savings.Id, 100, DateTime.Today);
    var incoming = store.Load()[1].Transactions.Single();
    AccountTransfers.Edit(store, savings.Id, incoming.Id, 150, new TimeOnly(0, 1));
    Assert(store.Load()[0].CurrentBalance == 850 && store.Load()[1].CurrentBalance == 650);
    Assert(store.Load().SelectMany(a => a.Transactions).All(t => t.Amount == 150 && t.OccurredAt == DateTime.Today.AddMinutes(1)));
    var summary = BudgetStatistics.Calculate(store.Load(), [], CategoryKind.Expense, new(today, today));
    Assert(summary.Total == 0 && summary.Categories.Count == 0 && summary.Entries.Single().IsTransfer);
    var income = BudgetOperations.Add(store, categories, savings.Id, category.Id, CategoryKind.Income, 10, DateTime.Today.AddMinutes(2), "");
    BudgetOperations.EditIncome(store, savings.Id, income.Id, 25, new TimeOnly(0, 3));
    Assert(store.Load()[1].CurrentBalance == 675);
    var profile = ProfileStore.Initialize(directory);
    profile.SetBudget(60000, cash.Id);
    Assert(profile.Load().MonthlyBudget == 60000 && profile.Load().BudgetAccountId == cash.Id);
});
Check("Interest posting timestamps preserve the calculation day and remain editable without duplicates", () =>
{
    var start = today.AddDays(-2);
    var timestamp = start.ToDateTime(new TimeOnly(2, 32));
    var store = new AccountStore(Path.Combine(root, "interest-posting-time"));
    var account = SavingsInterest.EnsureHistory(Cash("Savings", 1000) with { Type = AccountType.Savings, OpeningDate = start, InterestAsOf = start, AnnualInterestRate = 36.5m });
    account = SavingsInterest.Recalculate(account with { InterestCorrections = [new(start, 0.50m, timestamp)] }, today);
    store.Save([account]);
    var summary = BudgetStatistics.Calculate(store.Load(), [], CategoryKind.Income, new(start, today));
    var posted = summary.Entries.Single(e => e.InterestDate == start);
    Assert(posted.OccurredAt == timestamp && posted.Amount == 0.50m && account.CurrentBalance == 1001.50m);
    SavingsInterest.Correct(store, account.Id, posted.InterestDate!.Value, 0.25m);
    var edited = store.Load().Single();
    Assert(edited.InterestCorrections.Single().OccurredAt == timestamp && edited.CurrentBalance == 1001.25m);
});
Check("Daily bank interest posts at 01:00 under the posting day's date exactly once", () =>
{
    var postingDay = today.AddDays(-2);
    var start = postingDay.AddDays(-2);
    var account = SavingsInterest.EnsureHistory(Cash("Posting at 01", 1000) with
    {
        Type = AccountType.Savings, OpeningDate = start, InterestAsOf = start, AnnualInterestRate = 36.5m,
        DailyInterestPostingTime = new TimeOnly(1, 0),
        InterestCorrections = [new(start, 0.50m, start.ToDateTime(new TimeOnly(2, 32))), new(start.AddDays(1), 1, start.AddDays(1).ToDateTime(new TimeOnly(1, 0))), new(postingDay, 1, postingDay.ToDateTime(new TimeOnly(1, 0)))]
    });
    account = SavingsInterest.Recalculate(account, postingDay);
    Assert(account.CurrentBalance == 1002.50m);
    var tomorrow = postingDay.AddDays(1);
    var before = SavingsInterest.Accrue(account, tomorrow, tomorrow.ToDateTime(new TimeOnly(0, 59)));
    Assert(before.CurrentBalance == account.CurrentBalance);
    var after = SavingsInterest.Accrue(account, tomorrow, tomorrow.ToDateTime(new TimeOnly(1, 0)));
    Assert(after.CurrentBalance == 1003.50m && SavingsInterest.Accrue(after, tomorrow, tomorrow.ToDateTime(new TimeOnly(1, 1))).CurrentBalance == after.CurrentBalance);
    Assert(!SavingsInterest.History(account, tomorrow, tomorrow.ToDateTime(new TimeOnly(0, 59))).Any(a => a.Date == tomorrow));
    var posted = SavingsInterest.History(account, tomorrow, tomorrow.ToDateTime(new TimeOnly(1, 0))).Single(a => a.Date == tomorrow);
    Assert(posted.OccurredAt == tomorrow.ToDateTime(new TimeOnly(1, 0)) && posted.Amount == 1);
});
Check("Sber purchase month plus three months groups October spending into January and repays earliest debt first", () =>
{
    CreditPurchase Purchase(DateOnly date) => new() { Name = "Purchase", Amount = 100, Date = date, Rule = GracePeriodRule.PurchaseMonthPlusThreeMonths, GraceDays = 120 };
    Assert(CreditGrace.LastDueDate(Purchase(new(2026, 10, 1))) == new DateOnly(2027, 1, 31));
    Assert(CreditGrace.LastDueDate(Purchase(new(2026, 10, 31))) == new DateOnly(2027, 1, 31));
    Assert(CreditGrace.LastDueDate(Purchase(new(2026, 11, 1))) == new DateOnly(2027, 2, 28));
    Assert(CreditGrace.LastDueDate(Purchase(new(2027, 11, 30))) == new DateOnly(2028, 2, 29));
    var directory = Path.Combine(root, "sber-month-cycle");
    var store = new AccountStore(directory);
    var categories = new CategoryStore(directory);
    var food = new Category { Name = "Food" }; categories.Save([food]);
    var card = Cash("Card", 3000) with { Type = AccountType.CreditCard, CreditLimit = 10000, GraceRule = GracePeriodRule.PurchaseMonthPlusThreeMonths, GraceDays = 120, OpeningDebtPayments = [new(today, 1000), new(today.AddMonths(1), 1000), new(today.AddMonths(2), 1000)] };
    var cash = Cash("Cash", 5000); store.Save([card, cash]);
    var expense = BudgetOperations.Add(store, categories, card.Id, food.Id, CategoryKind.Expense, 200, DateTime.Today, "");
    var due = CreditGrace.LastDueDate(Purchase(today));
    Assert(store.Load()[0].PaymentSummaries.Count() == 4 && store.Load()[0].PaymentSummaries.Last().Heading == $"Задолженность до — {due:dd.MM}");
    AccountTransfers.Add(store, cash.Id, card.Id, 1200, DateTime.Today.AddMinutes(1));
    var repaid = store.Load()[0];
    Assert(repaid.Debt == 2000 && repaid.PaymentSummaries.Count() == 3 && repaid.PaymentSummaries.First().Payments == Money.Format(800));
    BudgetOperations.EditExpense(store, card.Id, expense.Id, 250, new TimeOnly(0, 0));
    Assert(store.Load()[0].PaymentSummaries.Last().Payments == Money.Format(250));
    BudgetOperations.DeleteExpense(store, card.Id, expense.Id);
    Assert(store.Load()[0].PaymentSummaries.Count() == 2 && store.Load()[0].Debt == 1800);
});
Check("Profile layout migrates flat financial files and backups without changing contents or PIN", () =>
{
    var directory = Path.Combine(root, "layout-migration");
    var store = new AccountStore(directory); store.Save([Cash("Cash", 1234.50m)]);
    var categories = new CategoryStore(directory); categories.Save([new Category { Name = "Food" }]);
    var profile = ProfileStore.Initialize(directory); profile.SetInitialPin("2468");
    var original = File.ReadAllBytes(store.FilePath);
    File.Move(store.FilePath, Path.Combine(directory, "accounts.json"));
    File.Move(categories.FilePath, Path.Combine(directory, "categories.json"));
    Directory.CreateDirectory(Path.Combine(directory, "backups", "old-snapshot"));
    File.WriteAllText(Path.Combine(directory, "backups", "old-snapshot", "note.txt"), "saved");
    var migrated = ProfileStore.Initialize(directory);
    Assert(migrated.VerifyPin("2468") && File.ReadAllBytes(store.FilePath).SequenceEqual(original));
    Assert(!File.Exists(Path.Combine(directory, "accounts.json")) && File.Exists(Path.Combine(ProfileLayout.ManualBackups(directory), "old-snapshot", "note.txt")));
    Assert(File.Exists(ProfileLayout.AutomaticBackup(directory, "profile.json")));
    store.Save(store.Load());
    Assert(File.Exists(store.BackupPath) && !File.Exists(store.FilePath + ".bak"));
    File.WriteAllText(Path.Combine(directory, "accounts.json"), "different");
    Reject<System.IO.IOException>(() => ProfileLayout.Migrate(directory));
    Assert(File.ReadAllBytes(store.FilePath).SequenceEqual(original) && File.ReadAllText(Path.Combine(directory, "accounts.json")) == "different");
});
Check("Excel export stores numeric amounts and dates, excludes transfers, and escapes user text", () =>
{
    var directory = Path.Combine(root, "excel-export"); Directory.CreateDirectory(directory);
    var store = new AccountStore(directory); var catalog = new CategoryStore(directory);
    var cash = Cash("Cash", 5000); var other = Cash("Other", 1000);
    var food = new Category { Name = "Продукты" }; catalog.Save([food]); store.Save([cash, other]);
    BudgetOperations.Add(store, catalog, cash.Id, food.Id, CategoryKind.Expense, 1250.50m, DateTime.Today.AddMinutes(1), "=2+2 & магазин");
    AccountTransfers.Add(store, cash.Id, other.Id, 500, DateTime.Today.AddMinutes(2));
    var before = File.ReadAllBytes(store.FilePath);
    var path = Path.Combine(directory, "expenses.xlsx");
    Assert(ExpenseExcelExporter.Export(path, store.Load(), catalog.Load()) == 1);
    using (var zip = System.IO.Compression.ZipFile.OpenRead(path))
    {
        System.Xml.Linq.XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using var stream = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var sheet = System.Xml.Linq.XDocument.Load(stream);
        System.Xml.Linq.XElement Cell(string address) => sheet.Descendants(ns + "c").Single(c => (string?)c.Attribute("r") == address);
        Assert(Cell("F6").Element(ns + "v")!.Value == "1250.50" && Cell("F3").Element(ns + "f")!.Value == "SUM(F6:F6)");
        Assert(Cell("E6").Attribute("t")!.Value == "inlineStr" && Cell("E6").Descendants(ns + "t").Single().Value == "=2+2 & магазин");
        Assert(Cell("A6").Attribute("t") is null && Cell("B6").Attribute("t") is null && zip.GetEntry("xl/tables/table1.xml") is not null);
    }
    Assert(File.ReadAllBytes(store.FilePath).SequenceEqual(before));
    Assert(ExpenseExcelExporter.Export(path, [], []) == 0 && !Directory.GetFiles(directory, "*.tmp").Any());
    ExpenseExcelExporter.Export(path, store.Load(), catalog.Load());
    Console.WriteLine("Excel sample: " + path);
});
Check("Recurring schedules preserve month-end anchors, leap years, intervals and final dates", () =>
{
    var payment = new RecurringPayment { Name = "Подписка", Amount = 100, AccountId = Guid.NewGuid(), CategoryId = Guid.NewGuid(), FirstDueAt = new(2026, 1, 31, 14, 30, 0) };
    payment.Validate();
    Assert(payment.DueAt(1) == new DateTime(2026, 2, 28, 14, 30, 0) && payment.DueAt(2) == new DateTime(2026, 3, 31, 14, 30, 0));
    Assert((payment with { Interval = 2 }).DueAt(1) == new DateTime(2026, 3, 31, 14, 30, 0));
    var annual = payment with { Repeat = PaymentRepeat.Yearly, FirstDueAt = new(2024, 2, 29, 9, 15, 0) };
    Assert(annual.DueAt(1) == new DateTime(2025, 2, 28, 9, 15, 0) && annual.DueAt(4) == new DateTime(2028, 2, 29, 9, 15, 0));
    Assert((payment with { Repeat = PaymentRepeat.Weekly, FirstDueAt = new(2026, 12, 28, 9, 0, 0) }).DueAt(1) == new DateTime(2027, 1, 4, 9, 0, 0));
    var limited = payment with { EndDate = new(2026, 2, 28) };
    Assert(limited.DueAt(1) is not null && limited.DueAt(2) is null);
    Assert((payment with { Repeat = PaymentRepeat.None, CompletedCount = 1 }).NextDueAt is null);
    Assert((payment with { IsEnabled = false }).NextDueAt is null);
    Reject<ArgumentException>(() => (payment with { Interval = 0 }).Validate());
    Reject<ArgumentException>(() => (payment with { Amount = 0 }).Validate());
    Reject<ArgumentException>(() => (payment with { EndDate = new(2026, 1, 30) }).Validate());
});
Check("Recurring reminders appear for all three calendar days at every read and persist until manually paid", () =>
{
    var store = new RecurringPaymentStore(Path.Combine(root, "recurring-reminders"));
    var now = new DateTime(2026, 10, 7, 23, 0, 0);
    var payment = new RecurringPayment { Name = "Связь", Amount = 500, AccountId = Guid.NewGuid(), CategoryId = Guid.NewGuid(), FirstDueAt = new(2026, 10, 10, 23, 59, 0), Repeat = PaymentRepeat.None };
    var distant = payment with { Id = Guid.NewGuid(), FirstDueAt = new(2026, 10, 11, 0, 0, 0) };
    store.Save([payment, distant]);
    Assert(store.Reminders(now).Single().Id == payment.Id && new RecurringPaymentStore(Path.GetDirectoryName(Path.GetDirectoryName(store.FilePath)!)!).Reminders(now).Single().Id == payment.Id);
    Assert(store.Reminders(new(2026, 10, 8)).Any(p => p.Id == payment.Id) && store.Reminders(new(2026, 10, 9)).Any(p => p.Id == payment.Id));
    Assert(store.Reminders(new(2026, 10, 15)).Any(p => p.Id == payment.Id));
    var bytes = File.ReadAllBytes(store.FilePath);
    var paid = store.Complete(payment.Id, payment.FirstDueAt, now);
    Assert(paid.NextDueAt is null && File.ReadAllBytes(store.BackupPath).SequenceEqual(bytes));
    Assert(!store.Reminders(new(2026, 10, 15)).Any(p => p.Id == payment.Id));
    var after = File.ReadAllBytes(store.FilePath);
    Reject<ArgumentException>(() => store.Complete(payment.Id, payment.FirstDueAt, now));
    Assert(File.ReadAllBytes(store.FilePath).SequenceEqual(after));
    Assert(!distant.NeedsReminder(new(2026, 10, 7)) && (payment with { IsEnabled = false }).NeedsReminder(now) == false);
});
Check("Recurring plans allow duplicates, keep balances untouched and reject corrupt files without overwriting", () =>
{
    var directory = Path.Combine(root, "recurring-storage");
    var accounts = new AccountStore(directory);
    var account = Cash();
    accounts.Save([account]);
    var bytes = File.ReadAllBytes(accounts.FilePath);
    var store = new RecurringPaymentStore(directory);
    var payment = new RecurringPayment { Name = "Аренда", Amount = 10000, AccountId = account.Id, CategoryId = Guid.NewGuid(), FirstDueAt = DateTime.Today.AddDays(3).AddHours(9) };
    store.Save([payment, payment with { Id = Guid.NewGuid() }]);
    Assert(store.Load().Count == 2);
    store.Complete(payment.Id, payment.FirstDueAt);
    Assert(File.ReadAllBytes(accounts.FilePath).SequenceEqual(bytes) && store.Load().Single(p => p.Id == payment.Id).NextDueAt == payment.FirstDueAt.AddMonths(1));
    var before = File.ReadAllBytes(store.FilePath);
    Reject<ArgumentException>(() => store.Save([payment, payment]));
    Assert(File.ReadAllBytes(store.FilePath).SequenceEqual(before));
    File.WriteAllText(store.FilePath, "broken JSON");
    Reject<System.Text.Json.JsonException>(() => store.Load());
    Reject<System.Text.Json.JsonException>(() => store.Complete(payment.Id, payment.FirstDueAt));
    Assert(File.ReadAllText(store.FilePath) == "broken JSON" && !File.Exists(store.FilePath + ".tmp"));
});
Check("Credit reminders use unpaid schedule amounts, appear three days before and disappear after repayment", () =>
{
    var directory = Path.Combine(root, "credit-reminders");
    var store = new AccountStore(directory);
    var opening = new DateOnly(2026, 10, 1);
    var cash = Cash("Cash", 5000) with { OpeningDate = opening };
    var card = Cash("Сбер", 1000) with { Type = AccountType.CreditCard, CreditLimit = 10000, OpeningDate = opening, IsHidden = true, OpeningDebtPayments = [new(new(2026, 10, 31), 600), new(new(2026, 11, 30), 400)] };
    store.Save([cash, card]);
    Assert(PaymentReminders.Calculate(store.Load(), [], new(2026, 10, 27, 23, 59, 0)).Count == 0);
    var reminders = PaymentReminders.Calculate(store.Load(), [], new(2026, 10, 28));
    Assert(reminders.Single().Amount == 600 && reminders.Single().TargetId == card.Id && reminders.Single().DueAt == new DateTime(2026, 10, 31, 23, 59, 0));
    AccountTransfers.Add(store, cash.Id, card.Id, 250, DateTime.Now);
    reminders = PaymentReminders.Calculate(store.Load(), [], new(2026, 10, 29));
    Assert(reminders.Single().Amount == 350 && CreditGrace.OutstandingPayments(store.Load().Single(a => a.Id == card.Id)).Count == 2);
    AccountTransfers.Add(store, cash.Id, card.Id, 350, DateTime.Now);
    Assert(PaymentReminders.Calculate(store.Load(), [], new(2026, 11, 1)).Count == 0);
    Assert(PaymentReminders.Calculate(store.Load(), [], new(2026, 11, 27)).Single().Amount == 400);
    AccountTransfers.Add(store, cash.Id, card.Id, 400, DateTime.Now);
    Assert(PaymentReminders.Calculate(store.Load(), [], new(2026, 12, 1)).Count == 0);
});
Check("Credit purchase reminders retain bank grace deadlines and combine with recurring plans without inventing dates", () =>
{
    var purchase = new CreditPurchase { Name = "Покупка", Amount = 100, Date = new(2026, 10, 1), Rule = GracePeriodRule.PurchaseMonthPlusThreeMonths, GraceDays = 120 };
    var card = Cash("Карта", 0) with { Type = AccountType.CreditCard, CreditLimit = 10000, OpeningDate = new(2026, 10, 1), Purchases = [purchase] };
    var plan = new RecurringPayment { Name = "Связь", Amount = 200, AccountId = Guid.NewGuid(), CategoryId = Guid.NewGuid(), FirstDueAt = new(2027, 1, 30, 9, 0, 0), Repeat = PaymentRepeat.None };
    Assert(PaymentReminders.Calculate([card], [], new(2027, 1, 27)).Count == 0);
    var reminders = PaymentReminders.Calculate([card], [plan], new(2027, 1, 28));
    Assert(reminders.Count == 2 && reminders[0].Kind == PaymentReminderKind.Recurring && reminders[1].Kind == PaymentReminderKind.Credit);
    Assert(reminders[1].DueAt == new DateTime(2027, 1, 31, 23, 59, 0) && reminders[1].SummaryText.Contains("31.01.2027"));
    Assert(PaymentReminders.Calculate([card with { Purchases = [], OpeningBalance = 100 }], [], new(2027, 5, 1)).Count == 0);
    Assert(PaymentReminders.Calculate([Cash()], [], new(2027, 5, 1)).Count == 0);
});
Check("Scheduled savings rate resets before payout; delayed payouts are counted once", () =>
{
    var start = today.AddDays(-6);
    var savings = SavingsInterest.EnsureHistory(Cash("Promo", 1000) with
    {
        Type = AccountType.Savings, OpeningDate = start, InterestAsOf = start,
        AnnualInterestRate = 20, DailyInterestPostingTime = new TimeOnly(1, 0)
    }) with
    {
        InterestRates = [new(start, 20, InterestPayout.Daily), new(today, 10, InterestPayout.Daily)],
        InterestCorrections = [new(start.AddDays(1), 0), new(start.AddDays(2), 1.10m)]
    };
    var settled = SavingsInterest.Recalculate(savings, today.AddDays(-1));
    var beforePayout = SavingsInterest.Accrue(settled, today, today.ToDateTime(new TimeOnly(0, 59)));
    Assert(beforePayout.AnnualInterestRate == 10 && beforePayout.CreditedInterest == settled.CreditedInterest);
    var afterPayout = SavingsInterest.Accrue(beforePayout, today, today.ToDateTime(new TimeOnly(1, 0)));
    Assert(afterPayout.CreditedInterest - beforePayout.CreditedInterest == decimal.Round(beforePayout.CurrentBalance * 0.20m / (DateTime.IsLeapYear(today.Year) ? 366 : 365), 2, MidpointRounding.AwayFromZero));
    var tomorrow = SavingsInterest.Accrue(afterPayout, today.AddDays(1));
    Assert(tomorrow.CreditedInterest - afterPayout.CreditedInterest == decimal.Round(afterPayout.CurrentBalance * 0.10m / (DateTime.IsLeapYear(today.Year) ? 366 : 365), 2, MidpointRounding.AwayFromZero));
    var stats = BudgetStatistics.Calculate([afterPayout], [], CategoryKind.Income, new(start, today));
    Assert(!stats.Entries.Any(e => e.InterestDate == start.AddDays(1)));
    Assert(stats.Entries.Single(e => e.InterestDate == start.AddDays(2)).Amount == 1.10m);
    Assert(stats.Total == afterPayout.CreditedInterest);
    var ownTransfer = new BudgetTransaction { Kind = CategoryKind.Income, Amount = 50000, OccurredAt = start.ToDateTime(new TimeOnly(12, 0)), CategoryId = Guid.NewGuid(), CategoryName = "Пополнение", IsExternalTransfer = true };
    ownTransfer.Validate();
    var transferred = BudgetStatistics.Calculate([Cash() with { Transactions = [ownTransfer] }], [], CategoryKind.Income, new(start, today));
    Assert(transferred.Total == 0 && transferred.Entries.Single().IsTransfer && !transferred.Entries.Single().CanEdit);
});
Check("New profiles use chosen names, persist PIN and remain isolated", () =>
{
    var docs = Path.Combine(root, "new-user-documents");
    Assert(ProfileStore.FindExistingDirectory(docs) is null);
    foreach (var invalid in new[] { "", "../other", "A/B", "A\\B", "CON", "nul.txt", "COM1", "user." })
        Reject<ArgumentException>(() => ProfileStore.DirectoryForName(docs, invalid));
    var directory = ProfileStore.DirectoryForName(docs, "  Анна  ");
    Assert(Path.GetFileName(directory) == "Анна");
    var profile = ProfileStore.Initialize(directory, name: "Анна");
    profile.SetInitialPin("2468");
    Assert(profile.Load().Name == "Анна" && profile.VerifyPin("2468"));
    Assert(new AccountStore(directory).Load().Count == 0);
    Assert(ProfileStore.FindExistingDirectory(docs) == directory);
    var bytes = File.ReadAllBytes(profile.FilePath);
    ProfileStore.Initialize(directory, name: "Другой пользователь");
    Assert(File.ReadAllBytes(profile.FilePath).SequenceEqual(bytes));
    Assert(!Directory.Exists(ProfileStore.DefaultDirectory(docs)));
});
Check("Profile location is per Windows user; relocation preserves files and rejects conflicts", () =>
{
    var sandbox = Path.Combine(root, "profile-location");
    var firstDocs = Path.Combine(sandbox, "first-user", "redirected-documents");
    var secondDocs = Path.Combine(sandbox, "second-user", "documents");
    var source = ProfileStore.DirectoryForName(firstDocs, "Анна");
    Assert(source != ProfileStore.DirectoryForName(secondDocs, "Анна"));
    var profile = ProfileStore.Initialize(source, name: "Анна");
    profile.SetInitialPin("2468");
    new AccountStore(source).Save([Cash("Тестовый счёт", 1234.56m)]);
    var backup = Path.Combine(ProfileLayout.ManualBackups(source), "snapshot");
    Directory.CreateDirectory(backup);
    File.WriteAllBytes(Path.Combine(backup, "test.bin"), [0, 1, 2, 255]);
    var originals = Directory.GetFiles(source, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(source, f), File.ReadAllBytes);
    var firstLocations = new ProfileLocationStore(Path.Combine(sandbox, "first-user", "local-app-data"));
    var secondLocations = new ProfileLocationStore(Path.Combine(sandbox, "second-user", "local-app-data"));
    Assert(firstLocations.LoadDirectory() is null && secondLocations.LoadDirectory() is null);
    firstLocations.SaveDirectory(source);
    var destination = Path.Combine(sandbox, "chosen-budget-folder");
    ProfileRelocation.Copy(source, destination);
    foreach (var file in originals)
    {
        Assert(File.ReadAllBytes(Path.Combine(source, file.Key)).SequenceEqual(file.Value));
        Assert(File.ReadAllBytes(Path.Combine(destination, file.Key)).SequenceEqual(file.Value));
    }
    firstLocations.SaveDirectory(destination);
    Assert(new ProfileLocationStore(Path.Combine(sandbox, "first-user", "local-app-data")).LoadDirectory() == destination);
    Assert(secondLocations.LoadDirectory() is null && new ProfileStore(destination).VerifyPin("2468"));
    Assert(new AccountStore(destination).Load().Single().CurrentBalance == 1234.56m);
    Reject<IOException>(() => ProfileRelocation.Copy(source, destination));
    Reject<ArgumentException>(() => ProfileRelocation.Copy(source, Path.Combine(source, "nested")));
    var profileFile = Path.Combine(destination, "profile.json");
    File.Move(profileFile, profileFile + ".offline");
    Reject<IOException>(() => firstLocations.LoadDirectory());
    Assert(!File.Exists(profileFile));
    File.Move(profileFile + ".offline", profileFile);
});
Console.WriteLine($"Passed: {passed}/{passed} checks.");

void Check(string name, Action action)
{
    action();
    passed++;
    Console.WriteLine($"PASS {name}");
}
void Assert(bool condition)
{
    if (!condition) throw new Exception("Assertion failed.");
}
void Reject<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}.");
}
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    Environment.ExitCode = 1;
}
