using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using RecurFolder.Models;
using RecurFolder.Services;

namespace RecurFolder;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _searchTimer;
    private CancellationTokenSource? _scanCts;
    private int _scanGeneration;
    private List<FileEntry> _all = [];
    private ICollectionView? _view;
    private bool _scanning;
    private bool _exporting;
    private bool _hasScanned;
    private TimeSpan _lastElapsed;
    private int _lastSkipped;
    private int _lastLinks;
    private bool _lastCancelled;
    private int _fileCount;
    private int _folderCount;
    private readonly ObservableCollection<FolderNode> _roots = [];
    private readonly Dictionary<FolderNode, Task> _childLoads = [];
    private bool _suppressTreeSelection;
    private string? _scannedRoot;
    private int _treeRevealGeneration;
    private string? _lastTreeScanPath;
    private long _lastTreeScanTick;

    public MainWindow()
    {
        InitializeComponent();
        ThemeManager.ThemeChanged += OnThemeChanged;
        UpdateThemeChecks();
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            ApplyFilter();
        };
        var computer = new FolderNode("This PC", "", FolderKind.Computer);
        computer.Expanded += OnNodeExpanded;
        computer.Selected += OnNodeSelected;
        computer.Children.Add(CreatePlaceholder("Reading drives…"));
        _roots.Add(computer);
        FolderTree.ItemsSource = _roots;

        Loaded += async (_, _) =>
        {
            computer.IsExpanded = true;
            FolderPathBox.Focus();
            if (Environment.GetCommandLineArgs() is not { Length: > 1 } args)
                return;

            var path = args[1].Trim().Trim('"');
            if (File.Exists(path))
                path = Path.GetDirectoryName(path) ?? path;
            if (!Directory.Exists(path))
                return;

            FolderPathBox.Text = path;
            await StartScanAsync();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(OnWindowMessage);
        ThemeManager.ApplyTitleBar(new WindowInteropHelper(this).Handle);
    }

    protected override void OnClosed(EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
        _scanCts?.Cancel();
        base.OnClosed(e);
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int settingChange = 0x001A;
        const int setHighContrast = 0x0043;
        if (msg != settingChange || ThemeManager.Selected != AppTheme.Default)
            return IntPtr.Zero;

        var setting = lParam == IntPtr.Zero ? null : Marshal.PtrToStringAuto(lParam);
        if (wParam != setHighContrast && setting is not ("ImmersiveColorSet" or "WindowsThemeElement"))
            return IntPtr.Zero;

        Dispatcher.BeginInvoke(() =>
        {
            if (ThemeManager.Selected == AppTheme.Default)
                ThemeManager.Apply(AppTheme.Default, save: false);
        });
        return IntPtr.Zero;
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnThemeChanged(sender, e)));
            return;
        }

        UpdateThemeChecks();
        ThemeManager.ApplyTitleBar(new WindowInteropHelper(this).Handle);
    }

    private void UpdateThemeChecks()
    {
        foreach (var item in ThemeMenu.Items)
        {
            if (item is MenuItem { Tag: string tag } menuItem)
            {
                menuItem.IsChecked = string.Equals(
                    tag,
                    ThemeManager.Selected.ToString(),
                    StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private void ThemeMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag })
            return;
        if (!Enum.TryParse(tag, ignoreCase: true, out AppTheme theme))
            return;

        ThemeManager.Apply(theme);
    }

    private void ExitMenu_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void HelpMenu_Click(object sender, RoutedEventArgs e)
    {
        var help = new HelpWindow { Owner = this };
        help.ShowDialog();
    }

    private void AboutMenu_Click(object sender, RoutedEventArgs e)
    {
        var about = new AboutWindow { Owner = this };
        about.ShowDialog();
    }

    private void ItemsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateEditMenu();
    }

    private void UpdateEditMenu()
    {
        var enabled = ItemsGrid.SelectedItems.Count > 0;
        EditRevealItem.IsEnabled = enabled;
        EditCopyNameItem.IsEnabled = enabled;
        EditCopyPathItem.IsEnabled = enabled;
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select a folder to list",
            Multiselect = false
        };

        var current = FolderPathBox.Text.Trim().Trim('"');
        if (Directory.Exists(current))
        {
            dialog.InitialDirectory = current;
        }
        else
        {
            var parent = Path.GetDirectoryName(current);
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                dialog.InitialDirectory = parent;
        }

        if (dialog.ShowDialog(this) != true || string.IsNullOrWhiteSpace(dialog.FolderName))
            return;

        FolderPathBox.Text = dialog.FolderName;
        await StartScanAsync();
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (_scanning)
        {
            _scanCts?.Cancel();
            ScanButton.IsEnabled = false;
            StatusText.Text = "Cancelling…";
            return;
        }

        await StartScanAsync();
    }

    private async void FolderPathBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        await StartScanAsync();
    }

    private async Task StartScanAsync()
    {
        string root;
        try
        {
            var raw = FolderPathBox.Text.Trim().Trim('"');
            if (raw.Length == 0)
            {
                StatusText.Text = "Type or choose a folder first.";
                return;
            }

            root = Path.GetFullPath(raw);
        }
        catch (Exception)
        {
            StatusText.Text = "That folder path is not valid.";
            return;
        }

        if (File.Exists(root))
        {
            StatusText.Text = "That path is a file. Choose a folder.";
            return;
        }

        if (!Directory.Exists(root))
        {
            StatusText.Text = "That folder does not exist.";
            return;
        }

        FolderPathBox.Text = root;
        _scannedRoot = root;
        var folderTitle = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        Title = string.IsNullOrEmpty(folderTitle) ? "RecurFolder" : $"RecurFolder - {folderTitle}";
        var revealGeneration = ++_treeRevealGeneration;
        _ = RevealPathInTreeAsync(root, revealGeneration);

        var previous = _scanCts;
        var cts = new CancellationTokenSource();
        _scanCts = cts;
        previous?.Cancel();

        var generation = ++_scanGeneration;
        var token = cts.Token;
        SetScanning(true);
        _all = [];
        _view = null;
        ItemsGrid.ItemsSource = null;
        var startedCount = ItemCount(0);
        ScanCountText.Text = startedCount;
        StatusText.Text = startedCount;

        var progress = new Progress<ScanProgress>(update =>
        {
            if (generation != _scanGeneration)
                return;

            var count = ItemCount(update.Found);
            ScanCountText.Text = count;
            StatusText.Text = count;
        });

        var started = Stopwatch.StartNew();
        ScanResult result;
        try
        {
            result = await Task.Run(() => FolderScanner.Scan(root, progress, token));
        }
        catch (Exception ex)
        {
            if (generation != _scanGeneration)
                return;

            SetScanning(false);
            StatusText.Text = "Scan failed.";
            EmptyText.Text = "The scan could not finish.";
            EmptyText.Visibility = Visibility.Visible;
            MessageBox.Show(this, ex.Message, "Could not scan that folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (generation != _scanGeneration)
            return;

        started.Stop();
        _hasScanned = true;
        _lastElapsed = started.Elapsed;
        _lastSkipped = result.Skipped;
        _lastLinks = result.LinksNotFollowed;
        _lastCancelled = result.Cancelled;
        _fileCount = result.FileCount;
        _folderCount = result.FolderCount;
        _all = result.Entries;
        _view = CollectionViewSource.GetDefaultView(_all);
        _view.Filter = FilterEntry;
        ItemsGrid.ItemsSource = _all;
        _view.Refresh();
        SetScanning(false);
        UpdateStatus();
    }

    private void FolderPathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        FolderHint.Visibility = string.IsNullOrEmpty(FolderPathBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateClearReset();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateClearReset();
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        SearchBox.Focus();
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _scanCts?.Cancel();
        _scanGeneration++;
        _treeRevealGeneration++;
        SetScanning(false);

        _scannedRoot = null;
        _hasScanned = false;
        _all = [];
        _view = null;
        _fileCount = 0;
        _folderCount = 0;
        _lastElapsed = TimeSpan.Zero;
        _lastSkipped = 0;
        _lastLinks = 0;
        _lastCancelled = false;
        ItemsGrid.ItemsSource = null;
        ClearTreeSelection();

        SearchBox.Clear();
        FolderPathBox.Clear();
        Title = "RecurFolder";
        EmptyText.Text = "Choose a folder on the left, or type a path and press Scan.";
        EmptyText.Visibility = Visibility.Visible;
        StatusText.Text = "Choose a folder on the left, type a path, or drop a folder here.";
        SetExportEnabled(false);
        UpdateEditMenu();
        FolderPathBox.Focus();
    }

    private void ClearTreeSelection()
    {
        foreach (var root in _roots)
            ClearTreeSelection(root);
    }

    private static void ClearTreeSelection(FolderNode node)
    {
        node.IsSelected = false;
        foreach (var child in node.Children)
            ClearTreeSelection(child);
    }

    private void UpdateClearReset()
    {
        ClearButton.IsEnabled = SearchBox.Text.Length > 0;
        ResetButton.IsEnabled = _scanning
            || _all.Count > 0
            || FolderPathBox.Text.Length > 0
            || SearchBox.Text.Length > 0;
    }

    private void ApplyFilter()
    {
        if (!_hasScanned || _view is null)
            return;

        _view.Refresh();
        UpdateStatus();
    }

    private bool FilterEntry(object item)
    {
        if (item is not FileEntry entry)
            return false;

        var query = SearchBox.Text.Trim();
        if (query.Length == 0)
            return true;

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return terms.All(term =>
            entry.FolderName.Contains(term, StringComparison.OrdinalIgnoreCase)
            || entry.FileName.Contains(term, StringComparison.OrdinalIgnoreCase)
            || entry.FilePath.Contains(term, StringComparison.OrdinalIgnoreCase)
            || entry.Kind.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_exporting || ExportButton.ContextMenu is null)
            return;

        var menu = ExportButton.ContextMenu;
        menu.PlacementTarget = ExportButton;
        Dispatcher.BeginInvoke(() => menu.IsOpen = true);
    }

    private async void ExportMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string format })
            return;

        await ExportAsync(format);
    }

    private async Task ExportAsync(string format)
    {
        if (_exporting || _view is null)
            return;

        var rows = new List<FileEntry>();
        foreach (FileEntry entry in _view)
            rows.Add(entry);

        if (rows.Count == 0)
        {
            StatusText.Text = "Nothing to export.";
            return;
        }

        var (filter, extension, title) = format switch
        {
            "csv" => ("CSV (*.csv)|*.csv", ".csv", "Export to CSV"),
            "pdf" => ("PDF (*.pdf)|*.pdf", ".pdf", "Export to PDF"),
            _ => ("Excel workbook (*.xlsx)|*.xlsx", ".xlsx", "Export to Excel")
        };

        var folderName = Path.GetFileName(FolderPathBox.Text.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(folderName))
            folderName = "Drive";
        foreach (var invalid in Path.GetInvalidFileNameChars())
            folderName = folderName.Replace(invalid, '_');

        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = $"{folderName}_{DateTime.Now:yyyyMMdd_HHmm}{extension}",
            AddExtension = true,
            DefaultExt = extension,
            OverwritePrompt = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog(this) != true)
            return;

        _exporting = true;
        SetExportEnabled(false);
        ExportButton.Content = "Exporting…";
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            var path = dialog.FileName;
            await Task.Run(() =>
            {
                switch (extension)
                {
                    case ".csv":
                        CsvExporter.Export(path, rows);
                        break;
                    case ".pdf":
                        PdfExporter.Export(path, rows);
                        break;
                    default:
                        ExcelExporter.Export(path, rows);
                        break;
                }
            });
            StatusText.Text = $"Exported {rows.Count:N0} rows to {path}";
        }
        catch (IOException)
        {
            MessageBox.Show(
                this,
                "Could not write that file. If it is open in another program, close it and try again.",
                "Export failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            _exporting = false;
            ExportButton.Content = "Export";
            SetExportEnabled(VisibleCount() > 0 && !_scanning);
        }
    }

    private void RevealButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: FileEntry entry })
            TryReveal(entry.FilePath);
    }

    private void ItemsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<DataGridColumnHeader>(e.OriginalSource as DependencyObject) is not null)
            return;
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
            return;
        if (ItemsGrid.SelectedItem is FileEntry entry)
            TryReveal(entry.FilePath);
    }

    private void ItemsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ItemsGrid.SelectedItem is FileEntry entry)
        {
            TryReveal(entry.FilePath);
            e.Handled = true;
        }
    }

    private void ItemsGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row is null)
            return;

        if (!row.IsSelected)
            ItemsGrid.SelectedItem = row.Item;

        row.Focus();
    }

    private void ContextReveal_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsGrid.SelectedItem is FileEntry entry)
            TryReveal(entry.FilePath);
    }

    private void ContextCopyName_Click(object sender, RoutedEventArgs e)
    {
        var names = new List<string>();
        foreach (var item in ItemsGrid.SelectedItems)
        {
            if (item is FileEntry entry && entry.FileName.Length > 0)
                names.Add(entry.FileName);
        }

        if (names.Count == 0)
            return;

        Clipboard.SetText(string.Join(Environment.NewLine, names));
        StatusText.Text = names.Count == 1
            ? "Copied the file name."
            : $"Copied {names.Count:N0} file names.";
    }

    private void ContextCopy_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsGrid.SelectedItem is not FileEntry entry)
            return;

        Clipboard.SetText(entry.FilePath);
        StatusText.Text = "Copied the file path.";
    }

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
            return;

        var path = paths[0];
        if (File.Exists(path))
            path = Path.GetDirectoryName(path) ?? path;

        FolderPathBox.Text = path;
        await StartScanAsync();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.E && Keyboard.Modifiers == ModifierKeys.Control && ExportButton.IsEnabled)
        {
            ExportButton_Click(ExportButton, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.F1)
        {
            HelpMenu_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void TryReveal(string path)
    {
        try
        {
            ExplorerReveal.Reveal(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Cannot reveal this item", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void SetScanning(bool scanning)
    {
        _scanning = scanning;
        ScanButton.Content = scanning ? "Cancel" : "Scan";
        ScanButton.IsEnabled = true;
        SetExportEnabled(!scanning && !_exporting && VisibleCount() > 0);
        ScanOverlay.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
        ScanProgress.IsIndeterminate = scanning;
        if (scanning)
            EmptyText.Visibility = Visibility.Collapsed;
    }

    private static string ItemCount(int count)
    {
        return count == 1 ? "1 item" : $"{count:N0} items";
    }

    private void SetExportEnabled(bool enabled)
    {
        ExportButton.IsEnabled = enabled;
        FileExportItem.IsEnabled = enabled;
    }

    private int VisibleCount()
    {
        return _view is CollectionView view ? view.Count : _all.Count;
    }

    private void UpdateStatus()
    {
        var total = _all.Count;
        var shown = VisibleCount();
        var summary = $"{_fileCount:N0} files · {_folderCount:N0} folders";
        if (shown != total)
            summary = $"Showing {shown:N0} of {total:N0} · {summary}";
        if (_lastCancelled)
            summary = "Scan cancelled · " + summary;
        if (_lastSkipped > 0)
            summary += $" · {_lastSkipped:N0} inaccessible";
        if (_lastLinks > 0)
            summary += _lastLinks == 1
                ? " · 1 link not opened"
                : $" · {_lastLinks:N0} links not opened";
        if (_lastElapsed > TimeSpan.Zero)
            summary += $" · {_lastElapsed.TotalSeconds:0.0}s";

        StatusText.Text = summary;
        SetExportEnabled(shown > 0 && !_scanning && !_exporting);

        if (total == 0)
        {
            EmptyText.Text = _lastSkipped > 0
                ? "Could not read that folder. It may be locked, or you may not have permission."
                : "That folder is empty.";
            EmptyText.Visibility = Visibility.Visible;
        }
        else if (shown == 0)
        {
            EmptyText.Text = "No rows match that search.";
            EmptyText.Visibility = Visibility.Visible;
        }
        else
        {
            EmptyText.Visibility = Visibility.Collapsed;
        }
    }

    private void OnNodeExpanded(FolderNode node)
    {
        _ = EnsureChildrenAsync(node);
    }

    private void OnNodeSelected(FolderNode node)
    {
        if (_suppressTreeSelection || node.Kind is FolderKind.Placeholder or FolderKind.Computer)
            return;
        if (!string.IsNullOrWhiteSpace(node.FullPath))
            FolderPathBox.Text = node.FullPath;
    }

    private void FolderTree_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<System.Windows.Controls.Primitives.ToggleButton>(e.OriginalSource as DependencyObject) is not null)
            return;
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject)?.DataContext is not FolderNode node)
            return;

        _ = ScanNodeAsync(node);
    }

    private void FolderTree_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || FolderTree.SelectedItem is not FolderNode node)
            return;

        e.Handled = true;
        _ = ScanNodeAsync(node);
    }

    private async Task ScanNodeAsync(FolderNode node)
    {
        try
        {
            if (node.Kind is FolderKind.Placeholder or FolderKind.Computer)
                return;
            if (string.IsNullOrWhiteSpace(node.FullPath) || !Directory.Exists(node.FullPath))
                return;

            var now = Environment.TickCount64;
            if (_lastTreeScanPath is not null
                && PathsEqual(_lastTreeScanPath, node.FullPath)
                && now - _lastTreeScanTick < 400)
                return;

            if (_scannedRoot is not null && PathsEqual(_scannedRoot, node.FullPath) && !_scanning)
                return;

            _lastTreeScanPath = node.FullPath;
            _lastTreeScanTick = now;
            FolderPathBox.Text = node.FullPath;
            await StartScanAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Could not open that folder.";
            MessageBox.Show(this, ex.Message, "RecurFolder", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private Task EnsureChildrenAsync(FolderNode node)
    {
        if (node.Kind == FolderKind.Placeholder || node.ChildrenLoaded)
            return Task.CompletedTask;
        if (_childLoads.TryGetValue(node, out var pending))
            return pending;

        var task = LoadChildrenAsync(node);
        _childLoads[node] = task;
        return task;
    }

    private async Task LoadChildrenAsync(FolderNode node)
    {
        try
        {
            var children = node.Kind == FolderKind.Computer
                ? await Task.Run(ReadDrives)
                : await Task.Run(() => ReadSubfolders(node.FullPath));

            await Dispatcher.InvokeAsync(() =>
            {
                node.Children.Clear();
                foreach (var child in children)
                    node.Children.Add(child);
                node.ChildrenLoaded = true;
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
        finally
        {
            _childLoads.Remove(node);
        }
    }

    private List<FolderNode> ReadDrives()
    {
        var nodes = new List<FolderNode>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            bool ready;
            try
            {
                ready = drive.IsReady;
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            if (!ready)
                continue;

            var fallback = drive.DriveType switch
            {
                DriveType.Removable => "Removable",
                DriveType.Network => "Network",
                DriveType.CDRom => "Disc",
                _ => "Local Disk"
            };

            string label;
            try
            {
                label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? fallback : drive.VolumeLabel;
            }
            catch (IOException)
            {
                label = fallback;
            }
            catch (UnauthorizedAccessException)
            {
                label = fallback;
            }

            var letter = drive.Name.TrimEnd('\\');
            nodes.Add(CreateNode($"{label} ({letter})", drive.RootDirectory.FullName, FolderKind.Drive, expandable: true));
        }

        nodes.Sort((left, right) => string.Compare(left.FullPath, right.FullPath, StringComparison.OrdinalIgnoreCase));
        return nodes;
    }

    private List<FolderNode> ReadSubfolders(string path)
    {
        var nodes = new List<FolderNode>();
        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return nodes;
        }

        foreach (var directory in directories)
        {
            try
            {
                var attributes = File.GetAttributes(directory);
                if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                    continue;

                var name = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrEmpty(name))
                    continue;

                var isLink = (attributes & FileAttributes.ReparsePoint) != 0;
                nodes.Add(CreateNode(name, directory, FolderKind.Folder, expandable: !isLink));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        nodes.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase));
        return nodes;
    }

    private FolderNode CreateNode(string name, string fullPath, FolderKind kind, bool expandable)
    {
        var node = new FolderNode(name, fullPath, kind);
        node.Expanded += OnNodeExpanded;
        node.Selected += OnNodeSelected;
        if (expandable)
            node.Children.Add(CreatePlaceholder());
        return node;
    }

    private static FolderNode CreatePlaceholder(string name = "")
    {
        return new FolderNode(name, "", FolderKind.Placeholder);
    }

    private async Task RevealPathInTreeAsync(string fullPath, int generation)
    {
        if (_roots.Count == 0)
            return;

        var computer = _roots[0];
        await EnsureChildrenAsync(computer);
        if (generation != _treeRevealGeneration)
            return;

        string normalized;
        try
        {
            normalized = Path.GetFullPath(fullPath);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            return;
        }

        var driveRoot = Path.GetPathRoot(normalized);
        if (string.IsNullOrEmpty(driveRoot))
            return;

        FolderNode? current = null;
        foreach (var drive in computer.Children)
        {
            if (drive.Kind == FolderKind.Placeholder)
                continue;
            if (normalized.StartsWith(drive.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                current = drive;
                break;
            }
        }

        if (current is null)
            return;

        computer.IsExpanded = true;
        var remainder = normalized[driveRoot.Length..].Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parts = remainder.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (generation != _treeRevealGeneration)
                return;

            current.IsExpanded = true;
            await EnsureChildrenAsync(current);
            if (generation != _treeRevealGeneration)
                return;

            var nextPath = Path.Combine(current.FullPath, part);
            var next = current.Children.FirstOrDefault(child =>
                child.Kind != FolderKind.Placeholder && PathsEqual(child.FullPath, nextPath));
            if (next is null)
                break;

            current = next;
        }

        if (generation != _treeRevealGeneration)
            return;

        _suppressTreeSelection = true;
        try
        {
            current.IsSelected = true;
        }
        finally
        {
            _suppressTreeSelection = false;
        }

        await Dispatcher.InvokeAsync(() =>
        {
            FolderTree.UpdateLayout();
            FindContainer(FolderTree, current)?.BringIntoView();
        }, DispatcherPriority.Background);
    }

    private static TreeViewItem? FindContainer(ItemsControl parent, object target)
    {
        if (parent.ItemContainerGenerator.ContainerFromItem(target) is TreeViewItem direct)
            return direct;

        foreach (var item in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(item) is not TreeViewItem container)
                continue;

            var nested = FindContainer(container, target);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
                return match;

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }
}
