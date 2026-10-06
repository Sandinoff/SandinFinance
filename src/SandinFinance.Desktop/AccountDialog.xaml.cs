using System.Windows;
using System.Windows.Controls;
using SandinFinance.Core;

namespace SandinFinance.Desktop;

public partial class AccountDialog : Window
{
    private readonly AccountStore store;
    private readonly List<Account> existing;
    private readonly Account? editing;
    public Account? CreatedAccount { get; private set; }

    public AccountDialog(AccountStore store, List<Account> existing, Account? editing = null)
    {
        this.store = store;
        this.existing = existing;
        this.editing = editing;
        InitializeComponent();
        DateInput.SelectedDate = DateTime.Today;
        DateInput.DisplayDateEnd = DateTime.Today;
        if (editing is not null)
        {
            Title = "Настройка счёта — SandinFinance";
            HeadingText.Text = "Настройка счёта";
            IntroText.Text = "Название и условия можно изменить. Начальная сумма и дата остаются точкой отсчёта.";
            NameInput.Text = editing.Name;
            TypeInput.SelectedIndex = (int)editing.Type;
            TypeInput.IsEnabled = false;
            BalanceInput.Text = editing.OpeningBalance.ToString(System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
            LimitInput.Text = editing.CreditLimit.ToString(System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
            SpendingLimitInput.Text = editing.MonthlySpendingLimit.ToString(System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
            DateInput.SelectedDate = editing.OpeningDate.ToDateTime(TimeOnly.MinValue);
            BalanceInput.IsReadOnly = true;
            DateInput.IsEnabled = false;
            SaveButton.Content = "Сохранить";
            RateInput.Text = editing.AnnualInterestRate.ToString(System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
            PayoutInput.SelectedIndex = (int)editing.InterestPayout;
            GraceDaysInput.Text = editing.GraceDays.ToString();
            GraceRuleInput.SelectedIndex = editing.GraceRule == GracePeriodRule.PurchaseMonthPlusThreeMonths ? 4 : editing.GraceRule == GracePeriodRule.StatementMonthEnd ? 3 : editing.GraceRule == GracePeriodRule.MonthEndPlusDays ? 2 : (int)editing.GraceRule;
            StatementDayInput.Text = (editing.StatementDay == 0 ? 8 : editing.StatementDay).ToString();
            OpeningPaymentInput.Text = (editing.OpeningDebtPayments.FirstOrDefault()?.Amount ?? 0).ToString(System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
            OpeningPaymentDateInput.SelectedDate = editing.OpeningDebtPayments.FirstOrDefault()?.DueDate.ToDateTime(TimeOnly.MinValue);
        }
        Loaded += (_, _) => NameInput.Focus();
    }

    private void TypeInput_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CreditFields is null) return;
        var credit = TypeInput.SelectedIndex == 2;
        CreditFields.Visibility = credit ? Visibility.Visible : Visibility.Collapsed;
        SavingsFields.Visibility = TypeInput.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
        BalanceLabel.Content = credit ? "Текущий долг, ₽" : "Начальный остаток, ₽";
        HintText.Text = credit
            ? "Лимит — деньги банка. Он не входит в ваши собственные средства. Если долга нет, укажите 0."
            : "Начальная сумма не считается доходом.";
        ErrorText.Text = "";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        if (!Money.TryParse(BalanceInput.Text, out var balance))
        {
            ErrorText.Text = "Введите сумму числом, например 5000 или 5000,50, без пробелов.";
            BalanceInput.Focus();
            return;
        }
        var type = (AccountType)TypeInput.SelectedIndex;
        decimal limit = 0;
        if (type == AccountType.CreditCard && !Money.TryParse(LimitInput.Text, out limit))
        {
            ErrorText.Text = "Введите кредитный лимит числом, например 100000.";
            LimitInput.Focus();
            return;
        }
        if (DateInput.SelectedDate is not { } date || DateInput.Text.Trim().Length == 0)
        {
            ErrorText.Text = "Выберите дату начала учёта.";
            return;
        }
        decimal rate = 0;
        if (type == AccountType.Savings && !Money.TryParse(RateInput.Text, out rate))
        {
            ErrorText.Text = "Введите годовую ставку числом, например 12,5.";
            return;
        }
        var graceDays = 0;
        if (type == AccountType.CreditCard && !int.TryParse(GraceDaysInput.Text, out graceDays))
        {
            ErrorText.Text = "Введите льготный период целым числом дней.";
            return;
        }
        var today = DateOnly.FromDateTime(DateTime.Today);
        try
        {
            var rule = type != AccountType.CreditCard ? GracePeriodRule.Unspecified : GraceRuleInput.SelectedIndex switch { 1 => GracePeriodRule.EndOfNextMonth, 2 => GracePeriodRule.MonthEndPlusDays, 3 => GracePeriodRule.StatementMonthEnd, 4 => GracePeriodRule.PurchaseMonthPlusThreeMonths, _ => GracePeriodRule.Unspecified };
            var spendingLimit = 0m;
            if (type == AccountType.CreditCard && !Money.TryParse(SpendingLimitInput.Text, out spendingLimit)) throw new ArgumentException("Введите лимит трат числом.");
            var statementDay = 0;
            if (rule == GracePeriodRule.StatementMonthEnd && !int.TryParse(StatementDayInput.Text, out statementDay)) throw new ArgumentException("Укажите день выписки.");
            var openingPayments = editing?.OpeningDebtPayments.ToList() ?? [];
            if (type == AccountType.CreditCard)
            {
                if (!Money.TryParse(OpeningPaymentInput.Text, out var payment)) throw new ArgumentException("Укажите сумму начального долга к погашению.");
                if (openingPayments.Count > 0) openingPayments.RemoveAt(0);
                if (payment > 0)
                {
                    if (OpeningPaymentDateInput.SelectedDate is not { } dueDate) throw new ArgumentException("Укажите срок погашения начального долга.");
                    openingPayments.Insert(0, new(DateOnly.FromDateTime(dueDate), payment));
                }
            }
            var basis = editing is null ? new Account
            {
                Name = NameInput.Text.Trim(), Type = type, OpeningBalance = balance,
                CreditLimit = limit, OpeningDate = DateOnly.FromDateTime(date),
                AnnualInterestRate = rate, InterestPayout = type == AccountType.Savings ? (InterestPayout)PayoutInput.SelectedIndex : InterestPayout.Daily,
                DailyInterestPostingTime = type == AccountType.Savings ? new TimeOnly(1, 0) : null
            } : editing.IsSavings
                ? SavingsInterest.ChangeSettings(editing, rate, (InterestPayout)PayoutInput.SelectedIndex, today) : editing;
            var account = basis with
            {
                Name = NameInput.Text.Trim(), CreditLimit = limit, MonthlySpendingLimit = spendingLimit,
                GraceDays = graceDays,
                GraceRule = rule, StatementDay = statementDay, OpeningDebtPayments = openingPayments,
                PaymentParts = 1
            };
            account.Validate();
            var updated = (editing is null ? existing.Append(account)
                : existing.Select(a => a.Id == account.Id ? account : a)).Select(a => SavingsInterest.Accrue(a, today)).ToList();
            store.Save(updated);
            CreatedAccount = updated.Single(a => a.Id == account.Id);
            DialogResult = true;
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException)
        {
            ErrorText.Text = ex is ArgumentException ? ex.Message : $"Не удалось сохранить счёт. {ex.Message}";
        }
    }
}
