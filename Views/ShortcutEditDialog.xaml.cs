using System.Windows;
using PrismPane_Widgets.Models;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace PrismPane_Widgets.Views;

public partial class ShortcutEditDialog : Window
{
    public ShortcutItem Result { get; private set; }

    public ShortcutEditDialog(ShortcutItem? existing = null)
    {
        InitializeComponent();
        Result = existing ?? new ShortcutItem();

        TxtName.Text = Result.Name;
        TxtPath.Text = Result.TargetPath;
        TxtArguments.Text = Result.Arguments;
        TxtIconPath.Text = Result.CustomIconPath;
    }

    private void BtnBrowsePath_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select application or file",
            Filter = "All files (*.*)|*.*|Executables (*.exe)|*.exe|Shortcuts (*.lnk)|*.lnk"
        };
        if (dlg.ShowDialog() == true)
        {
            TxtPath.Text = dlg.FileName;
            if (string.IsNullOrWhiteSpace(TxtName.Text) || TxtName.Text == "New Shortcut")
                TxtName.Text = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
        }
    }

    private void BtnBrowseIcon_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select icon",
            Filter = "Image/Icon files (*.ico;*.exe;*.png;*.jpg;*.bmp)|*.ico;*.exe;*.png;*.jpg;*.bmp|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
            TxtIconPath.Text = dlg.FileName;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtPath.Text))
        {
            System.Windows.MessageBox.Show("Path is required.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result.Name = TxtName.Text.Trim();
        Result.TargetPath = TxtPath.Text.Trim();
        Result.Arguments = TxtArguments.Text.Trim();
        Result.CustomIconPath = TxtIconPath.Text.Trim();

        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();
}
