using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Gorilla.CrashDetails;

namespace Gorilla.CakInspectorApp
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new InspectorWindow(args.FirstOrDefault()));
        }
    }

    internal sealed class InspectorWindow : Form
    {
        private readonly RichTextBox output = new RichTextBox();
        private readonly Button choose = new Button { Text = "Choose .cak…", AutoSize = true };
        private readonly Button save = new Button { Text = "Save report…", AutoSize = true, Enabled = false };
        private bool busy;

        public InspectorWindow(string firstFile)
        {
            Text = "Gorilla CAK Inspector — read-only inspection";
            ClientSize = new Size(930, 680);
            MinimumSize = new Size(640, 440);
            Font = new Font("Segoe UI", 10);
            StartPosition = FormStartPosition.CenterScreen;
            AllowDrop = true;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var intro = new Label { AutoSize = true, MaximumSize = new Size(860, 0), Text = "Choose a CAK or drop one here. This reads supported archive tables and checks payload bounds. It does not change the file or guarantee game compatibility.", Margin = new Padding(0, 0, 0, 12) };
            output.Dock = DockStyle.Fill;
            output.ReadOnly = true;
            output.DetectUrls = false;
            output.Font = new Font("Consolas", 10);
            output.Text = "No file inspected.\n\nFor installed mods, use Fix My Game > Check Again in Gorilla Mod Manager to also compare asset IDs across enabled CAKs and read crash records.\n\nThis window inspects one chosen file, even if you have not installed it. Encrypted or unsupported formats are marked as limited inspection. Compressed asset contents are not decoded.";
            var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0) };
            actions.Controls.Add(choose); actions.Controls.Add(save);
            layout.Controls.Add(intro, 0, 0); layout.Controls.Add(output, 0, 1); layout.Controls.Add(actions, 0, 2);
            Controls.Add(layout);
            choose.Click += delegate
            {
                using (var picker = new OpenFileDialog { Filter = "CAK archives (*.cak)|*.cak|All files (*.*)|*.*", CheckFileExists = true, Multiselect = false })
                    if (picker.ShowDialog(this) == DialogResult.OK) Inspect(picker.FileName);
            };
            save.Click += delegate
            {
                using (var picker = new SaveFileDialog { Filter = "Text reports (*.txt)|*.txt", FileName = "Gorilla CAK inspection.txt", OverwritePrompt = true })
                    if (picker.ShowDialog(this) == DialogResult.OK)
                        try { File.WriteAllText(picker.FileName, "GORILLA CAK INSPECTION\r\nSaved locally; review personal paths before sharing.\r\n\r\n" + output.Text, new UTF8Encoding(false)); }
                        catch (Exception ex) { MessageBox.Show(this, "The report could not be saved: " + ex.Message, "Save report", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            DragEnter += (sender, e) => { if (!busy && e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragDrop += (sender, e) =>
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (!busy && files != null && files.Length > 0) Inspect(files[0]);
            };
            if (!string.IsNullOrWhiteSpace(firstFile)) Shown += delegate { Inspect(firstFile); };
        }

        private async void Inspect(string path)
        {
            if (busy) return;
            busy = true; choose.Enabled = false; save.Enabled = false;
            output.Text = "Reading CAK archive tables…\n" + path;
            try
            {
                CakInspection result = await Task.Run(() => CakInspector.Inspect(path));
                if (!IsDisposed) { output.Text = result.Text(); save.Enabled = true; }
            }
            catch (Exception ex)
            {
                if (!IsDisposed) output.Text = "The inspection could not finish. No file validity is claimed.\n\n" + ex.GetType().Name + ": " + ex.Message;
            }
            finally { busy = false; if (!IsDisposed) choose.Enabled = true; }
        }
    }
}
