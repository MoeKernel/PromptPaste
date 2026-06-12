using System.Windows;
using System.Windows.Controls;

namespace PromptPaste.Views.Dialogs;

public partial class VariableDialog : Window
{
    private readonly Dictionary<string, TextBox> _fields = new();
    public Dictionary<string, string>? Values { get; private set; }

    public VariableDialog(List<string> variables)
    {
        InitializeComponent();

        foreach (var v in variables)
        {
            var label = new TextBlock { Text = v, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
            var box = new TextBox { FontSize = 14, Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 10) };
            FieldsPanel.Children.Add(label);
            FieldsPanel.Children.Add(box);
            _fields[v] = box;
        }

        Loaded += (_, _) => _fields.Values.FirstOrDefault()?.Focus();
    }

    private void Submit_Click(object sender, RoutedEventArgs e)
    {
        Values = _fields.ToDictionary(kv => kv.Key, kv => kv.Value.Text);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
