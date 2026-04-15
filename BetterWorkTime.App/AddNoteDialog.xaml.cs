using System.Windows;

namespace BetterWorkTime.App;

public partial class AddNoteDialog : Window
{
    public string? Note { get; private set; }

    public AddNoteDialog(string? existingNote)
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            NoteBox.Text = existingNote ?? string.Empty;
            NoteBox.SelectAll();
            NoteBox.Focus();
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Note = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
