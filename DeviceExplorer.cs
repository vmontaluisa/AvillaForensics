// Device Explorer
// Este formulario no venia en el repositorio publicado (MDIParent1 lo usa pero no
// existia su codigo). Se reconstruyo como un explorador de archivos del telefono
// via ADB: navegar carpetas y copiar archivos/carpetas al PC con "adb pull".
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Analisador_Arquivos_DEIC_SBC
{
    public class DeviceExplorer : Form
    {
        private readonly TextBox txtPath = new TextBox();
        private readonly Button btnGo = new Button { Text = "Go" };
        private readonly Button btnUp = new Button { Text = "Up" };
        private readonly Button btnPull = new Button { Text = "Copy to PC" };
        private readonly ListView list = new ListView();
        private readonly Label status = new Label();

        public DeviceExplorer()
        {
            Text = "Device Explorer";
            Size = new Size(900, 600);
            StartPosition = FormStartPosition.CenterParent;

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(4), WrapContents = false };
            txtPath.Width = 560;
            txtPath.Text = "/sdcard/";
            top.Controls.AddRange(new Control[] { new Label { Text = "Path:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, txtPath, btnGo, btnUp, btnPull });

            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.Columns.Add("Name", 380);
            list.Columns.Add("Type", 80);
            list.Columns.Add("Size", 110);
            list.Columns.Add("Modified", 160);

            status.Dock = DockStyle.Bottom;
            status.Height = 22;

            Controls.Add(list);
            Controls.Add(top);
            Controls.Add(status);

            btnGo.Click += async (s, e) => await LoadDir(txtPath.Text);
            txtPath.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await LoadDir(txtPath.Text); } };
            btnUp.Click += async (s, e) => await LoadDir(ParentDir(txtPath.Text));
            btnPull.Click += async (s, e) => await Pull();
            list.DoubleClick += async (s, e) =>
            {
                if (list.SelectedItems.Count == 1 && (string)list.SelectedItems[0].SubItems[1].Text == "Folder")
                    await LoadDir(Combine(txtPath.Text, list.SelectedItems[0].Text));
            };
            Load += async (s, e) => await LoadDir(txtPath.Text);
        }

        private static string AdbPath()
        {
            string local = Path.GetFullPath(Path.Combine("adb", "adb.exe"));
            return File.Exists(local) ? local : "adb";
        }

        private static string Combine(string dir, string name) => dir.TrimEnd('/') + "/" + name;

        private static string ParentDir(string dir)
        {
            dir = dir.TrimEnd('/');
            int i = dir.LastIndexOf('/');
            return i <= 0 ? "/" : dir.Substring(0, i) + "/";
        }

        private static Task<(int code, string output)> RunAdb(string args)
        {
            return Task.Run(() =>
            {
                var psi = new ProcessStartInfo(AdbPath(), args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                };
                using (var p = Process.Start(psi))
                {
                    var err = p.StandardError.ReadToEndAsync();
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    return (p.ExitCode, output + err.Result);
                }
            });
        }

        private static string Quote(string path) => "\"" + path.Replace("\"", "\\\"") + "\"";

        private async Task LoadDir(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir))
                dir = "/";
            if (!dir.EndsWith("/"))
                dir += "/";
            status.Text = "Listing " + dir + " ...";
            UseWaitCursor = true;
            try
            {
                // Comillas simples para el shell del telefono
                var (code, output) = await RunAdb("shell ls -la '" + dir.Replace("'", "'\\''") + "'");
                list.BeginUpdate();
                list.Items.Clear();
                foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var item = ParseLsLine(line);
                    if (item != null)
                        list.Items.Add(item);
                }
                list.EndUpdate();
                txtPath.Text = dir;
                status.Text = code == 0 ? list.Items.Count + " items" : output.Trim();
            }
            catch (Exception ex)
            {
                status.Text = "ADB error: " + ex.Message + " (adb.exe must be in the \"adb\" folder or in PATH)";
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        // Formato toybox: perms links owner group size date time name
        private static ListViewItem ParseLsLine(string line)
        {
            if (line.StartsWith("total"))
                return null;
            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 8 || parts[0].Length < 10)
                return null;
            string name = string.Join(" ", parts.Skip(7));
            char t = parts[0][0];
            string type = t == 'd' ? "Folder" : t == 'l' ? "Link" : "File";
            if (t == 'l')
            {
                int arrow = name.IndexOf(" -> ", StringComparison.Ordinal);
                if (arrow > 0) name = name.Substring(0, arrow);
            }
            if (name == "." || name == "..")
                return null;
            var item = new ListViewItem(name);
            item.SubItems.Add(type);
            item.SubItems.Add(type == "File" ? parts[4] : "");
            item.SubItems.Add(parts[5] + " " + parts[6]);
            return item;
        }

        private async Task Pull()
        {
            if (list.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "Select one or more files or folders.", Text);
                return;
            }
            string target;
            using (var dialog = new FolderBrowserDialog { Description = "Destination folder on the PC" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                target = dialog.SelectedPath;
            }

            var names = list.SelectedItems.Cast<ListViewItem>().Select(i => i.Text).ToList();
            var errors = new List<string>();
            UseWaitCursor = true;
            int done = 0;
            foreach (var name in names)
            {
                status.Text = $"Copying {name} ({++done}/{names.Count}) ...";
                var (code, output) = await RunAdb("pull -a " + Quote(Combine(txtPath.Text, name)) + " " + Quote(target));
                if (code != 0)
                    errors.Add(name + ": " + output.Trim());
            }
            UseWaitCursor = false;
            status.Text = errors.Count == 0 ? $"Copied {names.Count} item(s) to {target}" : $"{errors.Count} error(s)";
            if (errors.Count > 0)
                MessageBox.Show(this, string.Join(Environment.NewLine, errors), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
