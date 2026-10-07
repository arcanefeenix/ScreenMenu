using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LedMenu.App.ViewModels;

namespace LedMenu.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>Enter commits a typed number or name immediately instead of waiting for focus to leave.</summary>
    private void CommitOnEnter(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox box) return;
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        e.Handled = true;
    }

    private void Card_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ScreenItemViewModel item }) item.Select();
    }
}
