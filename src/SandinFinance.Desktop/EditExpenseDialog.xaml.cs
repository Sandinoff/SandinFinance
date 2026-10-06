using System.Globalization;
using System.Windows;
using SandinFinance.Core;

namespace SandinFinance.Desktop;

public partial class EditExpenseDialog : Window
{
    private readonly AccountStore store;
    private readonly BudgetEntry entry;

    public EditExpenseDialog(AccountStore store, BudgetEntry entry)
    {
        this.store = store;
        this.entry = entry;
        InitializeComponent();
        HeadingText.Text = entry.IsInterest ? "Уточнить проценты" : entry.IsTransfer ? "Изменить перевод" : entry.Kind == CategoryKind.Income ? "Изменить поступление" : "Изменить расход";
        Title = $"{HeadingText.Text} — SandinFinance";
        if (entry.IsInterest) { TimeLabel.Visibility = Visibility.Collapsed; TimeInput.Visibility = Visibility.Collapsed; }
        DetailText.Text = $"{entry.CategoryName} · {entry.DetailText}\nДата: {entry.OccurredAt:dd.MM.yyyy}";
        AmountInput.Text = entry.Amount.ToString("0.00", CultureInfo.GetCultureInfo("ru-RU"));
        TimeInputFormatter.Attach(TimeInput);
        TimeInput.Text = entry.OccurredAt.ToString("HH:mm");
        Loaded += (_, _) => { AmountInput.Focus(); AmountInput.SelectAll(); };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        if (!Money.TryParse(AmountInput.Text, out var amount)) { ErrorText.Text = "Введите сумму числом, например 1500,50."; return; }
        if (!TimeOnly.TryParseExact(TimeInput.Text.Trim(), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        { ErrorText.Text = "Укажите время в формате часы:минуты."; return; }
        try
        {
            if (entry.IsInterest) SavingsInterest.Correct(store, entry.AccountId, entry.InterestDate ?? DateOnly.FromDateTime(entry.OccurredAt).AddDays(-1), amount);
            else if (entry.IsTransfer) AccountTransfers.Edit(store, entry.AccountId, entry.Id, amount, time);
            else if (entry.Kind == CategoryKind.Income) BudgetOperations.EditIncome(store, entry.AccountId, entry.Id, amount, time);
            else BudgetOperations.EditExpense(store, entry.AccountId, entry.Id, amount, time);
            DialogResult = true;
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { ErrorText.Text = ex.Message; }
    }

    private void Input_Changed(object sender, RoutedEventArgs e)
    {
        if (ErrorText is not null) ErrorText.Text = "";
    }
}
