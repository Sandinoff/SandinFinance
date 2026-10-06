using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SandinFinance.Core;

namespace SandinFinance.Desktop;

public partial class MainWindow : Window
{
    private readonly AccountStore store;
    private readonly ProfileStore? profileStore;
    private readonly ObservableCollection<Account> accounts = [];
    private readonly DispatcherTimer interestTimer;
    private bool showingDialog;
    private DateOnly refreshedDate;
    private DateTime refreshedAt;
    private readonly ICollectionView accountView;
    private Point? dragStart;
    private const string AccountDragFormat = "SandinFinance.Account";

    public MainWindow(string directory, ProfileStore? profileStore = null)
    {
        InitializeComponent();
        this.profileStore = profileStore;
        ProfileNameText.Text = profileStore?.Load().Name ?? "Sandin";
        ProfileButton.IsEnabled = profileStore is not null;
        store = new AccountStore(directory);
        accountView = CollectionViewSource.GetDefaultView(accounts);
        accountView.Filter = item => item is Account account && (!account.IsHidden || ShowHiddenInput.IsChecked == true);
        AccountsList.ItemsSource = accountView;
        CategoriesPage.Initialize(directory);
        RefreshFromDisk();
        HomePage.Initialize(directory);
        HomePage.OperationSaved += (_, _) => RefreshFromDisk();
        interestTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        interestTimer.Tick += (_, _) => RefreshForNewDay();
        interestTimer.Start();
        Activated += (_, _) => RefreshForNewDay();
        Closed += (_, _) => interestTimer.Stop();
    }

    private void Profile_Click(object sender, RoutedEventArgs e)
    {
        if (profileStore is null || showingDialog) return;
        var menu = ProfileButton.ContextMenu;
        menu.PlacementTarget = ProfileButton;
        menu.IsOpen = !menu.IsOpen;
    }

    private static void AnimateLogo(UIElement target, DependencyProperty property, double value)
    {
        target.BeginAnimation(property, new DoubleAnimation(value, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void MoveLogoSpotlight(Point pointer)
    {
        Canvas.SetLeft(LogoSpotlight, Math.Clamp(pointer.X, 0, LogoHeader.ActualWidth) - LogoSpotlight.Width / 2);
        Canvas.SetTop(LogoSpotlight, Math.Clamp(pointer.Y, 0, LogoHeader.ActualHeight) - LogoSpotlight.Height / 2);
    }

    private void Logo_MouseEnter(object sender, MouseEventArgs e)
    {
        MoveLogoSpotlight(e.GetPosition(LogoHeader));
        AnimateLogo(LogoSpotlight, UIElement.OpacityProperty, 0.95);
    }

    private void Logo_MouseMove(object sender, MouseEventArgs e) => MoveLogoSpotlight(e.GetPosition(LogoHeader));

    private void Logo_MouseLeave(object sender, MouseEventArgs e) => AnimateLogo(LogoSpotlight, UIElement.OpacityProperty, 0);

    private void ProfileSettings_Click(object sender, RoutedEventArgs e)
    {
        if (profileStore is null || showingDialog) return;
        ProfileButton.ContextMenu.IsOpen = false;
        showingDialog = true;
        try { new ProfileSettingsWindow(profileStore) { Owner = this }.ShowDialog(); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException) { StatusText.Text = ex.Message; }
        finally { showingDialog = false; }
    }

    private void AccountsNav_Click(object sender, RoutedEventArgs e)
    {
        RefreshFromDisk();
        HomePage.Visibility = Visibility.Collapsed;
        CategoriesPage.Visibility = Visibility.Collapsed;
        AccountsScroll.Visibility = Visibility.Visible;
        SetNavigation(AccountsNav, "счета");
    }

    private void HomeNav_Click(object sender, RoutedEventArgs e)
    {
        AccountsScroll.Visibility = Visibility.Collapsed;
        CategoriesPage.Visibility = Visibility.Collapsed;
        HomePage.Visibility = Visibility.Visible;
        HomePage.Refresh();
        SetNavigation(HomeNav, "главная");
    }

    private void CategoriesNav_Click(object sender, RoutedEventArgs e)
    {
        HomePage.Visibility = Visibility.Collapsed;
        AccountsScroll.Visibility = Visibility.Collapsed;
        CategoriesPage.Visibility = Visibility.Visible;
        CategoriesPage.Refresh();
        SetNavigation(CategoriesNav, "категории");
    }

    private void SetNavigation(Button selected, string caption)
    {
        foreach (var button in new[] { HomeNav, AccountsNav, CategoriesNav })
            button.Background = button == selected ? (System.Windows.Media.Brush)FindResource("Selected") : System.Windows.Media.Brushes.Transparent;
        Title = $"SandinFinance — {caption}";
    }

    private void RefreshForNewDay()
    {
        var postingBoundary = DateTime.Today.AddHours(1);
        if (!showingDialog && (refreshedDate != DateOnly.FromDateTime(DateTime.Today) || (refreshedAt < postingBoundary && DateTime.Now >= postingBoundary))) RefreshFromDisk();
    }

    private void RefreshFromDisk()
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var loaded = store.LoadAndAccrue(today);
            loaded = loaded.Select(a =>
            {
                if (!a.IsCredit || a.MonthlySpendingLimit == 0) return a;
                var spent = BudgetStatistics.Calculate([a], [], CategoryKind.Expense, new(new(today.Year, today.Month, 1), today)).Total;
                return a with { DisplayBudgetRemaining = a.MonthlySpendingLimit - spent };
            }).ToList();
            accounts.Clear();
            foreach (var account in loaded) accounts.Add(account);
            refreshedDate = today;
            refreshedAt = DateTime.Now;
            AddButton.IsEnabled = true;
            AccountsList.IsEnabled = true;
            StatusText.Text = "Счета сохраняются на этом компьютере автоматически.";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            AddButton.IsEnabled = false;
            AccountsList.IsEnabled = false;
            StatusText.Text = $"Не удалось прочитать счета. Запись отключена, чтобы сохранить ваши данные.\n{store.FilePath}\n{ex.Message}";
        }
        RefreshTotals();
        if (HomePage.Visibility == Visibility.Visible) HomePage.Refresh();
    }

    private void Transfer_Click(object sender, RoutedEventArgs e)
    {
        RefreshForNewDay();
        if (!TransferButton.IsEnabled) return;
        showingDialog = true;
        try
        {
            var dialog = new TransferDialog(store) { Owner = this };
            if (dialog.ShowDialog() == true) RefreshFromDisk();
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { StatusText.Text = ex.Message; }
        finally { showingDialog = false; }
    }

    private void AddAccount_Click(object sender, RoutedEventArgs e)
    {
        RefreshForNewDay();
        if (!AddButton.IsEnabled) return;
        var dialog = new AccountDialog(store, accounts.ToList()) { Owner = this };
        OpenAccountDialog(dialog);
    }

    private void EditAccount_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Account selected }) return;
        RefreshForNewDay();
        if (!AddButton.IsEnabled) return;
        selected = accounts.Single(a => a.Id == selected.Id);
        var dialog = new AccountDialog(store, accounts.ToList(), selected) { Owner = this };
        OpenAccountDialog(dialog);
    }

    private void OpenAccountDialog(AccountDialog dialog)
    {
        showingDialog = true;
        try
        {
            if (dialog.ShowDialog() == true && dialog.CreatedAccount is { } updated)
            {
                RefreshFromDisk();
                if (AddButton.IsEnabled) StatusText.Text = $"Счёт «{updated.Name}» сохранён.";
            }
        }
        finally { showingDialog = false; }
    }

    private void DeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Account selected } || !AddButton.IsEnabled) return;
        showingDialog = true;
        try
        {
            var current = store.Load().SingleOrDefault(a => a.Id == selected.Id)
                ?? throw new ArgumentException("Счёт уже удалён.");
            var dialog = new DeleteAccountDialog(current) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            store.Delete(current.Id);
            RefreshFromDisk();
            if (AddButton.IsEnabled) StatusText.Text = $"Счёт «{current.Name}» удалён.";
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            StatusText.Text = $"Не удалось удалить счёт. {ex.Message}";
        }
        finally { showingDialog = false; }
    }

    private void AddPurchase_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Account selected }) return;
        RefreshForNewDay();
        if (!AddButton.IsEnabled) return;
        selected = accounts.Single(a => a.Id == selected.Id);
        var dialog = new PurchaseDialog(store, accounts.ToList(), selected) { Owner = this };
        showingDialog = true;
        try { if (dialog.ShowDialog() == true) RefreshFromDisk(); }
        finally { showingDialog = false; }
    }

    private void ShowHidden_Changed(object sender, RoutedEventArgs e)
    {
        if (accountView is null) return;
        accountView.Refresh();
        RefreshTotals();
    }

    private void ToggleHidden_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Account selected }) return;
        RefreshForNewDay();
        if (!AddButton.IsEnabled) return;
        var updated = accounts.Select(a => a.Id == selected.Id ? a with { IsHidden = !a.IsHidden } : a).ToList();
        SaveArrangement(updated);
    }

    private void SaveArrangement(List<Account> updated)
    {
        try
        {
            store.Save(updated.Select(a => SavingsInterest.Accrue(a, DateOnly.FromDateTime(DateTime.Today))).ToList());
            RefreshFromDisk();
            if (AddButton.IsEnabled) StatusText.Text = "Порядок и видимость счетов сохранены. Скрытые счета учитываются в общей сумме.";
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Не удалось сохранить настройки списка. {ex.Message}";
        }
    }

    private void DragHandle_MouseDown(object sender, MouseButtonEventArgs e) => dragStart = e.GetPosition(this);

    private void DragHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) { dragStart = null; return; }
        if (dragStart is not { } start || sender is not FrameworkElement { DataContext: Account account } handle || !AddButton.IsEnabled) return;
        var current = e.GetPosition(this);
        if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        dragStart = null;
        showingDialog = true;
        try { DragDrop.DoDragDrop(handle, new DataObject(AccountDragFormat, account.Id), DragDropEffects.Move); }
        finally { showingDialog = false; RefreshForNewDay(); }
    }

    private void AccountCard_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (sender is not Border { DataContext: Account target } card || !TryGetDraggedAccount(e, out var id) || id == target.Id || !AddButton.IsEnabled) return;
        e.Effects = DragDropEffects.Move;
        var after = e.GetPosition(card).Y >= card.ActualHeight / 2;
        card.BorderBrush = (System.Windows.Media.Brush)FindResource("Accent");
        card.BorderThickness = new Thickness(1, after ? 1 : 3, 1, after ? 3 : 1);
        var point = e.GetPosition(AccountsScroll);
        if (point.Y < 40) AccountsScroll.LineUp();
        else if (point.Y > AccountsScroll.ActualHeight - 40) AccountsScroll.LineDown();
    }

    private void AccountCard_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border card) ClearDropIndicator(card);
    }

    private void AccountCard_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (sender is not Border { DataContext: Account target } card) return;
        var after = e.GetPosition(card).Y >= card.ActualHeight / 2;
        ClearDropIndicator(card);
        if (!TryGetDraggedAccount(e, out var id) || !AddButton.IsEnabled || id == target.Id) return;
        SaveArrangement(AccountArrangement.Move(accounts.ToList(), id, target.Id, after));
        e.Effects = DragDropEffects.Move;
    }

    private bool TryGetDraggedAccount(DragEventArgs e, out Guid id)
    {
        id = e.Data.GetData(AccountDragFormat) is Guid value ? value : Guid.Empty;
        var draggedId = id;
        return draggedId != Guid.Empty && accounts.Any(a => a.Id == draggedId);
    }

    private static void ClearDropIndicator(Border card)
    {
        card.ClearValue(Border.BorderBrushProperty);
        card.ClearValue(Border.BorderThicknessProperty);
    }

    private void RefreshTotals()
    {
        var totals = AccountTotals.From(accounts);
        OwnMoneyText.Text = Money.Format(totals.OwnMoney);
        DebtText.Text = Money.Format(totals.Debt);
        NetText.Text = Money.Format(totals.Net);
        var hidden = accounts.Count(a => a.IsHidden);
        TransferButton.IsEnabled = AddButton.IsEnabled && accounts.Count >= 2;
        CountText.Text = $"Счета · {accounts.Count}" + (hidden > 0 ? $" (скрыто: {hidden})" : "");
        EmptyState.Visibility = accounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HiddenEmptyText.Visibility = accounts.Count > 0 && accountView.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    }
}
