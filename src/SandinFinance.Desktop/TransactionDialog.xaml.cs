using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SandinFinance.Core;

namespace SandinFinance.Desktop;

public partial class TransactionDialog : Window
{
    private readonly AccountStore store;
    private readonly CategoryStore categories;
    private readonly CategoryKind kind;
    public BudgetTransaction? SavedTransaction { get; private set; }

    public TransactionDialog(string directory, CategoryKind kind)
    {
        this.kind = kind;
        store = new AccountStore(directory);
        categories = new CategoryStore(directory);
        InitializeComponent();
        TimeInputFormatter.Attach(TimeInput);
        var income = kind == CategoryKind.Income;
        Title = income ? "Новый доход — SandinFinance" : "Новый расход — SandinFinance";
        HeadingText.Text = income ? "Новый доход" : "Новый расход";
        AccountLabel.Content = income ? "На какой счёт добавить" : "С какого счёта списать";
        SaveButton.Content = income ? "Сохранить доход" : "Сохранить расход";
        MerchantLabel.Content = income ? "Источник дохода (необязательно)" : "Магазин (необязательно)";
        AccountInput.ItemsSource = store.LoadAndAccrue(DateOnly.FromDateTime(DateTime.Today)).Where(a => !income || !a.IsCredit).ToList();
        CategoryInput.ItemsSource = categories.Load().Where(c => c.Kind == kind).ToList();
        AccountInput.SelectedIndex = 0;
        CategoryInput.SelectedIndex = 0;
        DateInput.SelectedDate = DateTime.Today;
        DateInput.DisplayDateEnd = DateTime.Today;
        TimeInput.Text = DateTime.Now.ToString("HH:mm");
        Loaded += (_, _) => AmountInput.Focus();
    }

    private void Account_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (BalanceText is null || DateInput is null) return;
        if (AccountInput.SelectedItem is Account account)
        {
            BalanceText.Text = $"{account.BalanceLabel}: {account.BalanceText}";
            var earliest = account.IsSavings ? account.InterestHistoryStart ?? account.InterestAsOf ?? account.OpeningDate : account.OpeningDate;
            DateInput.DisplayDateStart = earliest.ToDateTime(TimeOnly.MinValue);
        }
    }

    private void Input_Changed(object sender, RoutedEventArgs e) { if (ErrorText is not null) ErrorText.Text = ""; }

    private void Category_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (MerchantInput is null) return;
        var category = CategoryInput.SelectedItem as Category;
        MerchantLabel.Content = category?.IsTransport == true ? "Товар, услуга или место (необязательно)" : kind == CategoryKind.Income ? "Источник дохода (необязательно)" : "Магазин (необязательно)";
        MerchantInput.ItemsSource = category?.Merchants ?? new List<Merchant>();
        MerchantInput.Text = "";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        if (AccountInput.SelectedItem is not Account account || CategoryInput.SelectedItem is not Category category)
        {
            ErrorText.Text = "Выберите счёт и категорию. Если их ещё нет, создайте в разделах «Счета» и «Категории».";
            return;
        }
        if (!Money.TryParse(AmountInput.Text, out var amount)) { ErrorText.Text = "Введите сумму числом, например 1500,50."; return; }
        if (DateInput.SelectedDate is not { } date || DateInput.Text.Trim().Length == 0 ||
            !TimeOnly.TryParseExact(TimeInput.Text.Trim(), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            ErrorText.Text = "Укажите дату и время в формате часы:минуты, например 14:30.";
            return;
        }
        try
        {
            SavedTransaction = BudgetOperations.Add(store, categories, account.Id, category.Id, kind, amount,
                DateOnly.FromDateTime(date).ToDateTime(time), MerchantInput.Text);
            DialogResult = true;
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            ErrorText.Text = ex.Message;
        }
    }
}
