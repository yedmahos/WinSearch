
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using Draw = System.Drawing;

using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Management.Deployment;
using Windows.System;

using WinRT.Interop;

namespace WinSearch
{
    public sealed partial class MainWindow : Window
    {
        // Existing Figma geometry
        private const double PanelWidth = 680;
        private const double CollapsedHeight = 74;
        private const double ReferenceExpandedHeight = 310;

        private const int MaxResults = 6;
        private const int IconSize = 32;

        // Bounded file search
        private const int MaxIndexedItems = 30000;
        private const int MaxFolderDepth = 7;

        private readonly SearchGlassBackdrop _glassBackdrop = new();

        private bool _isReady;
        private bool _resizeQueued;
        private bool _fileScanCompleted;

        private readonly List<SearchResult> _installedApps = new();
        private readonly List<SearchResult> _indexedFiles = new();
        private readonly List<SearchResult> _visibleResults = new();

        private readonly Dictionary<string, BitmapImage>
            _iconCache = new(
                StringComparer.OrdinalIgnoreCase);

        private readonly string _iconCacheFolder =
            Path.Combine(
                Path.GetTempPath(),
                "WinSearch",
                "IconsV2");

        private const int DwmCornerPreference = 33;
        private const int DwmRound = 2; // Smooth system-composited corners.

        private const uint SHGFI_ICON = 0x00000100;
        private const uint SHGFI_LARGEICON = 0;

        [StructLayout(
            LayoutKind.Sequential,
            CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [DllImport(
            "shell32.dll",
            CharSet = CharSet.Unicode,
            EntryPoint = "SHGetFileInfoW")]
        private static extern IntPtr SHGetFileInfo(
            string pszPath,
            uint dwFileAttributes,
            out SHFILEINFO psfi,
            uint cbFileInfo,
            uint uFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(
            IntPtr hIcon);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int attribute,
            ref int value,
            int size);

        private enum ResultType
        {
            Shortcut,
            Packaged,
            File,
            Folder
        }

        private sealed record SearchResult(
            string Name,
            string Location,
            string Kind,
            string LaunchTarget,
            ResultType Type,
            AppListEntry? PackageEntry = null);


        public MainWindow()
        {
            InitializeComponent();
            SystemBackdrop = new TransparentSearchBackdrop();
            GlassBackdrop.SystemBackdrop = _glassBackdrop;
            Application.Current.UnhandledException += (_, args) =>
                WriteLifecycleDiagnostic("Unhandled exception: " + args.Exception);
            Closed += (_, _) => WriteLifecycleDiagnostic("Window Closed event received.");

            Title = "Win Search";

            // Set the custom Spotlight Lens window icon.
            string iconPath = System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "WinSearch.ico");

            if (System.IO.File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);
                Debug.WriteLine(
                    "Win Search: Custom icon loaded successfully.");
            }
            else
            {
                Debug.WriteLine(
                    "Win Search: Icon file not found: " + iconPath);
            }

            RootGrid.Loaded += RootGrid_Loaded;
            Activated += (_, _) =>
            {
                // Defer until WinUI has finished processing native activation.
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (!_searchClosing)
                        ApplyNativeWindowStyle();
                });
            };

            RootGrid.ActualThemeChanged += (_, _) =>
            {
                ApplyPanelStyle();
                if (_isReady)
                    UpdateResults();
            };
        }


        // ======================================
        // INITIALIZATION
        // ======================================

        private async void RootGrid_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            RootGrid.Loaded -= RootGrid_Loaded;

            ApplyPanelStyle();
            ConfigureWindow();

            try
            {
                Directory.CreateDirectory(_iconCacheFolder);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"Icon cache unavailable: {ex.Message}");
            }

            DiscoverStartMenuApplications();

            _isReady = true;

            UpdateResults();

            SearchBox.Focus(FocusState.Programmatic);
            QueueResize();

            await DiscoverPackagedApplicationsAsync();

            UpdateResults();

            try
            {
                List<SearchResult> discovered =
                    await Task.Run(ScanUserFiles);

                _indexedFiles.Clear();
                _indexedFiles.AddRange(discovered);

                _fileScanCompleted = true;

                Debug.WriteLine(
                    $"Indexed {_indexedFiles.Count} files/folders.");

                UpdateResults();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"File indexing failed: {ex}");
            }
        }

        private SolidColorBrush PrimaryTextBrush() => new SolidColorBrush(
            RootGrid.ActualTheme == ElementTheme.Dark
                ? Colors.White : ColorHelper.FromArgb(255, 26, 26, 26));

        private SolidColorBrush SecondaryTextBrush() => new SolidColorBrush(
            RootGrid.ActualTheme == ElementTheme.Dark
                ? ColorHelper.FromArgb(148, 255, 255, 255)
                : ColorHelper.FromArgb(255, 69, 76, 87));

        private void ClearQuery_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = string.Empty;
            SearchBox.Focus(FocusState.Programmatic);
        }

        private static T? FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T element && element.Name == name) return element;
                T? found = FindVisualChild<T>(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private void ApplyPanelStyle()
        {
            bool dark = RootGrid.ActualTheme == ElementTheme.Dark;
            ApplyNativeWindowStyle();
            _glassBackdrop.SetAppearance(dark);
            // Subtle rim and highlight follow the same radius as the acrylic surface.
            GlassEdge.BorderBrush = new SolidColorBrush(dark
                ? ColorHelper.FromArgb(48, 255, 255, 255)
                : ColorHelper.FromArgb(115, 255, 255, 255));
            GlassEdge.Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop { Color = ColorHelper.FromArgb(dark ? (byte)10 : (byte)18, 255, 255, 255), Offset = 0 },
                    new GradientStop { Color = Colors.Transparent, Offset = 0.65 }
                }
            };
            // The window itself stays transparent, including outside the rounded surface.
            SearchPanel.BorderBrush = new SolidColorBrush(Colors.Transparent);
            SearchPanel.Background = new SolidColorBrush(Colors.Transparent);
            SearchField.Background = new SolidColorBrush(dark
                ? ColorHelper.FromArgb(24, 42, 42, 42)
                : ColorHelper.FromArgb(28, 255, 255, 255));
            SearchField.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(77, 255, 255, 255));
            SearchBox.Foreground = PrimaryTextBrush();
            ClearGlyph.Source = new SvgImageSource(new Uri(
                "ms-appx:///Assets/Figma/clear-" + (dark ? "dark" : "light") + ".svg"));
            SearchGlyph.Source = new SvgImageSource(new Uri(
                "ms-appx:///Assets/Figma/search-" + (dark ? "dark" : "light") + ".svg"));
        }

        private static void WriteLifecycleDiagnostic(string message)
        {
            try
            {
                string folder = Path.Combine(Path.GetTempPath(), "WinSearch");
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "lifecycle.log"),
                    DateTimeOffset.Now.ToString("O") + " " + message + Environment.NewLine);
            }
            catch (Exception ex) { Debug.WriteLine(ex); }
        }

        private void ConfigureWindow()
        {
            if (AppWindow.Presenter is
                OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(
                    false,
                    false);

                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
            }

            ApplyNativeWindowStyle();

            // Global Alt+Space and outside-click
            // dismissal are implemented in
            // MainWindow.Hotkey.cs.
            InitializeGlobalHotkey();
        }

        private void ApplyNativeWindowStyle()
        {
            if (_searchClosing)
                return;

            IntPtr hwnd = WindowNative.GetWindowHandle(this);
            if (hwnd == IntPtr.Zero)
                return;

            // Keep the native frame in sync with XAML after theme/activation changes.
            int dark = RootGrid.ActualTheme == ElementTheme.Dark ? 1 : 0;
            SetNativeStyleAttribute(hwnd, 20, dark, "dark mode");
            SetNativeStyleAttribute(hwnd, DwmCornerPreference, DwmRound, "rounded corners");
            SetNativeStyleAttribute(hwnd, 34, unchecked((int)0xFFFFFFFE), "border suppression");
        }

        private static void SetNativeStyleAttribute(IntPtr hwnd, int attribute, int value, string name)
        {
            int result = DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
            if (result < 0)
                WriteLifecycleDiagnostic($"Native {name} failed: {result:X8}");
        }

        // ======================================
        // START MENU APPLICATION DISCOVERY
        // ======================================

        private void DiscoverStartMenuApplications()
        {
            _installedApps.Clear();

            string userStartMenu = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.StartMenu),
                "Programs");

            string commonStartMenu = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonStartMenu),
                "Programs");

            HashSet<string> names = new(
                StringComparer.OrdinalIgnoreCase);

            ScanStartMenuDirectory(
                userStartMenu,
                names);

            ScanStartMenuDirectory(
                commonStartMenu,
                names);
        }

        private void ScanStartMenuDirectory(
            string folder,
            HashSet<string> names)
        {
            if (!Directory.Exists(folder))
                return;

            try
            {
                foreach (string shortcut in
                    Directory.EnumerateFiles(
                        folder,
                        "*.lnk",
                        SearchOption.TopDirectoryOnly))
                {
                    string name =
                        Path.GetFileNameWithoutExtension(
                            shortcut).Trim();

                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    if (name.Equals(
                        "Uninstall",
                        StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (name.StartsWith(
                        "Uninstall ",
                        StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!names.Add(name))
                        continue;

                    _installedApps.Add(
                        new SearchResult(
                            name,
                            "Application",
                            "App",
                            shortcut,
                            ResultType.Shortcut));
                }

                foreach (string subfolder in
                    Directory.EnumerateDirectories(folder))
                {
                    ScanStartMenuDirectory(
                        subfolder,
                        names);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore protected folders.
            }
            catch (IOException ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        // ======================================
        // PACKAGED APPLICATION DISCOVERY
        // ======================================

        private async Task DiscoverPackagedApplicationsAsync()
        {
            try
            {
                PackageManager manager =
                    new PackageManager();

                var packages =
                    manager.FindPackagesForUserWithPackageTypes(
                        null!,
                        PackageTypes.Main |
                        PackageTypes.Optional);

                HashSet<string> names = new(
                    _installedApps.Select(x => x.Name),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var package in packages)
                {
                    try
                    {
                        var entries =
                            await package.GetAppListEntriesAsync();

                        foreach (AppListEntry entry in entries)
                        {
                            string name;

                            try
                            {
                                name =
                                    entry.DisplayInfo.DisplayName?
                                    .Trim() ?? string.Empty;
                            }
                            catch
                            {
                                continue;
                            }

                            if (string.IsNullOrWhiteSpace(name))
                                continue;

                            string appId =
                                entry.AppUserModelId;

                            if (string.IsNullOrWhiteSpace(appId))
                                continue;

                            if (!names.Add(name))
                                continue;

                            _installedApps.Add(
                                new SearchResult(
                                    name,
                                    "Application",
                                    "App",
                                    appId,
                                    ResultType.Packaged,
                                    entry));
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            $"Package skipped: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"Package discovery error: {ex}");
            }

            _installedApps.Sort((a, b) =>
                StringComparer.OrdinalIgnoreCase.Compare(
                    a.Name,
                    b.Name));
        }

        // ======================================
        // FILE AND FOLDER DISCOVERY
        // ======================================

        private List<SearchResult> ScanUserFiles()
        {
            List<SearchResult> results = new();

            string userProfile =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile);

            HashSet<string> roots = new(
                StringComparer.OrdinalIgnoreCase);

            void AddRoot(string path)
            {
                if (!string.IsNullOrWhiteSpace(path) &&
                    Directory.Exists(path))
                {
                    roots.Add(Path.GetFullPath(path));
                }
            }

            AddRoot(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.DesktopDirectory));

            AddRoot(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.MyDocuments));

            AddRoot(
                Path.Combine(userProfile, "Downloads"));

            AddRoot(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.MyPictures));

            AddRoot(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.MyMusic));

            AddRoot(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.MyVideos));

            string? oneDrive =
                Environment.GetEnvironmentVariable("OneDrive");

            if (!string.IsNullOrWhiteSpace(oneDrive))
            {
                AddRoot(Path.Combine(oneDrive, "Desktop"));
                AddRoot(Path.Combine(oneDrive, "Documents"));
            }

            HashSet<string> seenPaths = new(
                StringComparer.OrdinalIgnoreCase);

            foreach (string root in roots)
            {
                if (results.Count >= MaxIndexedItems)
                    break;

                ScanFileDirectory(
                    root,
                    0,
                    results,
                    seenPaths);
            }

            return results;
        }

        private void ScanFileDirectory(
            string folder,
            int depth,
            List<SearchResult> results,
            HashSet<string> seenPaths)
        {
            if (depth > MaxFolderDepth ||
                results.Count >= MaxIndexedItems)
            {
                return;
            }

            try
            {
                DirectoryInfo directory =
                    new DirectoryInfo(folder);

                if (!directory.Exists)
                    return;

                if ((directory.Attributes &
                    FileAttributes.ReparsePoint) != 0)
                {
                    return;
                }

                foreach (string file in
                    Directory.EnumerateFiles(folder))
                {
                    if (results.Count >= MaxIndexedItems)
                        return;

                    try
                    {
                        if (!seenPaths.Add(file))
                            continue;

                        string name = Path.GetFileName(file);

                        if (string.IsNullOrWhiteSpace(name))
                            continue;

                        string extension =
                            Path.GetExtension(file)
                                .TrimStart('.')
                                .ToUpperInvariant();

                        results.Add(
                            new SearchResult(
                                name,
                                GetDisplayLocation(file),
                                string.IsNullOrEmpty(extension)
                                    ? "File"
                                    : extension,
                                file,
                                ResultType.File));
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            $"File skipped: {ex.Message}");
                    }
                }

                foreach (string subfolder in
                    Directory.EnumerateDirectories(folder))
                {
                    if (results.Count >= MaxIndexedItems)
                        return;

                    try
                    {
                        DirectoryInfo subdir =
                            new DirectoryInfo(subfolder);

                        if ((subdir.Attributes &
                            FileAttributes.ReparsePoint) != 0)
                        {
                            continue;
                        }

                        string name = subdir.Name;

                        if (ShouldSkipFolder(name))
                            continue;

                        if (seenPaths.Add(subfolder))
                        {
                            results.Add(
                                new SearchResult(
                                    name,
                                    GetDisplayLocation(subfolder),
                                    "Folder",
                                    subfolder,
                                    ResultType.Folder));
                        }

                        ScanFileDirectory(
                            subfolder,
                            depth + 1,
                            results,
                            seenPaths);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            $"Folder skipped: {ex.Message}");
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException ex)
            {
                Debug.WriteLine(
                    $"Directory scan error: {ex.Message}");
            }
        }

        private static bool ShouldSkipFolder(string name)
        {
            return name.Equals(
                    "node_modules",
                    StringComparison.OrdinalIgnoreCase)
                || name.Equals(
                    ".git",
                    StringComparison.OrdinalIgnoreCase)
                || name.Equals(
                    ".vs",
                    StringComparison.OrdinalIgnoreCase)
                || name.Equals(
                    "bin",
                    StringComparison.OrdinalIgnoreCase)
                || name.Equals(
                    "obj",
                    StringComparison.OrdinalIgnoreCase)
                || name.Equals(
                    "__pycache__",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static string GetDisplayLocation(string path)
        {
            string? parent = Path.GetDirectoryName(path);

            if (string.IsNullOrWhiteSpace(parent))
                return "File";

            string folderName =
                Path.GetFileName(
                    parent.TrimEnd(
                        Path.DirectorySeparatorChar));

            return string.IsNullOrWhiteSpace(folderName)
                ? parent
                : folderName;
        }

        // ======================================
        // APPLICATION-FIRST SEARCH RANKING
        // ======================================

        private List<SearchResult> SearchAll(string query)
        {
            // Applications always come first.
            List<SearchResult> apps = _installedApps
                .Where(app =>
                    app.Name.Contains(
                        query,
                        StringComparison.OrdinalIgnoreCase))
                .OrderBy(app =>
                    GetNameMatchRank(app.Name, query))
                .ThenBy(app => app.Name.Length)
                .ThenBy(
                    app => app.Name,
                    StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();

            if (apps.Count >= MaxResults)
                return apps;

            // Files fill remaining slots.
            List<SearchResult> files = _indexedFiles
                .Where(item =>
                    item.Type == ResultType.File &&
                    item.Name.Contains(
                        query,
                        StringComparison.OrdinalIgnoreCase))
                .OrderBy(item =>
                    GetNameMatchRank(item.Name, query))
                .ThenBy(item => item.Name.Length)
                .ThenBy(
                    item => item.Name,
                    StringComparer.OrdinalIgnoreCase)
                .Take(MaxResults - apps.Count)
                .ToList();

            List<SearchResult> results = new(apps);

            results.AddRange(files);

            if (results.Count >= MaxResults)
                return results;

            // Folders fill any remaining slots.
            List<SearchResult> folders = _indexedFiles
                .Where(item =>
                    item.Type == ResultType.Folder &&
                    item.Name.Contains(
                        query,
                        StringComparison.OrdinalIgnoreCase))
                .OrderBy(item =>
                    GetNameMatchRank(item.Name, query))
                .ThenBy(item => item.Name.Length)
                .ThenBy(
                    item => item.Name,
                    StringComparer.OrdinalIgnoreCase)
                .Take(MaxResults - results.Count)
                .ToList();

            results.AddRange(folders);

            return results;
        }

        private static int GetNameMatchRank(
            string name,
            string query)
        {
            if (name.Equals(
                query,
                StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (name.StartsWith(
                query,
                StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            if (name.Contains(
                " " + query,
                StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            return 3;
        }

        private void SearchBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            if (_isReady)
                UpdateResults();
        }

        private void UpdateResults()
        {
            string query = SearchBox.Text.Trim();
            ClearQueryButton.Visibility = SearchBox.Text.Length == 0
                ? Visibility.Collapsed : Visibility.Visible;
            if (FindVisualChild<TextBlock>(SearchBox, "QueryPlaceholder") is TextBlock placeholder)
                placeholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

            ResultsHost.Children.Clear();
            _visibleResults.Clear();

            if (query.Length == 0)
            {
                ResultsHost.Visibility =
                    Visibility.Collapsed;

                QueueResize();
                return;
            }

            ResultsHost.Visibility =
                Visibility.Visible;

            List<SearchResult> results =
                SearchAll(query);

            _visibleResults.AddRange(results);

            if (results.Count == 0)
            {
                BuildEmptyState();
                QueueResize();
                return;
            }

            AddGroup(
                "Top Hit",
                results.Take(1).ToList(),
                true);

            SearchResult[] remaining =
                results.Skip(1).ToArray();

            List<SearchResult> apps = remaining
                .Where(r =>
                    r.Type == ResultType.Shortcut ||
                    r.Type == ResultType.Packaged)
                .ToList();

            List<SearchResult> files = remaining
                .Where(r => r.Type == ResultType.File)
                .ToList();

            List<SearchResult> folders = remaining
                .Where(r => r.Type == ResultType.Folder)
                .ToList();

            AddGroup("Applications", apps, false);
            AddGroup("Files", files, false);
            AddGroup("Folders", folders, false);

            QueueResize();
        }

        // ======================================
        // RESULT GROUPS
        // ======================================

        private void AddGroup(
            string title,
            List<SearchResult> results,
            bool selectFirst)
        {
            if (results.Count == 0)
                return;

            StackPanel group = new StackPanel
            {
                Spacing = 0
            };

            Grid heading = new Grid
            {
                Height = 30,
                Padding = new Thickness(12, 8, 12, 6)
            };

            heading.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 12,
                FontWeight =
                    Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = SecondaryTextBrush()
            });

            group.Children.Add(heading);

            for (int i = 0; i < results.Count; i++)
            {
                group.Children.Add(
                    CreateResultRow(
                        results[i],
                        selectFirst && i == 0));
            }

            ResultsHost.Children.Add(group);
        }

        // ======================================
        // RESULT ROWS WITH 40 PX ICONS
        // ======================================

        private Border CreateResultRow(
            SearchResult result,
            bool selected)
        {
            bool dark =
                RootGrid.ActualTheme == ElementTheme.Dark;

            Border row = new Border
            {
                Height = 56,
                CornerRadius = new CornerRadius(8),

                Background = new SolidColorBrush(
                    selected
                        ? (dark
                            ? ColorHelper.FromArgb(
                                97, 13, 13, 13)
                            : ColorHelper.FromArgb(
                                115, 255, 255, 255))
                        : Microsoft.UI.Colors.Transparent)
            };

            Grid content = new Grid
            {
                Padding = new Thickness(12, 8, 12, 8),
                ColumnSpacing = 12
            };

            content.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(IconSize)
                });

            content.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(
                        1,
                        GridUnitType.Star)
                });

            content.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(36)
                });

            Grid iconHost = new Grid
            {
                Width = IconSize,
                Height = IconSize,
                HorizontalAlignment =
                    HorizontalAlignment.Center,
                VerticalAlignment =
                    VerticalAlignment.Center
            };

            FontIcon fallbackIcon = new FontIcon
            {
                Glyph =
                    result.Type == ResultType.Folder
                        ? "\uE8B7"
                        : result.Type == ResultType.File
                            ? "\uE8A5"
                            : "\uE71D",

                FontFamily = new FontFamily(
                    "Segoe Fluent Icons"),
                FontSize = 32,
                Width = IconSize,
                Height = IconSize,
                HorizontalAlignment =
                    HorizontalAlignment.Center,
                VerticalAlignment =
                    VerticalAlignment.Center
            };

            Image itemIcon = new Image
            {
                Width = IconSize,
                Height = IconSize,
                Stretch = Stretch.Uniform,
                Visibility = Visibility.Collapsed,
                HorizontalAlignment =
                    HorizontalAlignment.Center,
                VerticalAlignment =
                    VerticalAlignment.Center
            };

            iconHost.Children.Add(fallbackIcon);
            iconHost.Children.Add(itemIcon);

            Grid.SetColumn(iconHost, 0);
            content.Children.Add(iconHost);

            _ = LoadResultIconAsync(
                result,
                itemIcon,
                fallbackIcon);

            StackPanel labels = new StackPanel
            {
                Spacing = 0,
                VerticalAlignment =
                    VerticalAlignment.Center
            };

            labels.Children.Add(new TextBlock
            {
                Text = result.Name,
                Foreground = PrimaryTextBrush(),
                FontSize = 14,
                MaxLines = 1,
                TextTrimming =
                    TextTrimming.CharacterEllipsis
            });

            labels.Children.Add(new TextBlock
            {
                Text = result.Location,
                FontSize = 12,
                Foreground = SecondaryTextBrush(),
                MaxLines = 1,
                TextTrimming =
                    TextTrimming.CharacterEllipsis
            });

            Grid.SetColumn(labels, 1);
            content.Children.Add(labels);

            TextBlock kind = new TextBlock
            {
                Text = result.Kind,
                FontSize = 11,
                Foreground = SecondaryTextBrush(),
                TextAlignment = TextAlignment.Right,
                VerticalAlignment =
                    VerticalAlignment.Center
            };

            Grid.SetColumn(kind, 2);
            content.Children.Add(kind);

            Grid container = new Grid();
            container.Children.Add(content);

            if (selected)
            {
                Border indicator = new Border
                {
                    Width = 3,
                    Height = 24,
                    CornerRadius = new CornerRadius(2),

                    Background = new SolidColorBrush(
                        ColorHelper.FromArgb(255, 0, 103, 192)),

                    HorizontalAlignment =
                        HorizontalAlignment.Left,
                    VerticalAlignment =
                        VerticalAlignment.Center,
                    IsHitTestVisible = false
                };

                container.Children.Add(indicator);
            }

            row.Child = container;
            return row;
        }

        // ======================================
        // ICON LOADING AND CACHING
        // ======================================

        private async Task LoadResultIconAsync(
            SearchResult result,
            Image target,
            FontIcon fallback)
        {
            try
            {
                string key =
                    $"{result.Type}|{result.LaunchTarget}";

                if (_iconCache.TryGetValue(
                    key,
                    out BitmapImage? cached))
                {
                    target.Source = cached;
                    target.Visibility = Visibility.Visible;
                    fallback.Visibility = Visibility.Collapsed;
                    return;
                }

                string cachePath = GetIconCachePath(key);

                if (!File.Exists(cachePath))
                {
                    if (result.Type == ResultType.Packaged &&
                        result.PackageEntry != null)
                    {
                        await ExtractPackagedIconAsync(
                            result.PackageEntry,
                            cachePath);
                    }
                    else
                    {
                        await Task.Run(() =>
                            ExtractShellIcon(
                                result.LaunchTarget,
                                cachePath));
                    }
                }

                if (!File.Exists(cachePath))
                    return;

                BitmapImage bitmap =
                    new BitmapImage(
                        new Uri(cachePath));

                _iconCache[key] = bitmap;

                target.Source = bitmap;
                target.Visibility = Visibility.Visible;
                fallback.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"Icon unavailable ({result.Name}): {ex.Message}");
            }
        }

        private string GetIconCachePath(string key)
        {
            byte[] hash =
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(key));

            return Path.Combine(
                _iconCacheFolder,
                Convert.ToHexString(hash) + ".png");
        }

        private static void ExtractShellIcon(
            string itemPath,
            string outputPath)
        {
            IntPtr result = SHGetFileInfo(
                itemPath,
                0,
                out SHFILEINFO info,
                (uint)Marshal.SizeOf<SHFILEINFO>(),
                SHGFI_ICON | SHGFI_LARGEICON);

            if (result == IntPtr.Zero ||
                info.hIcon == IntPtr.Zero)
            {
                return;
            }

            try
            {
                using Draw.Icon icon =
                    (Draw.Icon)Draw.Icon
                        .FromHandle(info.hIcon)
                        .Clone();

                using Draw.Bitmap bitmap =
                    icon.ToBitmap();

                SaveNormalizedIcon(
                    bitmap,
                    outputPath);
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        }

        private static async Task ExtractPackagedIconAsync(
            AppListEntry entry,
            string outputPath)
        {
            var logoReference =
                entry.DisplayInfo.GetLogo(
                    new Size(256, 256));

            using var stream =
                await logoReference.OpenReadAsync();

            using Stream managedStream =
                stream.AsStreamForRead();

            using Draw.Bitmap source =
                new Draw.Bitmap(managedStream);

            SaveNormalizedIcon(
                source,
                outputPath);
        }

        private static void SaveNormalizedIcon(
            Draw.Bitmap source,
            string outputPath)
        {
            int left = source.Width;
            int top = source.Height;
            int right = -1;
            int bottom = -1;

            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    Draw.Color pixel =
                        source.GetPixel(x, y);

                    if (pixel.A <= 12)
                        continue;

                    if (x < left) left = x;
                    if (y < top) top = y;
                    if (x > right) right = x;
                    if (y > bottom) bottom = y;
                }
            }

            Draw.Rectangle crop =
                right < left || bottom < top
                    ? new Draw.Rectangle(
                        0,
                        0,
                        source.Width,
                        source.Height)
                    : Draw.Rectangle.FromLTRB(
                        left,
                        top,
                        right + 1,
                        bottom + 1);

            const int outputSize = 160;
            const int padding = 4;

            using Draw.Bitmap output =
                new Draw.Bitmap(
                    outputSize,
                    outputSize,
                    Draw.Imaging.PixelFormat.Format32bppArgb);

            using (Draw.Graphics g =
                Draw.Graphics.FromImage(output))
            {
                g.Clear(Draw.Color.Transparent);

                g.InterpolationMode =
                    Draw.Drawing2D.InterpolationMode
                        .HighQualityBicubic;

                g.SmoothingMode =
                    Draw.Drawing2D.SmoothingMode
                        .HighQuality;

                double available =
                    outputSize - padding * 2;

                double scale = Math.Min(
                    available / crop.Width,
                    available / crop.Height);

                int width = Math.Max(
                    1,
                    (int)Math.Round(crop.Width * scale));

                int height = Math.Max(
                    1,
                    (int)Math.Round(crop.Height * scale));

                g.DrawImage(
                    source,
                    new Draw.Rectangle(
                        (outputSize - width) / 2,
                        (outputSize - height) / 2,
                        width,
                        height),
                    crop,
                    Draw.GraphicsUnit.Pixel);
            }

            string tempPath =
                outputPath + "." +
                Guid.NewGuid().ToString("N") +
                ".tmp";

            try
            {
                output.Save(
                    tempPath,
                    Draw.Imaging.ImageFormat.Png);

                File.Move(
                    tempPath,
                    outputPath,
                    overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        // ======================================
        // EMPTY RESULTS
        // ======================================

        private void BuildEmptyState()
        {
            Grid container = new Grid
            {
                Height = 184
            };

            StackPanel message = new StackPanel
            {
                Spacing = 8,
                HorizontalAlignment =
                    HorizontalAlignment.Center,
                VerticalAlignment =
                    VerticalAlignment.Center
            };

            message.Children.Add(new Image
            {
                Source = new SvgImageSource(new Uri("ms-appx:///Assets/Figma/empty-light.svg")),
                Width = 32,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            });

            message.Children.Add(new TextBlock
            {
                Text = "No results found",
                Foreground = PrimaryTextBrush(),
                FontSize = 16,
                FontWeight =
                    Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment =
                    HorizontalAlignment.Center
            });

            message.Children.Add(new TextBlock
            {
                Text = _fileScanCompleted
                    ? "Try a different app or filename."
                    : "Searching installed apps and files...",
                FontSize = 13,
                Foreground = SecondaryTextBrush(),
                HorizontalAlignment =
                    HorizontalAlignment.Center
            });

            container.Children.Add(message);
            ResultsHost.Children.Add(container);
        }

        // ======================================
        // WINDOW SIZING AND POSITIONING
        // ======================================

        private void QueueResize()
        {
            if (!_isReady || _resizeQueued)
                return;

            _resizeQueued = true;

            bool scheduled =
                DispatcherQueue.TryEnqueue(() =>
                {
                    _resizeQueued = false;
                    ResizeToContent();
                });

            if (!scheduled)
                _resizeQueued = false;
        }

        private void ResizeToContent()
        {
            if (RootGrid.XamlRoot == null)
                return;

            double scale =
                RootGrid.XamlRoot.RasterizationScale;

            int width = (int)Math.Ceiling(
                PanelWidth * scale);

            bool collapsed =
                string.IsNullOrWhiteSpace(
                    SearchBox.Text);

            int height;

            if (collapsed)
            {
                height = (int)Math.Ceiling(
                    CollapsedHeight * scale);
            }
            else
            {
                SearchPanel.Measure(new Size(
                    PanelWidth,
                    double.PositiveInfinity));

                height = (int)Math.Ceiling(
                    SearchPanel.DesiredSize.Height * scale);
            }

            if (AppWindow.ClientSize.Width != width ||
                AppWindow.ClientSize.Height != height)
            {
                AppWindow.ResizeClient(
                    new SizeInt32(width, height));
            }

            PositionWindow();
        }

        private void PositionWindow()
        {
            DisplayArea display =
                DisplayArea.GetFromWindowId(
                    AppWindow.Id,
                    DisplayAreaFallback.Primary);

            RectInt32 area = display.WorkArea;

            int windowWidth = AppWindow.Size.Width;
            int windowHeight = AppWindow.Size.Height;

            int x = area.X +
                (area.Width - windowWidth) / 2;

            // Keep the search field anchored while results grow below it.
            double scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
            int referenceHeight = (int)Math.Ceiling(ReferenceExpandedHeight * scale);
            int y = area.Y + (area.Height - referenceHeight) / 2;

            int maxX =
                area.X + area.Width - windowWidth;

            int maxY =
                area.Y + area.Height - windowHeight;

            x = Math.Clamp(
                x,
                area.X,
                Math.Max(area.X, maxX));

            y = Math.Clamp(
                y,
                area.Y,
                Math.Max(area.Y, maxY));

            AppWindow.Move(
                new PointInt32(x, y));
        }

        // ======================================
        // KEYBOARD AND RESULT LAUNCHING
        // ======================================

        private async void SearchBox_KeyDown(
            object sender,
            KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Escape)
            {
                SearchBox.Text = string.Empty;

                SearchBox.Focus(
                    FocusState.Programmatic);

                e.Handled = true;
                return;
            }

            if (e.Key == VirtualKey.Enter &&
                _visibleResults.Count > 0)
            {
                e.Handled = true;

                await OpenResultAsync(
                    _visibleResults[0]);
            }
        }

        private async Task OpenResultAsync(
            SearchResult result)
        {
            try
            {
                if (result.Type == ResultType.Packaged &&
                    result.PackageEntry != null)
                {
                    bool launched =
                        await result.PackageEntry.LaunchAsync();

                    if (!launched)
                    {
                        Debug.WriteLine(
                            $"Launch returned false: {result.Name}");
                    }

                    return;
                }

                if (result.Type == ResultType.Shortcut ||
                    result.Type == ResultType.File ||
                    result.Type == ResultType.Folder)
                {
                    if (!File.Exists(result.LaunchTarget) &&
                        !Directory.Exists(result.LaunchTarget))
                    {
                        Debug.WriteLine(
                            $"Missing item: {result.LaunchTarget}");

                        return;
                    }

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = result.LaunchTarget,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"Unable to open {result.Name}: {ex}");
            }
        }
    }

    // Each backdrop owns its system compositor; dispose brushes before their compositor.
    internal sealed class TransparentSearchBackdrop : SystemBackdrop
    {
        private Windows.UI.Composition.Compositor? _compositor;
        private Windows.UI.Composition.CompositionColorBrush? _brush;

        protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot root)
        {
            base.OnTargetConnected(target, root);
            DispatcherQueue.EnsureSystemDispatcherQueue();
            _compositor = new Windows.UI.Composition.Compositor();
            _brush = _compositor.CreateColorBrush(Colors.Transparent);
            target.SystemBackdrop = _brush;
        }

        protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
        {
            target.SystemBackdrop = null;
            _brush?.Dispose();
            _brush = null;
            _compositor?.Dispose();
            _compositor = null;
            base.OnTargetDisconnected(target);
        }
    }

    // Owned by GlassBackdrop, not attached directly to the native window.
    // Keep the framework configuration for activation, transparency policy and high contrast.
    internal sealed class SearchGlassBackdrop : SystemBackdrop
    {
        private DesktopAcrylicController? _controller;
        private Windows.UI.Composition.Compositor? _fallbackCompositor;
        private Windows.UI.Composition.CompositionColorBrush? _fallback;
        private bool _dark;

        public void SetAppearance(bool dark)
        {
            _dark = dark;
            ApplyAppearance();
        }

        private void ApplyAppearance()
        {
            var tint = _dark
                ? ColorHelper.FromArgb(255, 24, 32, 43)
                : ColorHelper.FromArgb(255, 247, 250, 255);
            var fallback = _dark
                ? ColorHelper.FromArgb(255, 32, 36, 43)
                : ColorHelper.FromArgb(255, 242, 246, 250);

            if (_fallback != null)
                _fallback.Color = fallback;

            if (_controller == null)
                return;

            // Starting values for Windows visual testing, not a Figma blur-radius mapping.
            _controller.TintColor = tint;
            _controller.TintOpacity = _dark ? 0.35f : 0.18f;
            _controller.LuminosityOpacity = _dark ? 0.55f : 0.65f;
            _controller.FallbackColor = fallback;
        }

        protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot root)
        {
            base.OnTargetConnected(target, root);
            if (DesktopAcrylicController.IsSupported())
            {
                var controller = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Thin };
                try
                {
                    controller.SetSystemBackdropConfiguration(
                        GetDefaultSystemBackdropConfiguration(target, root));
                    controller.AddSystemBackdropTarget(target);
                    _controller = controller;
                    ApplyAppearance();
                    return;
                }
                catch (Exception ex)
                {
                    _controller = null;
                    controller.Dispose();
                    Debug.WriteLine($"Rounded acrylic unavailable; using solid fallback: {ex}");
                }
            }

            DispatcherQueue.EnsureSystemDispatcherQueue();
            _fallbackCompositor = new Windows.UI.Composition.Compositor();
            _fallback = _fallbackCompositor.CreateColorBrush();
            ApplyAppearance();
            target.SystemBackdrop = _fallback;
        }

        protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
        {
            // Clear the field before disposal so queued theme callbacks cannot touch a closed controller.
            var controller = _controller;
            _controller = null;
            if (controller != null)
            {
                controller.RemoveSystemBackdropTarget(target);
                controller.Dispose();
            }
            target.SystemBackdrop = null;
            _fallback?.Dispose();
            _fallback = null;
            _fallbackCompositor?.Dispose();
            _fallbackCompositor = null;
            base.OnTargetDisconnected(target);
        }
    }
}

