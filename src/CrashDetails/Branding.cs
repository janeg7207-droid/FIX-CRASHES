using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Gorilla.CrashDetails
{
    // Keep the original assembly/resource identity and data paths so saved
    // settings, BAML references and the original updater remain compatible.
    public static class Branding
    {
        public static void Apply(Window window)
        {
            window.Title = "Mykelovves Mod Manager";
            // The source window's content is constructed by InitializeComponent
            // before this hook runs. Replace its wordmark with accessible text.
            if (window.Content is DependencyObject content) Visit(content);
            if (window.FindName("VersionText") is TextBlock version)
                version.Text += " · Mykelovves r4";
        }

        private static void Visit(DependencyObject element)
        {
            // The fixed v2.0.4 header puts the logo and this caption in the
            // same panel. This works for both BitmapImage and BitmapFrame sources.
            if (element is TextBlock text && text.Text == "MOD MANAGER 26" && text.Parent is Panel parent)
            {
                text.Text = "MOD MANAGER";
                Image image = parent.Children.OfType<Image>().FirstOrDefault();
                if (image != null)
                {
                    int index = parent.Children.IndexOf(image);
                    var name = new TextBlock
                    {
                        Text = "MYKELOVVES", FontSize = 20, FontWeight = FontWeights.Bold,
                        Foreground = image.TryFindResource("Text") as Brush ?? Brushes.White,
                        VerticalAlignment = VerticalAlignment.Center, Margin = image.Margin,
                        ToolTip = "Mykelovves Mod Manager — based on Gorilla Mod Manager 26 by Mr Creeper HD"
                    };
                    parent.Children.RemoveAt(index);
                    parent.Children.Insert(index, name);
                }
            }
            // Copy children before walking: replacing the wordmark changes its
            // parent's collection. Author credits and external links are retained.
            foreach (DependencyObject child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>().ToList())
                Visit(child);
        }
    }
}
