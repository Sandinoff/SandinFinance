using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Input;
using System.Windows.Media.Effects;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
using SandinFinance.Core;

namespace SandinFinance.Desktop;

public partial class HomeView : UserControl
{
    private string? directory;
    private BudgetPeriod period = BudgetPeriod.Month;
    private bool ready;
    private BudgetSummary? displayedSummary;
    private DateRange? displayedRange;
    private List<Category> displayedCategories = [];
    private CategoryKind displayedKind;
    private readonly Popup detailsPopup = new() { AllowsTransparency = true, StaysOpen = false, Placement = PlacementMode.RelativePoint };
    private Shape? selectedShape;
    private Window? popupOwner;
    public event EventHandler? OperationSaved;
    public event Action<BudgetEntry>? MakeRecurringRequested;

    public HomeView()
    {
        InitializeComponent();
        detailsPopup.Closed += (_, _) =>
        {
            if (selectedShape is not null) { selectedShape.Effect = null; selectedShape.Opacity = 1; }
            selectedShape = null;
        };
        Loaded += (_, _) =>
        {
            popupOwner = Window.GetWindow(this);
            if (popupOwner is null) return;
            popupOwner.Deactivated += DismissDetails;
            popupOwner.LocationChanged += DismissDetails;
            popupOwner.SizeChanged += DismissDetails;
            popupOwner.PreviewKeyDown += Popup_KeyDown;
        };
        Unloaded += (_, _) =>
        {
            detailsPopup.IsOpen = false;
            if (popupOwner is null) return;
            popupOwner.Deactivated -= DismissDetails;
            popupOwner.LocationChanged -= DismissDetails;
            popupOwner.SizeChanged -= DismissDetails;
            popupOwner.PreviewKeyDown -= Popup_KeyDown;
            popupOwner = null;
        };
        IsVisibleChanged += (_, _) => { if (!IsVisible) detailsPopup.IsOpen = false; };
    }

    private void DismissDetails(object? sender, EventArgs e) => detailsPopup.IsOpen = false;
    private void Popup_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !detailsPopup.IsOpen) return;
        detailsPopup.IsOpen = false;
        e.Handled = true;
    }

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
        detailsPopup.IsOpen = false;
        try
        {
            var range = DateRange.For(period, DateOnly.FromDateTime(AnchorInput.SelectedDate ?? DateTime.Today),
                StartInput.SelectedDate is { } start ? DateOnly.FromDateTime(start) : null, EndInput.SelectedDate is { } end ? DateOnly.FromDateTime(end) : null);
            var accounts = new AccountStore(directory).LoadAndAccrue(DateOnly.FromDateTime(DateTime.Today));
            var categories = new CategoryStore(directory).Load();
            var kind = KindTabs.SelectedIndex == 1 ? CategoryKind.Income : CategoryKind.Expense;
            var summary = BudgetStatistics.Calculate(accounts, categories, kind, range);
            displayedSummary = summary;
            displayedRange = range;
            displayedCategories = categories;
            displayedKind = kind;
            RangeText.Text = $"{range.Start:dd.MM.yyyy} — {range.End:dd.MM.yyyy}";
            TotalText.Text = Money.Format(summary.Total);
            TotalCaption.Text = kind == CategoryKind.Expense ? "Расходы за период" : "Доходы за период";
            CategoryTotalsList.ItemsSource = summary.Categories;
            EntriesList.ItemsSource = summary.Entries;
            EmptyText.Visibility = summary.Categories.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            DrawChart(summary.Categories);
            AddOperationButton.IsEnabled = true;
            StatusText.Text = "Нажмите на сектор диаграммы, чтобы посмотреть подробности категории за выбранный период.";
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            AddOperationButton.IsEnabled = false;
            displayedSummary = null;
            StatusText.Text = ex.Message;
        }
    }

    private void DrawChart(IReadOnlyList<CategoryTotal> categories)
    {
        PieCanvas.Children.Clear();
        if (categories.Count == 0)
        {
            var empty = new Ellipse { Width = 224, Height = 224, Stroke = (Brush)FindResource("OutlineSoft"), StrokeThickness = 44 };
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
                shape = new System.Windows.Shapes.Path { Data = geometry, Fill = color, Stroke = (Brush)FindResource("Surface"), StrokeThickness = 1.5 };
            }
            shape.ToolTip = category.Tooltip;
            shape.DataContext = category;
            shape.Cursor = Cursors.Hand;
            shape.Focusable = true;
            System.Windows.Automation.AutomationProperties.SetName(shape, $"{category.Name}: {category.AmountText}, {category.ShareText}");
            shape.MouseEnter += (_, _) => shape.Opacity = 0.75;
            shape.MouseLeave += (_, _) => shape.Opacity = 1;
            var sectorAnchor = PointAt(112, angle + sweep / 2);
            shape.MouseLeftButtonUp += (_, e) => { e.Handled = true; ShowCategoryDetails(category, shape, e.GetPosition(PieCanvas)); };
            shape.KeyDown += (_, e) =>
            {
                if (e.Key is not (Key.Enter or Key.Space)) return;
                e.Handled = true;
                ShowCategoryDetails(category, shape, sectorAnchor);
            };
            PieCanvas.Children.Add(shape);
            angle += sweep;
        }
    }

    private void ShowCategoryDetails(CategoryTotal category, Shape shape, Point anchor)
    {
        if (displayedSummary is null || displayedRange is null) return;
        if (detailsPopup.IsOpen && selectedShape == shape) { detailsPopup.IsOpen = false; return; }
        detailsPopup.IsOpen = false;
        var entries = displayedSummary.Entries.Where(e => !e.IsTransfer && e.Amount > 0 && e.CategoryId == category.Id).ToList();
        var catalogCategory = displayedCategories.FirstOrDefault(c => c.Id == category.Id);
        var breakdownHeading = entries.All(e => e.IsInterest) ? "По счетам" : catalogCategory?.IsTransport == true ? "Товары и услуги" : displayedKind == CategoryKind.Income ? "Источники дохода" : "Магазины";
        selectedShape = shape;
        shape.Effect = new DropShadowEffect { Color = (Color)ColorConverter.ConvertFromString(category.ColorHex), BlurRadius = 12, ShadowDepth = 0, Opacity = 0.5 };
        var card = new CategoryDetailsPopup(category, entries, displayedKind, displayedRange, breakdownHeading);
        card.CloseRequested += (_, _) => detailsPopup.IsOpen = false;
        card.PreviewKeyDown += Popup_KeyDown;
        detailsPopup.Child = card;
        detailsPopup.PlacementTarget = PieCanvas;
        detailsPopup.PlacementRectangle = new Rect(Math.Clamp(anchor.X, 0, 320), Math.Clamp(anchor.Y, 0, 320), 1, 1);
        detailsPopup.HorizontalOffset = 10;
        detailsPopup.VerticalOffset = 10;
        detailsPopup.IsOpen = true;
        var slide = new TranslateTransform(0, 7);
        card.RenderTransform = slide;
        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(7, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private static Point PointAt(double radius, double angle) => new(160 + radius * Math.Cos(angle), 160 + radius * Math.Sin(angle));
    private void MakeRecurring_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BudgetEntry entry } && entry.CanMakeRecurring) MakeRecurringRequested?.Invoke(entry);
    }
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
