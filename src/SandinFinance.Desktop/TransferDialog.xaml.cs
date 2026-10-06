using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SandinFinance.Core;

namespace SandinFinance.Desktop;

public partial class TransferDialog : Window
{
    private readonly AccountStore store;
    public Guid? SavedTransferId { get; private set; }

    public TransferDialog(AccountStore store)
    {
        this.store = store;
        InitializeComponent();
        TimeInputFormatter.Attach(TimeInput);
        var accounts = store.LoadAndAccrue(DateOnly.FromDateTime(DateTime.Today));
        FromInput.ItemsSource = accounts;
        ToInput.ItemsSource = accounts;
        FromInput.SelectedIndex = 0;
        ToInput.SelectedIndex = accounts.Count > 1 ? 1 : 0;
        DateInput.DisplayDateEnd = DateTime.Today;
        DateInput.SelectedDate = DateTime.Today;
        TimeInput.Text = DateTime.Now.ToString("HH:mm");
        Loaded += (_, _) => AmountInput.Focus();
    }

    private static DateOnly Earliest(Account account) => account.IsSavings ? account.InterestHistoryStart ?? account.InterestAsOf ?? account.OpeningDate : account.OpeningDate;
    private void Account_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (DateInput is null) return;
        if (FromInput.SelectedItem is Account from) FromBalanceText.Text = $"{from.BalanceLabel}: {from.BalanceText}";
        if (ToInput.SelectedItem is Account to) ToBalanceText.Text = to.IsCredit ? $"Долг: {Money.Format(to.Debt)}. Перевод уменьшит задолженность." : $"Остаток: {to.BalanceText}";
        if (FromInput.SelectedItem is Account source && ToInput.SelectedItem is Account destination)
        {
            var earliest = Earliest(source) > Earliest(destination) ? Earliest(source) : Earliest(destination);
            DateInput.DisplayDateStart = earliest.ToDateTime(TimeOnly.MinValue);
            HintText.Text = source.IsCredit ? "Перевод с кредитки увеличивает долг и получает такой же беспроцентный период, как покупка.\n" + CreditGrace.Description(source)
                : "Перевод сохранится в истории обоих счетов и не будет считаться расходом или доходом бюджета.";
        }
        if (ErrorText is not null) ErrorText.Text = "";
    }

    private void Input_Changed(object sender, RoutedEventArgs e) { if (ErrorText is not null) ErrorText.Text = ""; }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        if (FromInput.SelectedItem is not Account from || ToInput.SelectedItem is not Account to)
        { ErrorText.Text = "Выберите счёт списания и счёт зачисления."; return; }
        if (!Money.TryParse(AmountInput.Text, out var amount)) { ErrorText.Text = "Введите сумму числом, например 1000,50."; return; }
        if (DateInput.SelectedDate is not { } date || DateInput.Text.Trim().Length == 0 ||
            !TimeOnly.TryParseExact(TimeInput.Text.Trim(), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        { ErrorText.Text = "Укажите дату и время в формате часы:минуты."; return; }
        try
        {
            SavedTransferId = AccountTransfers.Add(store, from.Id, to.Id, amount, DateOnly.FromDateTime(date).ToDateTime(time));
            DialogResult = true;
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { ErrorText.Text = ex.Message; }
    }
}
