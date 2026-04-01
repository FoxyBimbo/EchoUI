using System.Collections.ObjectModel;
using System.Windows;
using PrismPane_Widgets.Models;

namespace PrismPane_Widgets.Views;

public partial class NotificationPanel : Window
{
    public ObservableCollection<AppNotification> Notifications { get; }

    public NotificationPanel(ObservableCollection<AppNotification> notifications)
    {
        InitializeComponent();
        Notifications = notifications;
        LstNotifications.ItemsSource = Notifications;
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        Notifications.Clear();
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        Close();
    }
}
