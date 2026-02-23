using System;
using Avalonia.Controls;
using Avalonia.Input;
using Krypton_Desktop.ViewModels;

namespace Krypton_Desktop.Views;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape)
        {
            if (DataContext is LoginViewModel vm)
            {
                vm.CancelCommand.Execute().Subscribe();
            }
            e.Handled = true;
        }
    }
}
