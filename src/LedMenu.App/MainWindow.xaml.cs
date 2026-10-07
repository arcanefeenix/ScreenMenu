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

    /// <summary>
    /// A number box may show an invalid entry while it is being edited, but once focus leaves it must
    /// show the value that is actually stored; it never keeps displaying something the configuration does not hold.
    /// </summary>
    private void NumericBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box) return;
        // Run after the binding has tried to commit the text.
        Dispatcher.BeginInvoke(() =>
        {
            var binding = box.GetBindingExpression(TextBox.TextProperty);
            if (binding != null && (binding.HasError || Validation.GetHasError(box)))
            {
                Validation.ClearInvalid(binding);
                binding.UpdateTarget();
            }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void Card_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ScreenItemViewModel item }) item.Select();
    }
}
