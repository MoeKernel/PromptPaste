using System.Windows;

namespace PromptPaste.Views.Dialogs;

public partial class JsonFormatDialog : Window
{
    private const string ExampleJson = """
        [
          {
            "title": "AI提示词示例",
            "content": "你是一名专业的{{领域}}专家，请帮我{{任务}}。",
            "categoryPaths": ["AI提示词/写作", "常用"],
            "tags": ["prompt", "写作"],
            "usageCount": 0,
            "lastUsed": null,
            "createdAt": "2026-06-11T00:00:00+08:00"
          },
          {
            "title": "商务邮件模板",
            "content": "尊敬的{{收件人}}：\n\n感谢您的来信。关于{{问题}}，我们会尽快处理。",
            "categoryPaths": ["商务话术/邮件"],
            "tags": ["email", "商务"],
            "usageCount": 0,
            "lastUsed": null,
            "createdAt": "2026-06-11T00:00:00+08:00"
          }
        ]
        """;

    public JsonFormatDialog()
    {
        InitializeComponent();
        ExampleBox.Text = ExampleJson;
    }

    private void CopyExample_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(ExampleJson);
        CopiedHint.Visibility = Visibility.Visible;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
