using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Gorilla.Manager;
using Gorilla.Manager.Engine;
using Microsoft.Win32;

namespace Gorilla.CrashDetails
{
    public sealed class DuplicatesPage : UserControl, IPage
    {
        private readonly Shell shell;
        private readonly RadioButton navigation;
        private readonly TextBlock folders = new TextBlock();
        private readonly TextBlock status = new TextBlock();
        private readonly CheckBox disabled = new CheckBox { Content = "Include disabled CAKs" };
        private readonly CheckBox large = new CheckBox { Content = "Compare large CAKs (slower)" };
        private readonly CheckBox characters = new CheckBox { Content = "Character/roster paths" };
        private readonly ComboBox kind = new ComboBox { MinWidth = 175 };
        private readonly TextBox search = new TextBox { MinWidth = 210, ToolTip = "Search internal paths, CAK names or full locations" };
        private readonly DataGrid table = new DataGrid();
        private readonly TextBox details = new TextBox();
        private readonly Button scan = new Button { Content = "Scan CAKs" };
        private readonly Button cancel = new Button { Content = "Cancel", IsEnabled = false };
        private readonly Button save = new Button { Content = "Save full report", IsEnabled = false };
        private readonly Button notes = new Button { Content = "Scan notes", IsEnabled = false };
        private readonly ProgressBar progress = new ProgressBar { Height = 3, Visibility = Visibility.Collapsed, IsIndeterminate = true };
        private DuplicateScanResult last;
        private CancellationTokenSource running;
        private bool opened;
        public bool Loading { get; private set; }

        // The constructor hook passes the existing page registry, avoiding
        // reflection or a second navigation system. Native Go/RefreshAll work.
        public static void Attach(Shell shell, IDictionary<string, IPage> pages)
        {
            if (pages.ContainsKey("duplicates")) return;
            if (!(shell.FindName("NavFix") is RadioButton reference) || !(reference.Parent is Panel links) ||
                !(shell.FindName("Host") is Panel host)) throw new InvalidOperationException("The original navigation controls were not found.");
            var nav = new RadioButton { Content = "Duplicate files", Style = reference.Style, GroupName = reference.GroupName,
                Tag = shell.TryFindResource("IconList"), ToolTip = "Compare CAK archives and internal files" };
            var page = new DuplicatesPage(shell, nav) { Visibility = Visibility.Collapsed };
            pages.Add("duplicates", page);
            host.Children.Add(page);
            links.Children.Insert(links.Children.IndexOf(reference) + 1, nav);
            nav.Click += (sender, args) => shell.Go("duplicates");
            shell.Closed += (sender, args) => page.running?.Cancel();
        }

        private DuplicatesPage(Shell shell, RadioButton navigation)
        {
            this.shell = shell;
            this.navigation = navigation;
            Foreground = shell.TryFindResource("Text") as Brush ?? Brushes.White;
            Brush surface = shell.TryFindResource("Surface2") as Brush ?? new SolidColorBrush(Color.FromRgb(30, 30, 36));
            var root = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            for (int i = 0; i < 7; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star), MinHeight = 100 });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 100 });
            Content = root;
            Add(0, new TextBlock { Text = "Duplicate files", FontSize = 28, FontWeight = FontWeights.SemiBold });
            Add(1, new TextBlock { Text = "Find identical CAKs, matching stored files and different versions of shared paths. Select a result for hashes and exact locations.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 8) });
            folders.TextWrapping = TextWrapping.Wrap; folders.FontSize = 11; folders.Margin = new Thickness(0, 0, 0, 8); Add(2, folders);
            var actions = new WrapPanel();
            foreach (var button in new[] { scan, cancel, save, notes })
            {
                button.Padding = new Thickness(12, 6, 12, 6); button.Margin = new Thickness(0, 0, 8, 6);
                if (shell.TryFindResource("GhostButton") is Style style) button.Style = style;
                actions.Children.Add(button);
            }
            foreach (var option in new[] { disabled, large }) { option.Margin = new Thickness(4, 8, 12, 6); actions.Children.Add(option); }
            Add(3, actions);
            var filters = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            kind.ItemsSource = new[] { "All results", "Identical CAKs", "Identical stored files", "Different versions", "Unverified", "File problem" };
            kind.SelectedIndex = 0; kind.Margin = new Thickness(0, 0, 8, 4); search.Margin = new Thickness(0, 0, 8, 4);
            filters.Children.Add(kind); filters.Children.Add(new TextBlock { Text = "Search: ", VerticalAlignment = VerticalAlignment.Center });
            filters.Children.Add(search); characters.Margin = new Thickness(0, 5, 0, 4); filters.Children.Add(characters); Add(4, filters);
            var guide = new Expander { Header = "Crashes while friends pick characters through Parsec", Margin = new Thickness(0, 0, 0, 6),
                Content = new ScrollViewer { MaxHeight = 190, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new TextBlock { Text = CharacterSelectionGuide.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) } } };
            Add(5, guide);
            var state = new StackPanel(); status.Text = "Open this tab to scan enabled CAKs. No files are changed."; status.TextWrapping = TextWrapping.Wrap;
            status.Margin = new Thickness(0, 0, 0, 5); state.Children.Add(status); state.Children.Add(progress); Add(6, state);
            table.IsReadOnly = true; table.AutoGenerateColumns = false; table.CanUserAddRows = false; table.CanUserDeleteRows = false;
            table.SelectionMode = DataGridSelectionMode.Single; table.SelectionUnit = DataGridSelectionUnit.FullRow;
            table.Background = surface; table.Foreground = Foreground; table.RowBackground = surface; table.AlternatingRowBackground = surface;
            table.HeadersVisibility = DataGridHeadersVisibility.Column; table.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
            table.HorizontalGridLinesBrush = shell.TryFindResource("Line") as Brush ?? Brushes.DimGray;
            var headerStyle = new Style(typeof(DataGridColumnHeader));
            headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, surface));
            headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, Foreground));
            headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6)));
            table.ColumnHeaderStyle = headerStyle;
            table.Columns.Add(Column("Result", "Kind", 150));
            table.Columns.Add(Column("Internal file / archive", "File", 270));
            table.Columns.Add(Column("CAKs", "Archives", 170));
            table.Columns.Add(Column("Enabled / disabled", "Activity", 150));
            Add(7, table);
            details.IsReadOnly = true; details.AcceptsReturn = true; details.TextWrapping = TextWrapping.Wrap;
            details.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; details.Background = surface; details.Foreground = Foreground;
            details.Padding = new Thickness(8); details.Margin = new Thickness(0, 8, 0, 0);
            details.Text = "Select a result to inspect its evidence. A duplicate or shared path alone is not a confirmed crash cause."; Add(8, details);
            search.Background = surface; search.Foreground = Foreground;
            scan.Click += async (sender, args) => await ScanAsync();
            cancel.Click += (sender, args) => running?.Cancel();
            save.Click += (sender, args) => Save();
            notes.Click += (sender, args) => ShowNotes();
            kind.SelectionChanged += (sender, args) => Filter(); search.TextChanged += (sender, args) => Filter();
            characters.Checked += (sender, args) => Filter(); characters.Unchecked += (sender, args) => Filter();
            disabled.Click += (sender, args) => OptionsChanged(); large.Click += (sender, args) => OptionsChanged();
            table.SelectionChanged += (sender, args) => {
                if (table.SelectedItem is DuplicateRow row) details.Text = row.Kind + ": " + row.File + "\n" + row.Activity + "\n\n" + row.Details;
            };
            IsVisibleChanged += (sender, args) => { if (!IsVisible) running?.Cancel(); };

            void Add(int row, UIElement element) { Grid.SetRow(element, row); root.Children.Add(element); }
        }

        public void Refresh()
        {
            Places places = shell.Core?.Places;
            folders.Text = "Enabled: " + (places?.ModsDir ?? "not configured") + "\nDisabled store: " + (places?.OffDir ?? "not configured");
            if (Visibility == Visibility.Visible) navigation.IsChecked = true;
            if (last != null && (last.ModsFolder != places?.ModsDir || last.DisabledFolder != places?.OffDir))
                status.Text = "Folder settings changed. Scan again; displayed results belong to the previous folders.";
            if (!opened && Visibility == Visibility.Visible && places != null) { opened = true; _ = ScanAsync(); }
        }

        private async Task ScanAsync()
        {
            if (Loading) return;
            Places places = shell.Core?.Places;
            if (places == null) { status.Text = "The manager is still loading. Try Scan CAKs again."; return; }
            var snapshot = new Places { ModsDir = places.ModsDir, OffDir = places.OffDir };
            bool includeDisabled = disabled.IsChecked == true, compareLarge = large.IsChecked == true;
            using (var cancellation = new CancellationTokenSource())
            {
                running = cancellation; Loading = true; scan.IsEnabled = disabled.IsEnabled = large.IsEnabled = save.IsEnabled = false;
                cancel.IsEnabled = true; progress.Visibility = Visibility.Visible; status.Text = "Scanning CAKs… Previous results stay until this scan completes.";
                try
                {
                    var updates = new Progress<string>(message => { if (running == cancellation) status.Text = message; });
                    DuplicateScanResult completed = await Task.Run(() => DuplicateScan.Run(snapshot, includeDisabled, compareLarge, cancellation.Token, updates));
                    cancellation.Token.ThrowIfCancellationRequested();
                    last = completed; Filter(); status.Text = "Scanned " + completed.Created.ToString("yyyy-MM-dd HH:mm:ss") + ". " + completed.Summary +
                        " Use Scan notes for limits; scan again after mod changes.";
                    // Do not put the entire report into the detail view before
                    // selection; large result sets remain in the virtualized grid.
                    ShowNotes();
                    Refresh();
                }
                catch (OperationCanceledException) { status.Text = "Scan cancelled. No partial matches were published; any displayed results are from the previous completed scan."; }
                catch (Exception ex) { status.Text = "The scan could not finish: " + ex.Message + ". No validity or crash-cause claim was made."; }
                finally
                {
                    running = null; Loading = false; scan.IsEnabled = disabled.IsEnabled = large.IsEnabled = true;
                    cancel.IsEnabled = false; save.IsEnabled = notes.IsEnabled = last != null; progress.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void Filter()
        {
            if (last == null) return;
            var visible = last.Filter(kind.SelectedItem as string, search.Text, characters.IsChecked == true).ToList();
            table.ItemsSource = visible;
            table.SelectedIndex = visible.Count > 0 ? 0 : -1;
            if (visible.Count == 0) details.Text = "No results match these filters. Clear Search, select All results or turn off Character/roster paths. Inspect scan notes before drawing conclusions.";
        }

        private void OptionsChanged()
        {
            if (last != null) status.Text = "Scan options changed. Press Scan CAKs to apply them; displayed results are from the previous scan.";
        }

        private void ShowNotes()
        {
            if (last == null) return;
            table.SelectedIndex = -1;
            details.Text = "SCAN NOTES\n" + string.Join("\n\n", last.Notes) +
                (last.Rows.Count == 0 ? "\n\nNo matches were established within this scan's scope and limits." : "\n\nSelect a result above for exact locations and evidence.");
        }

        private void Save()
        {
            if (last == null) return;
            var dialog = new SaveFileDialog { FileName = "Mykelovves duplicate files.txt", Filter = "Text report (*.txt)|*.txt", DefaultExt = ".txt", AddExtension = true };
            try
            {
                if (dialog.ShowDialog(shell) != true) return;
                File.WriteAllText(dialog.FileName, last.FullText(), new UTF8Encoding(false));
                status.Text = "Full report saved locally. It includes all results, regardless of filters. Review personal paths before sharing.";
            }
            catch (Exception ex) { status.Text = "The report could not be saved: " + ex.Message; }
        }

        private static DataGridTextColumn Column(string title, string property, int width) =>
            new DataGridTextColumn { Header = title, Binding = new Binding(property), Width = new DataGridLength(width) };
    }
}
