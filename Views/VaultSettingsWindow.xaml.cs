using System.Windows;
using SoleKey.ViewModels;

namespace SoleKey.Views;
public partial class VaultSettingsWindow : Window
{
    public VaultSettingsWindow(VaultSettingsViewModel vm)
    {
        InitializeComponent(); DataContext = vm; WindowDialogs.Connect(this, vm);
        Closing += (_, e) => { if (vm.Active && vm.Busy) e.Cancel = true; };
        Closed += (_, _) => vm.Deactivate();
    }
}
