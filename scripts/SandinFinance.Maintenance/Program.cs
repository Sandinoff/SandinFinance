using SandinFinance.Core;

try
{
if (args.Length == 3 && args[0] == "--apply-account-reconciliation")
{
    using var lease = new ProfileWriteLease(args[1]);
    var options = new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    var planBytes = File.ReadAllBytes(args[2]);
    var plan = System.Text.Json.JsonSerializer.Deserialize<AccountReconciliation>(planBytes, options) ?? throw new ArgumentException("Missing reconciliation plan.");
    var store = new AccountStore(args[1]);
    var original = File.ReadAllBytes(store.FilePath);
    if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original)) != plan.ExpectedSha256) throw new IOException("The profile changed after preparing the reconciliation; no changes saved.");
    if (plan.AsOf != DateOnly.FromDateTime(DateTime.Today)) throw new ArgumentException("The reconciliation must be prepared for today.");
    var accounts = store.Load();
    if (plan.Replacements.Select(a => a.Id).Distinct().Count() != plan.Replacements.Count || plan.Replacements.Any(a => !accounts.Any(old => old.Id == a.Id))) throw new ArgumentException("Invalid replacement accounts.");
    var replacements = plan.Replacements.Select(a => a.IsSavings ? SavingsInterest.Recalculate(a, plan.AsOf) : a).ToDictionary(a => a.Id);
    foreach (var replacement in replacements.Values) { replacement.Validate(); BudgetOperations.ValidateTimeline(replacement); }
    var updated = accounts.Select(a => replacements.GetValueOrDefault(a.Id) ?? a).ToList();
    if (updated.Single(a => a.Id == plan.AccountId).CurrentBalance != plan.TargetBalance) throw new InvalidOperationException("The corrected balance does not match the bank.");
    // Validate the complete transfer graph on a disposable copy before touching the profile.
    var previewDirectory = Path.Combine(Path.GetTempPath(), "SandinFinance-reconciliation-" + Guid.NewGuid().ToString("N"));
    try
    {
        var preview = new AccountStore(previewDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(preview.FilePath)!);
        File.WriteAllBytes(preview.FilePath, original);
        preview.Save(updated);
        preview.Load();
    }
    finally
    {
        var resolved = Path.GetFullPath(previewDirectory);
        var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        if (Path.GetDirectoryName(resolved) != tempRoot) throw new IOException("Unexpected temporary directory.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
    }
    var backup = Path.Combine(ProfileLayout.ManualBackups(args[1]), "before-savings-reconciliation-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
    Directory.CreateDirectory(backup);
    File.WriteAllBytes(Path.Combine(backup, "accounts.json"), original);
    File.WriteAllBytes(Path.Combine(backup, "reconciliation.json"), planBytes);
    if (!File.ReadAllBytes(store.FilePath).AsSpan().SequenceEqual(original)) throw new IOException("Profile changed during backup; no changes saved.");
    store.Save(updated);
    var saved = store.Load().Single(a => a.Id == plan.AccountId);
    if (saved.CurrentBalance != plan.TargetBalance) throw new InvalidOperationException("Saved balance verification failed; backup retained.");
    Console.WriteLine($"Reconciled balance: {Money.Format(saved.CurrentBalance)}");
    Console.WriteLine("Backup: " + backup);
    return;
}
if (args.Length == 5 && args[0] == "--reconcile-credit-statement")
{
    using var lease = new ProfileWriteLease(args[1]);
    var store = new AccountStore(args[1]);
    var cardId = Guid.Parse(args[2]);
    var statement = DateOnly.ParseExact(args[3], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    var amount = decimal.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture);
    Money.Validate(amount, "Statement amount");
    var accounts = store.Load();
    var card = accounts.Single(a => a.Id == cardId && a.IsCredit);
    if (card.GraceRule != GracePeriodRule.StatementMonthEnd || card.StatementDay != statement.Day) throw new ArgumentException("Statement date does not match the card cycle.");
    var due = new DateOnly(statement.Year, statement.Month, DateTime.DaysInMonth(statement.Year, statement.Month));
    var scheduled = CreditGrace.OutstandingPayments(card).Where(p => p.DueDate == due).Sum(p => p.Amount);
    var missing = amount - scheduled;
    var unscheduledOpening = card.OpeningBalance - card.OpeningDebtPayments.Sum(p => p.Amount);
    if (missing == 0) { Console.WriteLine("Statement already reconciled; no changes saved."); return; }
    if (missing < 0 || missing > unscheduledOpening || card.Transactions.Any(t => t.IsTransfer && t.Kind == CategoryKind.Income))
        throw new ArgumentException("Statement cannot be reconciled solely by assigning unallocated opening debt; no changes saved.");
    var corrected = card with { OpeningDebtPayments = [.. card.OpeningDebtPayments, new(due, missing)] };
    corrected.Validate();
    BudgetOperations.ValidateTimeline(corrected);
    if (corrected.Debt != card.Debt || corrected.AvailableBalance != card.AvailableBalance || CreditGrace.OutstandingPayments(corrected).Where(p => p.DueDate == due).Sum(p => p.Amount) != amount)
        throw new InvalidOperationException("Reconciliation mismatch; no changes saved.");
    var bytes = File.ReadAllBytes(store.FilePath);
    var backup = Path.Combine(ProfileLayout.ManualBackups(args[1]), "before-credit-statement-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
    Directory.CreateDirectory(backup);
    File.WriteAllBytes(Path.Combine(backup, "accounts.json"), bytes);
    if (!File.ReadAllBytes(store.FilePath).AsSpan().SequenceEqual(bytes)) throw new IOException("Account data changed while preparing the statement; no changes saved.");
    store.Save(accounts.Select(a => a.Id == cardId ? corrected : a).ToList());
    var saved = store.Load().Single(a => a.Id == cardId);
    if (saved.Debt != card.Debt || saved.AvailableBalance != card.AvailableBalance || CreditGrace.OutstandingPayments(saved).Where(p => p.DueDate == due).Sum(p => p.Amount) != amount)
        throw new InvalidOperationException("Saved statement verification failed; original data preserved in backup.");
    Console.WriteLine($"Statement {statement:dd.MM.yyyy}: {Money.Format(amount)} due {due:dd.MM.yyyy}");
    foreach (var payment in CreditGrace.OutstandingPayments(saved)) Console.WriteLine($"{payment.DueDate:dd.MM.yyyy}: {Money.Format(payment.Amount)}");
    Console.WriteLine($"Debt unchanged: {Money.Format(saved.Debt)}; available unchanged: {Money.Format(saved.AvailableBalance)}");
    Console.WriteLine("Backup: " + backup);
    return;
}
if (args.Length == 2 && args[0] == "--organize-profile")
{
    var root = Path.GetFullPath(args[1]);
    var names = new[] { "profile.json", "accounts.json", "categories.json", "recurring-payments.json" };
    var originals = names.Where(name => File.Exists(name == "profile.json" ? Path.Combine(root, name) : ProfileLayout.ReadFile(root, name))).ToDictionary(name => name, name => File.ReadAllBytes(name == "profile.json" ? Path.Combine(root, name) : ProfileLayout.ReadFile(root, name)));
    var backup = Path.Combine(ProfileLayout.ManualBackups(root), "before-layout-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
    Directory.CreateDirectory(backup);
    foreach (var original in originals) File.WriteAllBytes(Path.Combine(backup, original.Key), original.Value);
    ProfileStore.Initialize(root);
    foreach (var original in originals)
    {
        var target = original.Key == "profile.json" ? Path.Combine(root, original.Key) : ProfileLayout.DataFile(root, original.Key);
        if (!File.ReadAllBytes(target).AsSpan().SequenceEqual(original.Value)) throw new InvalidOperationException("Migration changed financial or profile contents.");
    }
    Console.WriteLine("Profile organized. Original financial data and PIN metadata preserved byte-for-byte.");
    Console.WriteLine("Data: " + Path.Combine(root, "data"));
    Console.WriteLine("Backups: " + Path.Combine(root, "backups"));
    return;
}
if (args.Length == 5 && args[0] == "--configure-credit-month-cycle")
{
    var store = new AccountStore(args[1]);
    var cardId = Guid.Parse(args[2]);
    var since = DateTime.ParseExact(args[3], "yyyy-MM-ddTHH:mm", System.Globalization.CultureInfo.InvariantCulture);
    var payments = System.Text.Json.JsonSerializer.Deserialize<List<OpeningDebtPayment>>(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(args[4]))) ?? throw new ArgumentException("Payments missing.");
    var accounts = store.Load();
    var card = accounts.Single(a => a.Id == cardId && a.IsCredit);
    if (payments.Sum(p => p.Amount) != card.OpeningBalance) throw new ArgumentException("Statement amounts must match opening debt; no changes saved.");
    var corrected = card with
    {
        GraceRule = GracePeriodRule.PurchaseMonthPlusThreeMonths, GraceDays = 120, StatementDay = 0, OpeningDebtPayments = payments,
        Transactions = card.Transactions.Select(t => t.Kind == CategoryKind.Expense && t.OccurredAt >= since ? t with { CreditRule = GracePeriodRule.PurchaseMonthPlusThreeMonths, CreditGraceDays = 120, CreditStatementDay = 0 } : t).ToList(),
        Purchases = card.Purchases.Select(p => p.Date >= DateOnly.FromDateTime(since) ? p with { Rule = GracePeriodRule.PurchaseMonthPlusThreeMonths, GraceDays = 120, StatementDay = 0 } : p).ToList()
    };
    corrected.Validate(); BudgetOperations.ValidateTimeline(corrected);
    if (corrected.Debt != card.Debt || corrected.AvailableBalance != card.AvailableBalance) throw new InvalidOperationException("Financial amounts changed.");
    var backup = Path.Combine(ProfileLayout.ManualBackups(args[1]), "before-credit-month-cycle-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
    Directory.CreateDirectory(backup); File.Copy(store.FilePath, Path.Combine(backup, "accounts.json"));
    store.Save(accounts.Select(a => a.Id == cardId ? corrected : a).ToList());
    foreach (var payment in corrected.PaymentSummaries) Console.WriteLine($"{payment.Heading}: {payment.Payments}");
    Console.WriteLine($"Total debt: {Money.Format(corrected.Debt)}; bank available: {corrected.BalanceText}");
    return;
}
if (args.Length == 5 && args[0] == "--shift-opening-deadline")
{
    var store = new AccountStore(args[1]);
    var cardId = Guid.Parse(args[2]);
    var from = DateOnly.ParseExact(args[3], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    var to = DateOnly.ParseExact(args[4], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    var accounts = store.Load();
    var card = accounts.Single(a => a.Id == cardId && a.IsCredit);
    var corrected = card with { OpeningDebtPayments = card.OpeningDebtPayments.Select(p => p.DueDate == from ? p with { DueDate = to } : p).ToList() };
    corrected.Validate();
    var backup = Path.Combine(ProfileLayout.ManualBackups(args[1]), "before-deadline-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
    Directory.CreateDirectory(backup);
    File.Copy(store.FilePath, Path.Combine(backup, "accounts.json"));
    store.Save(accounts.Select(a => a.Id == cardId ? corrected : a).ToList());
    foreach (var payment in corrected.PaymentSummaries) Console.WriteLine($"{payment.Heading}: {payment.Payments}");
    return;
}
if (args.Length == 4 && args[0] == "--reconcile-credit-opening")
{
    var store = new AccountStore(args[1]);
    var accountId = Guid.Parse(args[2]);
    var target = decimal.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture);
    Money.Validate(target, "Available bank balance");
    var accounts = store.Load();
    var account = accounts.Single(a => a.Id == accountId && a.IsCredit);
    var corrected = account with { OpeningBalance = account.CreditLimit - target - account.Purchases.Sum(p => p.Amount) + account.TransactionDelta };
    corrected.Validate();
    BudgetOperations.ValidateTimeline(corrected);
    if (corrected.AvailableBalance != target) throw new InvalidOperationException("Balance mismatch; no changes saved.");
    var backup = Path.Combine(ProfileLayout.ManualBackups(args[1]), "before-credit-reconciliation-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
    Directory.CreateDirectory(backup);
    File.Copy(store.FilePath, Path.Combine(backup, "accounts.json"));
    store.Save(accounts.Select(a => a.Id == accountId ? corrected : a).ToList());
    if (store.Load().Single(a => a.Id == accountId).AvailableBalance != target) throw new InvalidOperationException("Saved balance mismatch.");
    Console.WriteLine($"Opening debt at {corrected.OpeningDate:dd.MM.yyyy}: {Money.Format(corrected.OpeningBalance)}; available bank balance: {Money.Format(corrected.AvailableBalance)}");
    return;
}
if (args.Length == 4 && args[0] == "--set-card-spending-limit")
{
    var store = new AccountStore(args[1]);
    var profile = new ProfileStore(args[1]);
    var cardId = Guid.Parse(args[2]);
    var limit = decimal.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture);
    var accounts = store.Load();
    var card = accounts.Single(a => a.Id == cardId && a.IsCredit) with { MonthlySpendingLimit = limit };
    card.Validate();
    var backup = Path.Combine(ProfileLayout.ManualBackups(args[1]), "before-card-limit-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
    Directory.CreateDirectory(backup);
    File.Copy(store.FilePath, Path.Combine(backup, "accounts.json"));
    File.Copy(profile.FilePath, Path.Combine(backup, "profile.json"));
    store.Save(accounts.Select(a => a.Id == cardId ? card : a).ToList());
    profile.SetBudget(0, null);
    profile.SetBudgetDisplay(null);
    var setupDate = DateOnly.FromDateTime(DateTime.Today);
    var spent = BudgetStatistics.Calculate([card], [], CategoryKind.Expense, new(new(setupDate.Year, setupDate.Month, 1), setupDate)).Total;
    Console.WriteLine($"Card monthly limit: {Money.Format(limit)}; spent: {Money.Format(spent)}; remaining: {Money.Format(limit - spent)}; bank availability: {card.BalanceText}");
    foreach (var payment in card.PaymentSummaries) Console.WriteLine($"{payment.Heading}: {payment.Payments}");
    return;
}
if (args.Length == 3 && args[0] == "--set-budget-display")
{
    var cardId = Guid.Parse(args[2]);
    var store = new AccountStore(args[1]);
    if (!store.Load().Any(a => a.Id == cardId && a.IsCredit)) throw new ArgumentException("Credit card not found.");
    var profile = new ProfileStore(args[1]);
    var backup = Path.Combine(ProfileLayout.ManualBackups(args[1]), "before-budget-display-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
    Directory.CreateDirectory(backup);
    File.Copy(profile.FilePath, Path.Combine(backup, "profile.json"));
    profile.SetBudgetDisplay(cardId);
    Console.WriteLine("Budget display configured; bank balance unchanged.");
    return;
}
if (args.Length == 7 && args[0] == "--set-posted-interest-reconcile")
{
    var store = new AccountStore(args[1]);
    var accountId = Guid.Parse(args[2]);
    var postingDay = DateOnly.ParseExact(args[3], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    var amount = decimal.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture);
    var postingTime = TimeOnly.ParseExact(args[5], "HH:mm", System.Globalization.CultureInfo.InvariantCulture);
    var target = decimal.Parse(args[6], System.Globalization.CultureInfo.InvariantCulture);
    Money.Validate(amount, "Interest"); Money.Validate(target, "Balance");
    if (postingDay.ToDateTime(postingTime) > DateTime.Now) throw new ArgumentException("Future posting.");
    var accounts = store.Load();
    var account = accounts.Single(a => a.Id == accountId && a.IsSavings);
    var correction = account with
    {
        DailyInterestPostingTime = postingTime,
        InterestCorrections = [.. account.InterestCorrections.Where(c => c.Date != postingDay).Select(c => c with { OccurredAt = c.OccurredAt ?? c.Date.ToDateTime(postingTime) }), new(postingDay, amount, postingDay.ToDateTime(postingTime))]
    };
    var reconciliationDate = DateOnly.FromDateTime(DateTime.Today);
    correction = SavingsInterest.Recalculate(correction, reconciliationDate);
    for (var attempt = 0; correction.CurrentBalance != target && attempt < 32; attempt++)
        correction = SavingsInterest.Recalculate(correction with { OpeningBalance = correction.OpeningBalance + target - correction.CurrentBalance }, reconciliationDate);
    if (correction.CurrentBalance != target) throw new ArgumentException("Could not reconcile exactly.");
    var backup = Path.Combine(ProfileLayout.ManualBackups(args[1]), "before-posting-time-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
    Directory.CreateDirectory(backup);
    File.Copy(store.FilePath, Path.Combine(backup, "accounts.json"));
    store.Save(accounts.Select(a => a.Id == accountId ? correction : a).ToList());
    Console.WriteLine($"Posting {postingDay:dd.MM.yyyy} {postingTime:HH:mm}: {Money.Format(amount)}; opening: {Money.Format(correction.OpeningBalance)}; balance: {Money.Format(correction.CurrentBalance)}; interest total: {Money.Format(correction.CreditedInterest)}");
    return;
}
if (args.Length == 5 && args[0] == "--reconcile-opening")
{
    var store = new AccountStore(args[1]);
    var accountId = Guid.Parse(args[2]);
    var target = decimal.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture);
    var openingDate = DateOnly.ParseExact(args[4], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    Money.Validate(target, "Actual balance");
    var accounts = store.Load();
    var account = accounts.Single(a => a.Id == accountId && a.IsSavings);
    if (account.OpeningDate != openingDate) throw new ArgumentException("Opening date does not match.");
    var todayForReconciliation = DateOnly.FromDateTime(DateTime.Today);
    var corrected = SavingsInterest.Recalculate(account, todayForReconciliation);
    for (var attempt = 0; corrected.CurrentBalance != target && attempt < 32; attempt++)
        corrected = SavingsInterest.Recalculate(corrected with { OpeningBalance = corrected.OpeningBalance + target - corrected.CurrentBalance }, todayForReconciliation);
    if (corrected.CurrentBalance != target) throw new ArgumentException("Could not reconcile balance exactly; no changes saved.");
    var backup = Path.Combine(ProfileLayout.ManualBackups(args[1]), "before-opening-reconciliation-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
    Directory.CreateDirectory(backup);
    File.Copy(store.FilePath, Path.Combine(backup, "accounts.json"));
    store.Save(accounts.Select(a => a.Id == accountId ? corrected : a).ToList());
    if (store.Load().Single(a => a.Id == accountId).CurrentBalance != target) throw new InvalidOperationException("Saved balance mismatch.");
    Console.WriteLine($"Opening balance at {openingDate:dd.MM.yyyy}: {Money.Format(corrected.OpeningBalance)}; current balance: {Money.Format(corrected.CurrentBalance)}");
    return;
}
if (args.Length == 6 && args[0] == "--move-interest-remove-duplicate")
{
    var store = new AccountStore(args[1]);
    var accountId = Guid.Parse(args[2]);
    var duplicateId = Guid.Parse(args[3]);
    var day = DateOnly.ParseExact(args[4], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    var timestamp = DateTime.ParseExact(args[5], "yyyy-MM-ddTHH:mm", System.Globalization.CultureInfo.InvariantCulture);
    var accounts = store.Load();
    var account = accounts.Single(a => a.Id == accountId && a.IsSavings);
    if (timestamp > DateTime.Now || DateOnly.FromDateTime(timestamp) < account.OpeningDate) throw new ArgumentException("Invalid interest timestamp.");
    var accrual = SavingsInterest.History(account, DateOnly.FromDateTime(DateTime.Today)).Single(a => a.Date == day);
    var duplicate = account.Transactions.SingleOrDefault(t => t.Id == duplicateId);
    if (duplicate is not null && (duplicate.IsTransfer || duplicate.Kind != CategoryKind.Income || duplicate.Amount != accrual.Amount)) throw new ArgumentException("Duplicate does not match interest income.");
    var corrected = SavingsInterest.Recalculate(account with
    {
        Transactions = account.Transactions.Where(t => t.Id != duplicateId).ToList(),
        InterestCorrections = [.. account.InterestCorrections.Where(c => c.Date != day), new(day, accrual.Amount, timestamp)]
    }, DateOnly.FromDateTime(DateTime.Today));
    var backup = Path.Combine(ProfileLayout.ManualBackups(args[1]), "before-interest-move-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
    Directory.CreateDirectory(backup);
    File.Copy(store.FilePath, Path.Combine(backup, "accounts.json"));
    store.Save(accounts.Select(a => a.Id == accountId ? corrected : a).ToList());
    Console.WriteLine($"Original interest: {Money.Format(accrual.Amount)} at {timestamp:dd.MM.yyyy HH:mm}; duplicate removed: {duplicate is not null}");
    return;
}
if (args.Length == 6 && args[0] == "--add-income")
{
    var store = new AccountStore(args[1]);
    var catalog = new CategoryStore(args[1]);
    var accountId = Guid.Parse(args[2]);
    var amount = decimal.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture);
    var timestamp = DateTime.ParseExact(args[4], "yyyy-MM-ddTHH:mm", System.Globalization.CultureInfo.InvariantCulture);
    var account = store.Load().Single(a => a.Id == accountId);
    if (account.Transactions.Any(t => t.Kind == CategoryKind.Income && !t.IsTransfer && t.Amount == amount && t.OccurredAt == timestamp && t.CategoryName == args[5]))
    {
        Console.WriteLine("Matching income already exists; no duplicate added.");
        return;
    }
    var backup = Path.Combine(ProfileLayout.ManualBackups(args[1]), "before-income-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
    Directory.CreateDirectory(backup);
    foreach (var file in new[] { "accounts.json", "categories.json", "profile.json", "recurring-payments.json" })
        if (File.Exists(file == "profile.json" ? Path.Combine(args[1], file) : ProfileLayout.ReadFile(args[1], file))) File.Copy(file == "profile.json" ? Path.Combine(args[1], file) : ProfileLayout.ReadFile(args[1], file), Path.Combine(backup, file));
    var categories = catalog.Load();
    var category = categories.SingleOrDefault(c => c.Kind == CategoryKind.Income && c.Name == args[5]);
    if (category is null)
    {
        category = new Category { Name = args[5], Kind = CategoryKind.Income, IconKey = "interest" };
        catalog.Save([.. categories, category]);
    }
    var income = BudgetOperations.Add(store, catalog, accountId, category.Id, CategoryKind.Income, amount, timestamp, "");
    Console.WriteLine($"Income saved: {income.Id}; amount: {Money.Format(income.Amount)}; date: {income.OccurredAt:dd.MM.yyyy HH:mm}");
    return;
}
if (args.Length == 9 && args[0] == "--apply-finance-settings")
{
    var store = new AccountStore(args[1]);
    var profile = new ProfileStore(args[1]);
    var accounts = store.Load();
    var sber = accounts.Single(a => a.Id == Guid.Parse(args[2]) && a.IsCredit);
    var paymentAmount = decimal.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture);
    var due = DateOnly.ParseExact(args[4], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    var tinkoff = accounts.Single(a => a.Id == Guid.Parse(args[5]) && a.IsCredit);
    var statementDay = int.Parse(args[6]);
    var budget = decimal.Parse(args[7], System.Globalization.CultureInfo.InvariantCulture);
    Guid? budgetAccountId = args[8] == "all" ? null : Guid.Parse(args[8]);
    (profile.Load() with { MonthlyBudget = budget, BudgetAccountId = budgetAccountId }).Validate();
    var updated = accounts.Select(a => a.Id == sber.Id ? a with { OpeningDebtPayments = [.. a.OpeningDebtPayments.Where(p => p.DueDate != due), new(due, paymentAmount)] }
        : a.Id == tinkoff.Id ? a with
        {
            GraceRule = GracePeriodRule.StatementMonthEnd, StatementDay = statementDay,
            Transactions = a.Transactions.Select(t => t.Kind == CategoryKind.Expense ? t with { CreditRule = GracePeriodRule.StatementMonthEnd, CreditStatementDay = statementDay, CreditGraceDays = a.GraceDays } : t).ToList(),
            Purchases = a.Purchases.Select(p => p with { Rule = GracePeriodRule.StatementMonthEnd, StatementDay = statementDay }).ToList()
        } : a).ToList();
    foreach (var account in updated) account.Validate();
    var backup = Path.Combine(ProfileLayout.ManualBackups(args[1]), "before-budget-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
    Directory.CreateDirectory(backup);
    foreach (var file in new[] { "accounts.json", "categories.json", "profile.json", "recurring-payments.json" })
        if (File.Exists(file == "profile.json" ? Path.Combine(args[1], file) : ProfileLayout.ReadFile(args[1], file))) File.Copy(file == "profile.json" ? Path.Combine(args[1], file) : ProfileLayout.ReadFile(args[1], file), Path.Combine(backup, file));
    store.Save(updated);
    profile.SetBudget(budget, budgetAccountId);
    var setupToday = DateOnly.FromDateTime(DateTime.Today);
    var spent = BudgetStatistics.Calculate(store.Load().Where(a => budgetAccountId is null || a.Id == budgetAccountId), new CategoryStore(args[1]).Load(), CategoryKind.Expense, new(new(setupToday.Year, setupToday.Month, 1), setupToday)).Total;
    Console.WriteLine($"Budget: {Money.Format(budget)}; spent: {Money.Format(spent)}; remaining: {Money.Format(budget - spent)}");
    Console.WriteLine("Opening balances unchanged. Backup: " + backup);
    return;
}
if (args.Length == 3 && args[0] == "--initialize-profile")
{
    var profileStore = ProfileStore.Initialize(args[1], args[2]);
    var pin = Console.ReadLine() ?? throw new ArgumentException("Не получен PIN через стандартный ввод.");
    if (profileStore.Load().HasPin)
    {
        if (!profileStore.VerifyPin(pin)) throw new ArgumentException("PIN существующего профиля не совпадает.");
    }
    else profileStore.SetInitialPin(pin);
    Console.WriteLine("Профиль Sandin подготовлен. PIN установлен без вывода кода.");
    return;
}
throw new ArgumentException("Unknown maintenance command. See docs/project-layout.md.");
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    Environment.ExitCode = 1;
}

internal sealed class ProfileWriteLease : IDisposable
{
    private readonly Mutex mutex;
    public ProfileWriteLease(string directory)
    {
        var identity = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        var suffix = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)));
        mutex = new Mutex(false, @"Local\SandinFinance." + suffix);
        var acquired = false;
        try { acquired = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) { mutex.Dispose(); throw new IOException("Close the application using this profile before modifying the statement."); }
    }
    public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
}

internal sealed record AccountReconciliation(string ExpectedSha256, Guid AccountId, DateOnly AsOf, decimal TargetBalance, List<Account> Replacements);
