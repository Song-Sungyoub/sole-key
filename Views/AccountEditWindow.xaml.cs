using System.Windows;
using System.Windows.Media;
using SoleKey.Models;
using SoleKey.ViewModels;

namespace SoleKey.Views;
public partial class AccountEditWindow : Window
{
    private bool approved;
    public AccountEditWindow(AccountEditViewModel vm)
    {
        InitializeComponent(); DataContext = vm; WindowDialogs.Connect(this, vm);
        vm.CloseRequested += CloseEditor; vm.FocusPasswordRequested += FocusRow;
        Closing += (_, e) => { if (!approved && !vm.CanClose()) e.Cancel = true; };
        Closed += (_, _) => { vm.CloseRequested -= CloseEditor; vm.FocusPasswordRequested -= FocusRow; vm.Deactivate(); };
        Loaded += (_, _) => { if (vm.Draft.Passwords.Count > 0) FocusRow(vm.Draft.Passwords[^1]); };
    }
    private void CloseEditor(bool saved) { approved = true; DialogResult = saved; }
    private void FocusRow(PasswordEntry row) => Dispatcher.BeginInvoke(() =>
    {
        PasswordRows.UpdateLayout();
        if (PasswordRows.ItemContainerGenerator.ContainerFromItem(row) is DependencyObject container)
        {
            var input = FindInput(container); input?.BringIntoView(); input?.FocusSecret();
        }
    });
    private static SecretInput? FindInput(DependencyObject node)
    {
        if (node is SecretInput input) return input;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        { var found = FindInput(VisualTreeHelper.GetChild(node, i)); if (found is not null) return found; }
        return null;
    }
}
