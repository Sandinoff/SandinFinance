using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SandinFinance.Core;

namespace SandinFinance.Desktop;

public partial class RecurringPaymentDialog : Window
{
    private readonly RecurringPaymentStore store;
    private readonly RecurringPayment? editing;
    private bool ready;
    public RecurringPayment? SavedPayment { get; private set; }

    public RecurringPaymentDialog(string directory, RecurringPayment? editing = null, BudgetEntry? expense = null)
    {
        this.editing = editing;
        store = new(directory);
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height;
        TimeInputFormatter.Attach(TimeInput);
        EditableComboBoxDropdown.Attach(MerchantInput);
        AccountInput.ItemsSource = new AccountStore(directory).Load().ToList();
        CategoryInput.ItemsSource = new CategoryStore(directory).Load().Where(c => c.Kind == CategoryKind.Expense).ToList();
        RepeatInput.ItemsSource = new[] { new RepeatChoice(PaymentRepeat.None, "Без повторения"), new RepeatChoice(PaymentRepeat.Weekly, "Неделя"), new RepeatChoice(PaymentRepeat.Monthly, "Месяц"), new RepeatChoice(PaymentRepeat.Yearly, "Год") };
        AccountInput.SelectedItem = AccountInput.Items.Cast<Account>().FirstOrDefault(a => a.Id == (editing?.AccountId ?? expense?.AccountId));
        if (editing is null && AccountInput.SelectedItem is null) AccountInput.SelectedIndex = 0;
        CategoryInput.SelectedItem = CategoryInput.Items.Cast<Category>().FirstOrDefault(c => c.Id == (editing?.CategoryId ?? expense?.CategoryId));
        if (editing is null && CategoryInput.SelectedItem is null) CategoryInput.SelectedIndex = 0;
        NameInput.Text = editing?.Name ?? (expense is null ? "" : string.IsNullOrEmpty(expense.MerchantName) ? expense.CategoryName : expense.MerchantName);
        AmountInput.Text = (editing?.Amount ?? expense?.Amount)?.ToString("0.00", CultureInfo.GetCultureInfo("ru-RU")) ?? "";
        MerchantInput.Text = editing?.MerchantName ?? expense?.MerchantName ?? "";
        var first = editing?.FirstDueAt ?? (expense is null ? DateTime.Today.AddMonths(1).AddHours(9) : expense.OccurredAt.AddMonths(1));
        DateInput.SelectedDate = first.Date;
        TimeInput.Text = first.ToString("HH:mm");
        RepeatInput.SelectedValue = editing?.Repeat ?? PaymentRepeat.Monthly;
        IntervalInput.Text = (editing?.Interval ?? 1).ToString(CultureInfo.InvariantCulture);
        HasEndInput.IsChecked = editing?.EndDate is not null;
        EndInput.SelectedDate = (editing?.EndDate ?? DateOnly.FromDateTime(first).AddYears(1)).ToDateTime(TimeOnly.MinValue);
        if (editing is not null) HeadingText.Text = "Настройка платежа";
        ready = true;
        UpdateSchedule();
        Loaded += (_, _) => NameInput.Focus();
    }

    private void Category_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (MerchantInput is null) return;
        var category = CategoryInput.SelectedItem as Category;
        MerchantLabel.Content = category?.IsTransport == true ? "Товары и услуги (необязательно)" : "Магазин (необязательно)";
        MerchantInput.ItemsSource = category?.Merchants;
        MerchantInput.SelectedIndex = -1;
        MerchantInput.Text = "";
    }
    private void Schedule_Changed(object sender, RoutedEventArgs e) { if (ready) UpdateSchedule(); }
    private void UpdateSchedule()
    {
        IntervalInput.IsEnabled = RepeatInput.SelectedValue is PaymentRepeat repeat && repeat != PaymentRepeat.None;
        try
        {
            if (DateInput.SelectedDate is not { } date || !TimeOnly.TryParseExact(TimeInput.Text.Trim(), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) || RepeatInput.SelectedValue is not PaymentRepeat selectedRepeat || !int.TryParse(IntervalInput.Text, out var interval) || interval is < 1 or > 120) throw new ArgumentException();
            var preview = new RecurringPayment { Name = "Расписание", FirstDueAt = DateOnly.FromDateTime(date).ToDateTime(time), Repeat = selectedRepeat, Interval = interval, CompletedCount = selectedRepeat == PaymentRepeat.None ? Math.Min(editing?.CompletedCount ?? 0, 1) : editing?.CompletedCount ?? 0, EndDate = HasEndInput.IsChecked == true && EndInput.SelectedDate is { } end ? DateOnly.FromDateTime(end) : null };
            NextDueText.Text = "Следующий срок: " + preview.DueText;
        }
        catch (ArgumentException) { NextDueText.Text = "Укажите дату, время и повторение"; }
    }
    private RecurringPayment Build()
    {
        if (AccountInput.SelectedItem is not Account account || CategoryInput.SelectedItem is not Category category) throw new ArgumentException("Выберите счёт и категорию расходов.");
        if (!Money.TryParse(AmountInput.Text, out var amount)) throw new ArgumentException("Введите сумму платежа.");
        if (DateInput.SelectedDate is not { } date || string.IsNullOrWhiteSpace(DateInput.Text) || !TimeOnly.TryParseExact(TimeInput.Text.Trim(), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) throw new ArgumentException("Укажите дату и время в формате 14:30.");
        if (RepeatInput.SelectedValue is not PaymentRepeat repeat) throw new ArgumentException("Выберите повторение.");
        var interval = 1;
        if (repeat != PaymentRepeat.None && !int.TryParse(IntervalInput.Text, out interval)) throw new ArgumentException("Введите целый интервал повторения.");
        if (HasEndInput.IsChecked == true && (EndInput.SelectedDate is null || string.IsNullOrWhiteSpace(EndInput.Text))) throw new ArgumentException("Укажите последний срок.");
        return new RecurringPayment
        {
            Id = editing?.Id ?? Guid.NewGuid(), Name = NameInput.Text.Trim(), Amount = amount,
            AccountId = account.Id, CategoryId = category.Id, MerchantName = MerchantInput.Text.Trim(),
            FirstDueAt = DateOnly.FromDateTime(date).ToDateTime(time), Repeat = repeat, Interval = repeat == PaymentRepeat.None ? 1 : interval,
            EndDate = HasEndInput.IsChecked == true ? DateOnly.FromDateTime(EndInput.SelectedDate!.Value) : null,
            CompletedCount = repeat == PaymentRepeat.None ? Math.Min(editing?.CompletedCount ?? 0, 1) : editing?.CompletedCount ?? 0,
            LastCompletedAt = editing?.LastCompletedAt, IsEnabled = editing?.IsEnabled ?? true
        };
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        try
        {
            var payment = Build();
            payment.Validate();
            var payments = store.Load();
            if (editing is not null && !payments.Any(p => p.Id == editing.Id)) throw new ArgumentException("Этот платёж уже удалён. Обновите список.");
            store.Save(editing is null ? [.. payments, payment] : payments.Select(p => p.Id == payment.Id ? payment : p).ToList());
            SavedPayment = payment;
            DialogResult = true;
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { ErrorText.Text = ex.Message; }
    }
    private sealed record RepeatChoice(PaymentRepeat Value, string Name);
}
