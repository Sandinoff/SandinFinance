using System.Windows;
using SandinFinance.Core;

namespace SandinFinance.Desktop;

public partial class PurchaseDialog : Window
{
    private readonly AccountStore store;
    private readonly List<Account> accounts;
    private readonly Account account;

    public PurchaseDialog(AccountStore store, List<Account> accounts, Account account)
    {
        this.store = store;
        this.accounts = accounts;
        this.account = account;
        InitializeComponent();
        AccountText.Text = account.Name + "\n" + CreditGrace.Description(account);
        DateInput.SelectedDate = DateTime.Today;
        DateInput.DisplayDateStart = account.OpeningDate.ToDateTime(TimeOnly.MinValue);
        DateInput.DisplayDateEnd = DateTime.Today;
        Loaded += (_, _) => NameInput.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        if (!Money.TryParse(AmountInput.Text, out var amount))
        {
            ErrorText.Text = "Введите сумму числом, например 5000,50.";
            return;
        }
        if (DateInput.SelectedDate is not { } date || DateInput.Text.Trim().Length == 0)
        {
            ErrorText.Text = "Укажите дату покупки.";
            return;
        }
        try
        {
            var purchase = new CreditPurchase
            {
                Name = NameInput.Text.Trim(), Amount = amount, Date = DateOnly.FromDateTime(date),
                GraceDays = account.GraceDays, Rule = account.GraceRule, PaymentParts = account.PaymentParts
            };
            if (purchase.Date < account.OpeningDate) throw new ArgumentException("Покупка должна быть не раньше начала учёта.");
            purchase.Validate();
            var updated = account with { Purchases = [.. account.Purchases, purchase] };
            updated.Validate();
            store.Save(accounts.Select(a => SavingsInterest.Accrue(a.Id == updated.Id ? updated : a,
                DateOnly.FromDateTime(DateTime.Today))).ToList());
            DialogResult = true;
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException)
        {
            ErrorText.Text = ex is ArgumentException ? ex.Message : $"Не удалось сохранить покупку. {ex.Message}";
        }
    }
}
