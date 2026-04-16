using System.Windows;
using System.Windows.Input;

namespace BetterWorkTime.App;

public partial class RenameDialog : Window
{
    public string ResultName { get; private set; } = "";

    public RenameDialog(string currentName)
    {
        InitializeComponent();
        NameBox.Text = currentName;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Commit();

    private void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Commit();
    }

    private void Commit()
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;
        ResultName = name;
        DialogResult = true;
    }
}
