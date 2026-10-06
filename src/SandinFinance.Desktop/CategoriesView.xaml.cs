using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SandinFinance.Core;

namespace SandinFinance.Desktop;

public partial class CategoriesView : UserControl
{
    private readonly ObservableCollection<Category> categories = [];
    private readonly ICollectionView categoryView;
    private CategoryStore? store;
    private CategoryKind kind;
    private Point? dragStart;
    private const string CategoryDragFormat = "SandinFinance.Category";

    public CategoriesView()
    {
        InitializeComponent();
        categoryView = CollectionViewSource.GetDefaultView(categories);
        categoryView.Filter = value => value is Category category && category.Kind == kind;
        CategoriesList.ItemsSource = categoryView;
    }

    public void Initialize(string directory)
    {
        store = new CategoryStore(directory);
        Refresh();
    }

    private void Kind_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (categoryView is not null) Refresh();
    }

    private void DragHandle_MouseDown(object sender, MouseButtonEventArgs e) => dragStart = e.GetPosition(this);

    private void DragHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) { dragStart = null; return; }
        if (dragStart is not { } start || sender is not FrameworkElement { DataContext: Category category } handle || !CreateButton.IsEnabled) return;
        var current = e.GetPosition(this);
        if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        dragStart = null;
        DragDrop.DoDragDrop(handle, new DataObject(CategoryDragFormat, category.Id), DragDropEffects.Move);
    }

    private bool TryGetDraggedCategory(DragEventArgs e, Category target, out Guid id)
    {
        id = e.Data.GetData(CategoryDragFormat) is Guid value ? value : Guid.Empty;
        var movingId = id;
        return CreateButton.IsEnabled && movingId != target.Id && categories.Any(c => c.Id == movingId && c.Kind == target.Kind);
    }

    private void CategoryCard_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (sender is not Border { DataContext: Category target } card || !TryGetDraggedCategory(e, target, out _)) return;
        e.Effects = DragDropEffects.Move;
        card.BorderBrush = (Brush)FindResource("Accent");
        card.BorderThickness = e.GetPosition(card).Y < card.ActualHeight / 2 ? new Thickness(1, 4, 1, 1) : new Thickness(1, 1, 1, 4);
    }

    private static void ClearDropIndicator(Border card)
    {
        card.ClearValue(Border.BorderBrushProperty);
        card.ClearValue(Border.BorderThicknessProperty);
    }

    private void CategoryCard_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border card) ClearDropIndicator(card);
    }

    private void CategoryCard_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (sender is not Border { DataContext: Category target } card) return;
        var after = e.GetPosition(card).Y >= card.ActualHeight / 2;
        ClearDropIndicator(card);
        if (!TryGetDraggedCategory(e, target, out var id)) return;
        try
        {
            MoveCategory(id, target.Id, after);
            e.Effects = DragDropEffects.Move;
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            StatusText.Text = $"Не удалось сохранить порядок категорий. {ex.Message}";
        }
    }

    public void MoveCategory(Guid movingId, Guid targetId, bool after)
    {
        if (store is null || !CreateButton.IsEnabled) return;
        store.Save(CategoryArrangement.Move(store.Load(), movingId, targetId, after));
        Reload();
        if (CreateButton.IsEnabled) StatusText.Text = "Порядок категорий сохранён.";
    }

    public void Refresh()
    {
        kind = KindTabs.SelectedIndex == 1 ? CategoryKind.Income : CategoryKind.Expense;
        var expense = kind == CategoryKind.Expense;
        DescriptionText.Text = expense ? "Категории и магазины для подробного учёта расходов" : "Категории и источники ваших доходов";
        EmptyText.Text = expense ? "Например, «Продукты». Выберите цвет и иконку, затем добавьте магазины." : "Например, «Зарплата». Выберите цвет и иконку, затем добавьте работодателя или заказчика.";
        Reload();
    }

    private void Reload()
    {
        if (store is null) return;
        try
        {
            var loaded = store.Load();
            categories.Clear();
            foreach (var category in loaded) categories.Add(category);
            CreateButton.IsEnabled = true;
            CategoriesList.IsEnabled = true;
            StatusText.Text = "Категории сохраняются на этом компьютере автоматически.";
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            CreateButton.IsEnabled = false;
            CategoriesList.IsEnabled = false;
            StatusText.Text = $"Не удалось прочитать категории. Запись отключена, чтобы сохранить данные.\n{store.FilePath}\n{ex.Message}";
        }
        categoryView.Refresh();
        var count = categories.Count(c => c.Kind == kind);
        CountText.Text = $"Категории · {count}";
        EmptyState.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        Reload();
        if (store is null || !CreateButton.IsEnabled) return;
        OpenEditor(new CategoryDialog(store, categories.ToList(), kind));
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Category selected }) return;
        Reload();
        if (store is null || !CreateButton.IsEnabled) return;
        selected = categories.Single(c => c.Id == selected.Id);
        OpenEditor(new CategoryDialog(store, categories.ToList(), kind, selected));
    }

    private void OpenEditor(CategoryDialog dialog)
    {
        dialog.Owner = Window.GetWindow(this);
        if (dialog.ShowDialog() == true && dialog.SavedCategory is { } saved)
        {
            Reload();
            if (CreateButton.IsEnabled) StatusText.Text = $"Категория «{saved.Name}» сохранена.";
        }
    }

    public void SetMerchantIcon(Guid merchantId, string? pngData)
    {
        if (store is null || !CreateButton.IsEnabled) return;
        var loaded = store.Load();
        if (!loaded.Any(c => c.Merchants.Any(m => m.Id == merchantId))) throw new ArgumentException("Магазин не найден.");
        store.Save(loaded.Select(c => c with { Merchants = c.Merchants.Select(m => m.Id == merchantId ? m with { PngIconBase64 = pngData } : m).ToList() }).ToList());
        Reload();
        if (CreateButton.IsEnabled) StatusText.Text = pngData is null ? "Иконка магазина убрана." : "PNG-иконка магазина сохранена.";
    }

    private void SetMerchantIcon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Merchant merchant }) return;
        try
        {
            var png = MerchantIconLoader.SelectPng(this);
            if (png is not null) SetMerchantIcon(merchant.Id, png);
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException or NotSupportedException or FormatException or System.Text.Json.JsonException)
        {
            StatusText.Text = $"Не удалось загрузить PNG-иконку. {ex.Message}";
        }
    }

    private void ClearMerchantIcon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Merchant merchant }) return;
        try { SetMerchantIcon(merchant.Id, null); }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { StatusText.Text = ex.Message; }
    }

    private void AddMerchant_Click(object sender, RoutedEventArgs e)
    {
        if (store is null || sender is not Button { DataContext: Category selected, Tag: TextBox input }) return;
        try
        {
            var merchant = new Merchant { Name = input.Text.Trim() };
            merchant.Validate();
            var loaded = store.Load();
            var updated = loaded.Select(c => c.Id == selected.Id ? c with { Merchants = [.. c.Merchants, merchant] } : c).ToList();
            store.Save(updated);
            Reload();
            if (CreateButton.IsEnabled) StatusText.Text = $"«{merchant.Name}» добавлен в категорию «{selected.Name}».";
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            StatusText.Text = ex.Message;
        }
    }
}
