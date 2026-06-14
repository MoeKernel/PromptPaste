using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PromptPaste.Models;
using PromptPaste.Services;

namespace PromptPaste.Views;

public partial class QuickPasteWindow : Window
{
    private readonly List<ClipboardItem> _items;
    private readonly Action<ClipboardItem, QuickPasteWindow> _commit;
    private bool _isCommitting;
    private bool _isClosing;
    private bool _canCloseOnDeactivate;

    public ObservableCollection<QuickPasteCandidate> Candidates { get; } = new();

    public QuickPasteWindow(IEnumerable<ClipboardItem> items, Point location, Action<ClipboardItem, QuickPasteWindow> commit)
    {
        InitializeComponent();
        _items = items.Select(i => i.Clone()).ToList();
        _commit = commit;
        SetSafeLocation(location);
        CandidateList.ItemsSource = Candidates;
        RefreshCandidates();
        Loaded += (_, _) =>
        {
            UpdateRoundedClip();
            SearchBox.Focus();
            SearchBox.SelectAll();
            Dispatcher.BeginInvoke(() => _canCloseOnDeactivate = true, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        };
        SizeChanged += (_, _) => UpdateRoundedClip();
        Closing += (_, _) => _isClosing = true;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshCandidates();

    private void SetSafeLocation(Point anchor)
    {
        var location = QuickPasteWindowPlacement.CalculatePopupLocation(
            anchor,
            new Size(Width, Height),
            new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight));

        Left = location.X;
        Top = location.Y;
    }

    private void UpdateRoundedClip()
    {
        WindowChrome.Clip = new RectangleGeometry(
            new Rect(0, 0, ActualWidth, ActualHeight),
            16,
            16);
    }

    private void RefreshCandidates()
    {
        var selectedId = (CandidateList.SelectedItem as QuickPasteCandidate)?.Item.Id;
        var results = QuickPasteSearchService.Search(_items, SearchBox.Text);
        Candidates.Clear();
        foreach (var item in results)
            Candidates.Add(new QuickPasteCandidate(item));

        CandidateList.SelectedItem = Candidates.FirstOrDefault(c => c.Item.Id == selectedId) ?? Candidates.FirstOrDefault();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseSafely();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            CommitSelected();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            MoveSelection(1);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            MoveSelection(-1);
            e.Handled = true;
        }
    }

    private void MoveSelection(int delta)
    {
        if (Candidates.Count == 0) return;
        var index = CandidateList.SelectedIndex < 0 ? 0 : CandidateList.SelectedIndex + delta;
        index = Math.Clamp(index, 0, Candidates.Count - 1);
        CandidateList.SelectedIndex = index;
        CandidateList.ScrollIntoView(CandidateList.SelectedItem);
    }

    private void CandidateList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => CommitSelected();

    private void CommitSelected()
    {
        if (CandidateList.SelectedItem is not QuickPasteCandidate candidate) return;
        _isCommitting = true;
        _commit(candidate.Item, this);
        CloseSafely();
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (_canCloseOnDeactivate && !_isCommitting) CloseSafely();
    }

    private void CloseSafely()
    {
        if (_isClosing) return;
        _isClosing = true;
        Close();
    }
}

public sealed class QuickPasteCandidate
{
    public ClipboardItem Item { get; }
    public string Title => Item.Title;
    public string Preview => TextProcessor.Truncate(Item.Content.Replace("\r", " ").Replace("\n", " "), 90);
    public string FullContent => Item.Content;
    public IReadOnlyList<string> Tags => Item.Tags;

    public QuickPasteCandidate(ClipboardItem item) => Item = item;
}


public static class QuickPasteWindowPlacement
{
    private const double HorizontalOffset = 2;
    private const double VerticalOffset = 2;

    public static Point CalculatePopupLocation(Point anchor, Size popupSize, Rect screenBounds)
    {
        var left = anchor.X + HorizontalOffset;
        var top = anchor.Y + VerticalOffset;

        if (left + popupSize.Width > screenBounds.Right)
            left = screenBounds.Right - popupSize.Width;

        if (top + popupSize.Height > screenBounds.Bottom)
            top = anchor.Y - popupSize.Height - VerticalOffset;

        var maxLeft = Math.Max(screenBounds.Left, screenBounds.Right - popupSize.Width);
        var maxTop = Math.Max(screenBounds.Top, screenBounds.Bottom - popupSize.Height);

        left = Math.Clamp(left, screenBounds.Left, maxLeft);
        top = Math.Clamp(top, screenBounds.Top, maxTop);

        return new Point(left, top);
    }
}
