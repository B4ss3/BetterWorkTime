using System.Windows;

namespace BetterWorkTime.App;

public partial class HydrationPromptWindow : Window
{
    public HydrationPromptWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Activate();
    }

    private void GotIt_Click(object sender, RoutedEventArgs e) => Close();
}
