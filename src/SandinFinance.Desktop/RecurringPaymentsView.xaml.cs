using System.Windows;
using System.Windows.Controls;
using SandinFinance.Core;

namespace SandinFinance.Desktop;

public partial class RecurringPaymentsView : UserControl
{
    private string? directory;
    private RecurringPaymentStore? store;
    public event EventHandler? PaymentsChanged;
    public RecurringPaymentsView() { InitializeComponent(); }
    public void Initialize(string directory) { this.directory = directory; store = new(directory); Refresh(); }

    public void Refresh()
    {
        if (store is null || directory is null) return;
        try
        {
            var accounts = new AccountStore(directory).Load().ToDictionary(a => a.Id);
            var categories = new CategoryStore(directory).Load().ToDictionary(c => c.Id);
            var payments = store.Load().OrderBy(p => p.NextDueAt is null).ThenBy(p => p.NextDueAt).ThenBy(p => p.Name).ToList();
            PaymentsList.ItemsSource = payments.Select(p => new PaymentRow(p, accounts.GetValueOrDefault(p.AccountId)?.Name ?? "Счёт удалён", categories.GetValueOrDefault(p.CategoryId)?.Name ?? "Категория удалена", DateTime.Now)).ToList();
            EmptyState.Visibility = payments.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            CountText.Text = $"Платежи · {payments.Count}";
            CreateButton.IsEnabled = true;
            PaymentsList.IsEnabled = true;
            StatusText.Text = "Отметка оплаты обновляет срок; деньги автоматически не списываются.";
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            CreateButton.IsEnabled = false;
            PaymentsList.IsEnabled = false;
            StatusText.Text = $"Не удалось прочитать регулярные платежи. {ex.Message}";
        }
    }
    public void CreateFromExpense(BudgetEntry expense) => OpenDialog(null, expense);
    private void Create_Click(object sender, RoutedEventArgs e) => OpenDialog();
    private void Edit_Click(object sender, RoutedEventArgs e) { if (sender is FrameworkElement { DataContext: PaymentRow row }) OpenDialog(row.Payment); }
    private void OpenDialog(RecurringPayment? editing = null, BudgetEntry? expense = null)
    {
        if (directory is null || !CreateButton.IsEnabled) return;
        Execute(() =>
        {
            var dialog = new RecurringPaymentDialog(directory, editing, expense) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true) Changed();
        });
    }
    private void Complete_Click(object sender, RoutedEventArgs e)
    {
        if (store is null || sender is not FrameworkElement { DataContext: PaymentRow row } || row.Payment.NextDueAt is not { } due) return;
        Execute(() => { store.Complete(row.Payment.Id, due); Changed(); StatusText.Text = "Оплата отмечена. Остатки счетов не изменены."; });
    }
    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (store is null || sender is not FrameworkElement { DataContext: PaymentRow row }) return;
        Execute(() =>
        {
            var payments = store.Load();
            store.Save(payments.Select(p => p.Id == row.Payment.Id ? p with { IsEnabled = !p.IsEnabled } : p).ToList());
            Changed();
        });
    }
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (store is null || sender is not FrameworkElement { DataContext: PaymentRow row }) return;
        Execute(() =>
        {
            if (new DeleteRecurringPaymentDialog(row.Payment) { Owner = Window.GetWindow(this) }.ShowDialog() != true) return;
            store.Save(store.Load().Where(p => p.Id != row.Payment.Id).ToList());
            Changed();
        });
    }
    private void Changed() { Refresh(); PaymentsChanged?.Invoke(this, EventArgs.Empty); }
    private void Execute(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { StatusText.Text = ex.Message; }
    }
    private sealed record PaymentRow(RecurringPayment Payment, string AccountName, string CategoryName, DateTime Now)
    {
        public string ContextText => $"{AccountName} · {CategoryName}" + (string.IsNullOrEmpty(Payment.MerchantName) ? "" : $" · {Payment.MerchantName}");
        public bool IsOverdue => Payment.NextDueAt is { } due && due < Now;
        public bool CanComplete => Payment.NextDueAt is not null;
        public string StatusText => !Payment.IsEnabled ? "Приостановлен" : Payment.NextDueAt is null ? "Завершён" : IsOverdue ? "Срок прошёл" : "Ожидает оплаты";
        public string PauseText => Payment.IsEnabled ? "Приостановить" : "Возобновить";
        public string ScheduleText => Payment.RepeatText + (Payment.EndDate is { } end ? $" · до {end:dd.MM.yyyy}" : "") + $" · оплат отмечено: {Payment.CompletedCount}";
    }
}
