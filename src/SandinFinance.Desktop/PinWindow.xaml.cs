using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SandinFinance.Core;

namespace SandinFinance.Desktop;

public enum PinMode { Login, Setup, Change }

public partial class PinWindow : Window
{
    private readonly ProfileStore store;
    private readonly PinMode mode;
    private readonly DispatcherTimer timer;
    private string buffer = "";
    private string newPin = "";
    private string currentPin = "";
    private int stage;

    public PinWindow(ProfileStore store, PinMode mode)
    {
        this.store = store;
        this.mode = mode;
        InitializeComponent();
        ProfileNameText.Text = store.Load().Name;
        Title = mode == PinMode.Change ? "SandinFinance — смена PIN" : mode == PinMode.Setup ? "SandinFinance — создание PIN" : "SandinFinance — вход";
        RefreshPrompt();
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => RefreshLock();
        timer.Start();
        Closed += (_, _) => { timer.Stop(); buffer = newPin = currentPin = ""; };
        RefreshLock();
    }

    private void RefreshPrompt() => PromptText.Text = mode == PinMode.Login ? "Введите PIN" : mode == PinMode.Change && stage == 0
        ? "Введите текущий PIN" : stage == (mode == PinMode.Change ? 2 : 1) ? "Повторите новый PIN" : "Создайте PIN из 4 цифр";

    private void RefreshLock()
    {
        try
        {
            var locked = store.Load().LockedUntil;
            var remaining = locked is { } date ? (date - DateTimeOffset.UtcNow).TotalSeconds : 0;
            Keypad.IsEnabled = remaining <= 0;
            if (remaining > 0) ErrorText.Text = $"Повторите ввод через {Math.Ceiling(remaining)} сек.";
            else if (ErrorText.Text.StartsWith("Повторите ввод через")) ErrorText.Text = "";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { Keypad.IsEnabled = false; ErrorText.Text = ex.Message; }
    }

    private void Digit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string digit }) AddDigit(digit);
    }
    private void AddDigit(string digit)
    {
        if (!Keypad.IsEnabled || buffer.Length >= 4) return;
        ErrorText.Text = "";
        buffer += digit;
        RefreshDots();
        if (buffer.Length == 4) Submit();
    }

    private void Submit()
    {
        var entered = buffer;
        buffer = "";
        try
        {
            if (mode == PinMode.Login || mode == PinMode.Change && stage == 0)
            {
                if (!store.VerifyPin(entered)) { ErrorText.Text = "Неверный PIN. Попробуйте ещё раз."; return; }
                if (mode == PinMode.Login) { DialogResult = true; return; }
                currentPin = entered;
                stage = 1;
                RefreshPrompt();
                return;
            }
            var firstStage = mode == PinMode.Change ? 1 : 0;
            if (stage == firstStage)
            {
                newPin = entered;
                stage++;
                RefreshPrompt();
                return;
            }
            if (entered != newPin)
            {
                ErrorText.Text = "PIN не совпали. Задайте новый PIN ещё раз.";
                stage = firstStage;
                newPin = "";
                RefreshPrompt();
                return;
            }
            if (mode == PinMode.Change) store.ChangePin(currentPin, newPin);
            else store.SetInitialPin(newPin);
            DialogResult = true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { ErrorText.Text = ex.Message; }
        finally { RefreshDots(); RefreshLock(); }
    }

    private void RefreshDots()
    {
        var dots = new[] { Dot1, Dot2, Dot3, Dot4 };
        for (var i = 0; i < dots.Length; i++) dots[i].Fill = i < buffer.Length ? (Brush)FindResource("Accent") : Brushes.Transparent;
    }
    private void Clear_Click(object sender, RoutedEventArgs e) { buffer = ""; RefreshDots(); }
    private void Backspace_Click(object sender, RoutedEventArgs e) { if (buffer.Length > 0) buffer = buffer[..^1]; RefreshDots(); }
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key >= Key.D0 && e.Key <= Key.D9 && Keyboard.Modifiers == ModifierKeys.None) { AddDigit(((int)e.Key - (int)Key.D0).ToString()); e.Handled = true; }
        else if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9) { AddDigit(((int)e.Key - (int)Key.NumPad0).ToString()); e.Handled = true; }
        else if (e.Key == Key.Back) { Backspace_Click(this, e); e.Handled = true; }
        else if (e.Key == Key.Escape) Close();
    }
}
