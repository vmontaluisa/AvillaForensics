// Image Finder - BETA
// Este archivo no venia en el repositorio publicado (solo estaba Form1.designer.cs).
// Se reconstruyo para que el proyecto compile, implementando las funciones que
// muestra la interfaz: busqueda de imagenes, miniaturas, hash, EXIF, geolocalizacion,
// y exportacion a KML / HTML.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Analisador_Arquivos_DEIC_SBC
{
    public partial class Form1 : Form
    {
        private const int PageSize = 100;

        private readonly List<string> files = new List<string>();
        private int page;
        private string extension = "jpg";
        private double? currentLat;
        private double? currentLon;

        public Form1()
        {
            InitializeComponent();
        }

        private int PageCount => Math.Max(1, (files.Count + PageSize - 1) / PageSize);

        private void Form1_Load(object sender, EventArgs e)
        {
            if (comboBox1.Items.Count > 0)
                comboBox1.SelectedIndex = 0;
            button2.Enabled = true;
            label1.Text = "";
            label6.Text = "";
            listView1.View = View.LargeIcon;
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            extension = comboBox1.SelectedItem?.ToString() ?? "jpg";
            button3.Enabled = Directory.Exists(textBox2.Text);
        }

        // Elegir directorio de origen
        private void button2_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    textBox2.Text = dialog.SelectedPath;
                    button3.Enabled = true;
                }
            }
        }

        private void button9_Click(object sender, EventArgs e) => button2_Click(sender, e);

        // Buscar
        private void button3_Click(object sender, EventArgs e)
        {
            if (!Directory.Exists(textBox2.Text) || backgroundWorker2.IsBusy)
                return;

            SetBusy(true, "Searching files, please wait.");
            listBox1.Items.Clear();
            listView1.Items.Clear();
            ClearImageList();
            backgroundWorker2.RunWorkerAsync(new[] { textBox2.Text, extension });
        }

        private void backgroundWorker2_DoWork(object sender, DoWorkEventArgs e)
        {
            var args = (string[])e.Argument;
            e.Result = FindFiles(args[0], "." + args[1]);
        }

        private void backgroundWorker2_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            SetBusy(false, null);
            if (e.Error != null)
            {
                MessageBox.Show(this, e.Error.Message, "Image Finder", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            files.Clear();
            files.AddRange((List<string>)e.Result);
            listBox1.BeginUpdate();
            foreach (var f in files)
                listBox1.Items.Add(f);
            listBox1.EndUpdate();

            label1.Text = files.Count + " files ." + extension;
            button8.Enabled = files.Count > 0;
            button10.Enabled = files.Count > 0;
            page = 0;
            LoadPage();
        }

        private static List<string> FindFiles(string root, string ext)
        {
            var result = new List<string>();
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var dir = pending.Pop();
                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir))
                        if (string.Equals(Path.GetExtension(f), ext, StringComparison.OrdinalIgnoreCase))
                            result.Add(f);
                    foreach (var d in Directory.EnumerateDirectories(dir))
                        pending.Push(d);
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        // Miniaturas (paginadas)
        private void button1_Click(object sender, EventArgs e) => LoadPage();

        private void LoadPage()
        {
            listView1.Items.Clear();
            ClearImageList();
            label6.Text = files.Count == 0 ? "" : $"{page + 1} / {PageCount}";
            button5.Enabled = page > 0;
            button4.Enabled = page < PageCount - 1;
            button6.Enabled = button4.Enabled;

            var slice = files.Skip(page * PageSize).Take(PageSize).ToList();
            if (slice.Count == 0)
                return;

            if (!checkBoxMiniaturas.Checked)
            {
                foreach (var f in slice)
                    listView1.Items.Add(new ListViewItem(Path.GetFileName(f)) { Tag = f });
                return;
            }

            if (backgroundWorker1.IsBusy)
                return;
            SetBusy(true, "Loading thumbnails, please wait.");
            backgroundWorker1.RunWorkerAsync(new object[] { slice, imageList1.ImageSize });
        }

        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {
            var args = (object[])e.Argument;
            var slice = (List<string>)args[0];
            var size = (Size)args[1];
            var thumbs = new List<KeyValuePair<string, Image>>();
            for (int i = 0; i < slice.Count; i++)
            {
                thumbs.Add(new KeyValuePair<string, Image>(slice[i], MakeThumbnail(slice[i], size)));
                backgroundWorker1.ReportProgress((i + 1) * 100 / slice.Count);
            }
            e.Result = thumbs;
        }

        private void backgroundWorker1_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            label11.Text = $"Loading thumbnails... {e.ProgressPercentage}%";
        }

        private void backgroundWorker1_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            SetBusy(false, null);
            if (e.Error != null || e.Result == null)
                return;

            listView1.BeginUpdate();
            foreach (var kv in (List<KeyValuePair<string, Image>>)e.Result)
            {
                imageList1.Images.Add(kv.Key, kv.Value);
                listView1.Items.Add(new ListViewItem(Path.GetFileName(kv.Key), kv.Key) { Tag = kv.Key });
            }
            listView1.EndUpdate();
        }

        private static Image MakeThumbnail(string path, Size size)
        {
            var thumb = new Bitmap(size.Width, size.Height);
            using (var g = Graphics.FromImage(thumb))
            {
                g.Clear(Color.White);
                try
                {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var img = Image.FromStream(fs, false, false))
                    {
                        double scale = Math.Min((double)size.Width / img.Width, (double)size.Height / img.Height);
                        int w = Math.Max(1, (int)(img.Width * scale));
                        int h = Math.Max(1, (int)(img.Height * scale));
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(img, (size.Width - w) / 2, (size.Height - h) / 2, w, h);
                    }
                }
                catch
                {
                    // Formato no soportado por GDI+ (por ejemplo HEIC/WEBP/PSD) o archivo danado
                    using (var font = new Font("Segoe UI", 8))
                        g.DrawString("No preview\n" + Path.GetExtension(path), font, Brushes.Gray,
                            new RectangleF(0, 0, size.Width, size.Height),
                            new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                }
            }
            return thumb;
        }

        private void ClearImageList()
        {
            var old = imageList1.Images.Cast<Image>().ToList();
            imageList1.Images.Clear();
            foreach (var img in old)
                img.Dispose();
        }

        // Siguiente / anterior
        private void button4_Click(object sender, EventArgs e)
        {
            if (page < PageCount - 1) { page++; LoadPage(); }
        }

        private void button6_Click(object sender, EventArgs e) => button4_Click(sender, e);

        private void button5_Click(object sender, EventArgs e)
        {
            if (page > 0) { page--; LoadPage(); }
        }

        // Seleccion de archivo
        private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listBox1.SelectedItem is string path)
                ShowFile(path);
        }

        private void listView1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listView1.SelectedItems.Count > 0 && listView1.SelectedItems[0].Tag is string path)
                ShowFile(path);
        }

        private void ShowFile(string path)
        {
            textBox1.Text = path;
            Txthash256.Text = "";
            Txthashmd5.Text = "";
            TXTMeta.Text = "";
            TxtGEO.Text = "";
            currentLat = currentLon = null;

            try
            {
                Txthash256.Text = ComputeHash(SHA256.Create(), path);
                Txthashmd5.Text = ComputeHash(MD5.Create(), path);
            }
            catch (Exception ex)
            {
                Txthash256.Text = ex.Message;
            }

            try { webBrowser2.Navigate(new Uri(path)); } catch { }

            if (!checkBoxMeta.Checked && !checkBoxGEO.Checked)
                return;

            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var img = Image.FromStream(fs, false, false))
                {
                    if (checkBoxMeta.Checked)
                        TXTMeta.Text = DescribeExif(img);

                    if (checkBoxGEO.Checked && TryGetGps(img, out double lat, out double lon))
                    {
                        currentLat = lat;
                        currentLon = lon;
                        string coords = FormatCoords(lat, lon);
                        TxtGEO.Text = "Latitude: " + lat.ToString(CultureInfo.InvariantCulture) + Environment.NewLine +
                                      "Longitude: " + lon.ToString(CultureInfo.InvariantCulture) + Environment.NewLine +
                                      "https://www.google.com/maps?q=" + coords;
                        try { webBrowser1.Navigate("https://www.openstreetmap.org/?mlat=" + lat.ToString(CultureInfo.InvariantCulture) + "&mlon=" + lon.ToString(CultureInfo.InvariantCulture) + "#map=17/" + lat.ToString(CultureInfo.InvariantCulture) + "/" + lon.ToString(CultureInfo.InvariantCulture)); } catch { }
                    }
                    else if (checkBoxGEO.Checked)
                    {
                        TxtGEO.Text = "No GPS data.";
                    }
                }
            }
            catch (Exception ex)
            {
                TXTMeta.Text = "Could not read metadata: " + ex.Message;
            }
        }

        private static string ComputeHash(HashAlgorithm algorithm, string path)
        {
            using (algorithm)
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                return BitConverter.ToString(algorithm.ComputeHash(fs)).Replace("-", "");
        }

        // EXIF
        private static readonly Dictionary<int, string> ExifNames = new Dictionary<int, string>
        {
            { 0x010F, "Make" }, { 0x0110, "Model" }, { 0x0112, "Orientation" }, { 0x0131, "Software" },
            { 0x0132, "DateTime" }, { 0x013B, "Artist" }, { 0x8298, "Copyright" },
            { 0x829A, "ExposureTime" }, { 0x829D, "FNumber" }, { 0x8827, "ISOSpeedRatings" },
            { 0x9003, "DateTimeOriginal" }, { 0x9004, "DateTimeDigitized" }, { 0x920A, "FocalLength" },
            { 0x9209, "Flash" }, { 0xA002, "PixelXDimension" }, { 0xA003, "PixelYDimension" },
            { 0xA434, "LensModel" }, { 0x0001, "GPSLatitudeRef" }, { 0x0002, "GPSLatitude" },
            { 0x0003, "GPSLongitudeRef" }, { 0x0004, "GPSLongitude" }, { 0x0006, "GPSAltitude" },
            { 0x0007, "GPSTimeStamp" }, { 0x001D, "GPSDateStamp" },
        };

        private static string DescribeExif(Image img)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Width x Height: {img.Width} x {img.Height}");
            sb.AppendLine($"Pixel format: {img.PixelFormat}");
            sb.AppendLine();
            foreach (var p in img.PropertyItems.OrderBy(p => p.Id))
            {
                string name = ExifNames.TryGetValue(p.Id, out var n) ? n : "0x" + p.Id.ToString("X4");
                sb.AppendLine(name + ": " + FormatProperty(p));
            }
            if (img.PropertyItems.Length == 0)
                sb.AppendLine("No EXIF data.");
            return sb.ToString();
        }

        private static string FormatProperty(PropertyItem p)
        {
            try
            {
                switch (p.Type)
                {
                    case 2: // ASCII
                        return Encoding.ASCII.GetString(p.Value).TrimEnd('\0');
                    case 3: // SHORT
                        return string.Join(", ", Enumerable.Range(0, p.Len / 2).Select(i => BitConverter.ToUInt16(p.Value, i * 2)));
                    case 4: // LONG
                        return string.Join(", ", Enumerable.Range(0, p.Len / 4).Select(i => BitConverter.ToUInt32(p.Value, i * 4)));
                    case 5: // RATIONAL
                    case 10: // SRATIONAL
                        return string.Join(", ", Enumerable.Range(0, p.Len / 8).Select(i => Rational(p.Value, i).ToString("0.######", CultureInfo.InvariantCulture)));
                    default:
                        return p.Len <= 32 ? BitConverter.ToString(p.Value) : $"({p.Len} bytes)";
                }
            }
            catch
            {
                return "?";
            }
        }

        private static double Rational(byte[] value, int index)
        {
            uint num = BitConverter.ToUInt32(value, index * 8);
            uint den = BitConverter.ToUInt32(value, index * 8 + 4);
            return den == 0 ? 0 : (double)num / den;
        }

        private static bool TryGetGps(Image img, out double lat, out double lon)
        {
            lat = lon = 0;
            var ids = img.PropertyIdList;
            if (!ids.Contains(0x0002) || !ids.Contains(0x0004))
                return false;
            try
            {
                lat = Dms(img.GetPropertyItem(0x0002).Value);
                lon = Dms(img.GetPropertyItem(0x0004).Value);
                if (ids.Contains(0x0001) && Encoding.ASCII.GetString(img.GetPropertyItem(0x0001).Value).StartsWith("S"))
                    lat = -lat;
                if (ids.Contains(0x0003) && Encoding.ASCII.GetString(img.GetPropertyItem(0x0003).Value).StartsWith("W"))
                    lon = -lon;
                return !(lat == 0 && lon == 0);
            }
            catch
            {
                return false;
            }
        }

        private static double Dms(byte[] value) =>
            Rational(value, 0) + Rational(value, 1) / 60.0 + Rational(value, 2) / 3600.0;

        private static string FormatCoords(double lat, double lon) =>
            lat.ToString(CultureInfo.InvariantCulture) + "," + lon.ToString(CultureInfo.InvariantCulture);

        private static bool TryReadGps(string path, out double lat, out double lon)
        {
            lat = lon = 0;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var img = Image.FromStream(fs, false, false))
                    return TryGetGps(img, out lat, out lon);
            }
            catch
            {
                return false;
            }
        }

        // Google Earth
        private void button7_Click(object sender, EventArgs e)
        {
            if (currentLat == null || currentLon == null)
            {
                MessageBox.Show(this, "The selected image has no GPS data.", "Image Finder");
                return;
            }
            Process.Start(new ProcessStartInfo("https://earth.google.com/web/search/" + FormatCoords(currentLat.Value, currentLon.Value)) { UseShellExecute = true });
        }

        // Generar KML
        private async void button8_Click(object sender, EventArgs e)
        {
            string target = AskSavePath("KML (*.kml)|*.kml", "images.kml");
            if (target == null)
                return;

            var list = files.ToList();
            SetBusy(true, "Generating file, this process may take some time, please wait.");
            int count = 0;
            try
            {
                count = await Task.Run(() =>
                {
                    int n = 0;
                    var sb = new StringBuilder();
                    sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                    sb.AppendLine("<kml xmlns=\"http://www.opengis.net/kml/2.2\"><Document>");
                    foreach (var f in list)
                    {
                        if (!TryReadGps(f, out double lat, out double lon))
                            continue;
                        n++;
                        string uri = new Uri(f).AbsoluteUri;
                        sb.AppendLine("<Placemark>");
                        sb.AppendLine("<name>" + WebUtility.HtmlEncode(Path.GetFileName(f)) + "</name>");
                        sb.AppendLine("<description><![CDATA[<img src=\"" + uri + "\" width=\"300\"/><br/>" + WebUtility.HtmlEncode(f) + "]]></description>");
                        sb.AppendLine("<Point><coordinates>" + lon.ToString(CultureInfo.InvariantCulture) + "," + lat.ToString(CultureInfo.InvariantCulture) + ",0</coordinates></Point>");
                        sb.AppendLine("</Placemark>");
                    }
                    sb.AppendLine("</Document></kml>");
                    File.WriteAllText(target, sb.ToString(), Encoding.UTF8);
                    return n;
                });
            }
            catch (Exception ex)
            {
                SetBusy(false, null);
                MessageBox.Show(this, ex.Message, "Image Finder", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            SetBusy(false, null);
            MessageBox.Show(this, $"KML generated with {count} geolocated images:\n{target}", "Image Finder");
        }

        // Generar HTML
        private async void button10_Click(object sender, EventArgs e)
        {
            string target = AskSavePath("HTML (*.html)|*.html", "images.html");
            if (target == null)
                return;

            var list = files.ToList();
            SetBusy(true, "Generating file, this process may take some time, please wait.");
            try
            {
                await Task.Run(() =>
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Image Finder - Avilla Forensics</title>");
                    sb.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:16px}table{border-collapse:collapse;width:100%}td,th{border:1px solid #ccc;padding:6px;vertical-align:top;font-size:12px;word-break:break-all}img{max-width:220px;max-height:220px}</style></head><body>");
                    sb.AppendLine("<h1>Image Finder</h1><p>Source: " + WebUtility.HtmlEncode(textBox2.Text) + "<br>Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "<br>Files: " + list.Count + "</p>");
                    sb.AppendLine("<table><tr><th>#</th><th>Image</th><th>File</th><th>SHA-256</th><th>MD5</th><th>GPS</th></tr>");
                    int i = 0;
                    foreach (var f in list)
                    {
                        i++;
                        string sha = "", md5 = "", gps = "";
                        try { sha = ComputeHash(SHA256.Create(), f); md5 = ComputeHash(MD5.Create(), f); } catch { }
                        if (TryReadGps(f, out double lat, out double lon))
                        {
                            string c = FormatCoords(lat, lon);
                            gps = "<a href=\"https://www.google.com/maps?q=" + c + "\">" + c + "</a>";
                        }
                        string uri = new Uri(f).AbsoluteUri;
                        sb.AppendLine("<tr><td>" + i + "</td><td><a href=\"" + uri + "\"><img loading=\"lazy\" src=\"" + uri + "\"></a></td><td>" +
                                      WebUtility.HtmlEncode(f) + "</td><td>" + sha + "</td><td>" + md5 + "</td><td>" + gps + "</td></tr>");
                    }
                    sb.AppendLine("</table></body></html>");
                    File.WriteAllText(target, sb.ToString(), Encoding.UTF8);
                });
            }
            catch (Exception ex)
            {
                SetBusy(false, null);
                MessageBox.Show(this, ex.Message, "Image Finder", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            SetBusy(false, null);
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }

        private string AskSavePath(string filter, string fileName)
        {
            using (var dialog = new SaveFileDialog { Filter = filter, FileName = fileName })
                return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
        }

        private void SetBusy(bool busy, string message)
        {
            if (message != null)
                label11.Text = message;
            panelProcesso.Visible = busy;
            if (busy)
                panelProcesso.BringToFront();
            button3.Enabled = !busy && Directory.Exists(textBox2.Text);
            button2.Enabled = !busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            timer1.Stop();
        }
    }
}
