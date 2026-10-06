using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SandinFinance.Core;

namespace SandinFinance.Desktop;

public partial class HomeView : UserControl
{
    private string? directory;
    private BudgetPeriod period = BudgetPeriod.Month;
    private bool ready;
    public event EventHandler? OperationSaved;

    public HomeView() { InitializeComponent(); }

    public void Initialize(string directory)
    {
        this.directory = directory;
        AnchorInput.SelectedDate = DateTime.Today;
        StartInput.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        EndInput.SelectedDate = DateTime.Today;
        ready = true;
        Refresh();
    }

    private void Kind_Changed(object sender, SelectionChangedEventArgs e) { if (ready) Refresh(); }
    private void Date_Changed(object sender, SelectionChangedEventArgs e) { if (ready) Refresh(); }
    private void Period_Changed(object sender, RoutedEventArgs e)
    {
        if (!ready || sender is not RadioButton radio) return;
        period = (BudgetPeriod)int.Parse((string)radio.Tag);
        AnchorPanel.Visibility = period == BudgetPeriod.Custom ? Visibility.Collapsed : Visibility.Visible;
        CustomPanel.Visibility = period == BudgetPeriod.Custom ? Visibility.Visible : Visibility.Collapsed;
        Refresh();
    }

    public void Refresh()
    {
        if (!ready || directory is null) return;
        try
        {
            var range = DateRange.For(period, DateOnly.FromDateTime(AnchorInput.SelectedDate ?? DateTime.Today),
                StartInput.SelectedDate is { } start ? DateOnly.FromDateTime(start) : null, EndInput.SelectedDate is { } end ? DateOnly.FromDateTime(end) : null);
            var accounts = new AccountStore(directory).LoadAndAccrue(DateOnly.FromDateTime(DateTime.Today));
            var categories = new CategoryStore(directory).Load();
            var kind = KindTabs.SelectedIndex == 1 ? CategoryKind.Income : CategoryKind.Expense;
            var summary = BudgetStatistics.Calculate(accounts, categories, kind, range);
            RangeText.Text = $"{range.Start:dd.MM.yyyy} — {range.End:dd.MM.yyyy}";
            TotalText.Text = Money.Format(summary.Total);
            TotalCaption.Text = kind == CategoryKind.Expense ? "Расходы за период" : "Доходы за период";
            CategoryTotalsList.ItemsSource = summary.Categories;
            EntriesList.ItemsSource = summary.Entries;
            EmptyText.Visibility = summary.Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            DrawChart(summary.Categories);
            AddOperationButton.IsEnabled = true;
            StatusText.Text = "Наведите на сектор диаграммы для подробностей. Раскройте категорию, чтобы увидеть магазины или источники.";
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            AddOperationButton.IsEnabled = false;
            StatusText.Text = ex.Message;
        }
    }

    private void DrawChart(IReadOnlyList<CategoryTotal> categories)
    {
        PieCanvas.Children.Clear();
        if (categories.Count == 0)
        {
            var empty = new Ellipse { Width = 224, Height = 224, Stroke = new SolidColorBrush(Color.FromRgb(224, 231, 234)), StrokeThickness = 44 };
            Canvas.SetLeft(empty, 48); Canvas.SetTop(empty, 48); PieCanvas.Children.Add(empty); return;
        }
        var angle = -Math.PI / 2;
        foreach (var category in categories)
        {
            var sweep = (double)(category.Share / 100m) * Math.PI * 2;
            Shape shape;
            var color = new SolidColorBrush((Color)ColorConverter.ConvertFromString(category.ColorHex));
            if (categories.Count == 1)
            {
                shape = new Ellipse { Width = 224, Height = 224, Stroke = color, StrokeThickness = 44 };
                Canvas.SetLeft(shape, 48); Canvas.SetTop(shape, 48);
            }
            else
            {
                var geometry = new StreamGeometry();
                using (var context = geometry.Open())
                {
                    context.BeginFigure(PointAt(134, angle), true, true);
                    context.ArcTo(PointAt(134, angle + sweep), new Size(134, 134), 0, sweep > Math.PI, SweepDirection.Clockwise, true, false);
                    context.LineTo(PointAt(90, angle + sweep), true, false);
                    context.ArcTo(PointAt(90, angle), new Size(90, 90), 0, sweep > Math.PI, SweepDirection.Counterclockwise, true, false);
                }
                geometry.Freeze();
                shape = new System.Windows.Shapes.Path { Data = geometry, Fill = color, Stroke = Brushes.White, StrokeThickness = 1.5 };
            }
            shape.ToolTip = category.Tooltip;
            System.Windows.Automation.AutomationProperties.SetName(shape, $"{category.Name}: {category.AmountText}, {category.ShareText}");
            shape.MouseEnter += (_, _) => shape.Opacity = 0.75;
            shape.MouseLeave += (_, _) => shape.Opacity = 1;
            PieCanvas.Children.Add(shape);
            angle += sweep;
        }
    }

    private static Point PointAt(double radius, double angle) => new(160 + radius * Math.Cos(angle), 160 + radius * Math.Sin(angle));
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        CreateOperation(KindTabs.SelectedIndex == 1 ? CategoryKind.Income : CategoryKind.Expense);
    }
    private void DeleteExpense_Click(object sender, RoutedEventArgs e)
    {
        if (directory is null || !AddOperationButton.IsEnabled || sender is not FrameworkElement { DataContext: BudgetEntry entry } || !entry.CanDeleteExpense) return;
        try
        {
            var dialog = new DeleteExpenseDialog(entry) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() != true) return;
            BudgetOperations.DeleteExpense(new AccountStore(directory), entry.AccountId, entry.Id);
            Refresh();
            OperationSaved?.Invoke(this, EventArgs.Empty);
            StatusText.Text = "Расход удалён. Остаток, проценты и статистика пересчитаны.";
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            StatusText.Text = $"Не удалось удалить расход. {ex.Message}";
        }
    }

    private void EditExpense_Click(object sender, RoutedEventArgs e)
    {
        if (directory is null || !AddOperationButton.IsEnabled || sender is not FrameworkElement { DataContext: BudgetEntry entry } || !entry.CanEdit) return;
        try
        {
            var dialog = new EditExpenseDialog(new AccountStore(directory), entry) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() != true) return;
            Refresh();
            OperationSaved?.Invoke(this, EventArgs.Empty);
            StatusText.Text = "Операция изменена. Остаток и статистика пересчитаны.";
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            StatusText.Text = $"Не удалось изменить расход. {ex.Message}";
        }
    }

    private void CreateOperation(CategoryKind kind)
    {
        if (directory is null) return;
        try
        {
            var dialog = new TransactionDialog(directory, kind) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
            {
                KindTabs.SelectedIndex = kind == CategoryKind.Income ? 1 : 0;
                Refresh();
                OperationSaved?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            StatusText.Text = ex.Message;
        }
    }
}
