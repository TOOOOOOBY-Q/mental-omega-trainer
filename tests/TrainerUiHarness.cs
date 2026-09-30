using System;
using System.Collections;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace MOTrainer336
{
    // Renders the real form without attaching to a game or registering hotkeys.
    internal static class TrainerUiHarness
    {
        private static int checks;
        private static int failures;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [STAThread]
        private static int Main(string[] args)
        {
            NativeMethods.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string output = Path.GetFullPath(args.Length > 0 ? args[0] : "release/ui-preview");
            Directory.CreateDirectory(output);
            foreach (float scale in new float[] { 1 })
            {
                using (TrainerForm form = new TrainerForm(true))
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-30000, -30000);
                    form.ShowInTaskbar = false;
                    form.Show();
                    Application.DoEvents();
                    // Test the production form exactly as it opens. Do not resize
                    // controls or normalize fonts: that hid a real DPI regression.
                    Assert(form.ClientSize == new Size(820, 620), "Client size must stay fixed after showing the form");
                    string suffix = "-fixed.png";
                    Assert(Field(form, "memory") == null, "Preview must never attach to a game");
                    Assert(!((Timer)Field(form, "refreshTimer")).Enabled && !((Timer)Field(form, "featureTimer")).Enabled, "Preview timers must remain stopped");
                    Assert(((IDictionary)Field(form, "hotkeyNames")).Count == 0, "Preview must not reserve global hotkeys");

                    Invoke(form, "UpdateConnection", false, false, null);
                    Invoke(form, "Log", "已启动：等待 gamemd.exe");
                    Assert(!Find(form, "MoneyButton").Enabled && !Find(form, "powerToggle").Enabled, "No game: actions cannot run");
                    Capture(form, Path.Combine(output, "waiting" + suffix));

                    Invoke(form, "ShowDisconnected", "找到游戏，但无法打开进程（错误 5）");
                    Assert(((Label)Field(form, "statusDetail")).Text.Contains("错误 5"), "Connection failure must remain visible");
                    int logCount = ((IList)Field(form, "logLines")).Count;
                    Invoke(form, "ShowDisconnected", "找到游戏，但无法打开进程（错误 5）");
                    Assert(((IList)Field(form, "logLines")).Count == logCount, "Repeated connection failure must not flood the log");
                    Capture(form, Path.Combine(output, "error" + suffix));
                    ((IList)Field(form, "logLines")).Clear();

                    Invoke(form, "UpdateConnection", true, true, @"C:\Program Files (x86)\Mental Omega\gamemd.exe");
                    Invoke(form, "Log", "已连接游戏；进入对局后，功能已就绪");
                    Assert(Find(form, "MoneyButton").Enabled && Find(form, "powerToggle").Enabled, "In match: actions become available");
                    foreach (string name in new string[] { "TopMostToggle", "MoneyAmount", "MoneyButton", "powerToggle", "buildToggle", "superToggle", "MapButton", "WinButton", "CopyDiagnostics", "Diagnostics" })
                    {
                        Control control = Find(form, name);
                        Assert(control.CanSelect && control.TabStop, name + " must be keyboard reachable");
                        Assert(!string.IsNullOrEmpty(control.AccessibleName), name + " must have an accessible name");
                    }
                    Capture(form, Path.Combine(output, "ready" + suffix));
                    VerifyLayout(form, scale);
                    string[] tabOrder = new string[] { "TopMostToggle", "MoneyAmount", "MoneyButton", "powerToggle", "buildToggle", "superToggle", "MapButton", "WinButton", "CopyDiagnostics", "Diagnostics" };
                    Control current = Find(form, tabOrder[0]);
                    current.Select();
                    for (int i = 1; i < tabOrder.Length; i++)
                    {
                        form.SelectNextControl(current, true, true, true, false);
                        Assert(form.ActiveControl == Find(form, tabOrder[i]), "Tab order should reach " + tabOrder[i]);
                        current = Find(form, tabOrder[i]);
                    }

                    Invoke(form, "SetAllToggles", true);
                    Invoke(form, "Log", "无限电力已开启；快速建造已开启，仍需足额资金；无限超级武器已开启");
                    Assert(((Label)Field(form, "activity")).Text.Contains("3 / 3"), "Footer must reflect active features");
                    Assert(Find(form, "powerToggle").Text == "已开启", "Enabled state needs text as well as color");
                    Find(form, "buildToggle").Focus();
                    Capture(form, Path.Combine(output, "active" + suffix));
                    Invoke(form, "UpdateConnection", true, false, null);
                    Assert(!Find(form, "MoneyButton").Enabled && Find(form, "buildToggle").Enabled, "Outside match: active overrides can still be switched off");
                    Invoke(form, "SetAllToggles", false);
                    Invoke(form, "UpdateAvailability", false);
                    Assert(!Find(form, "buildToggle").Enabled, "Outside match: inactive overrides cannot be enabled");
                    Assert(((Label)Field(form, "activity")).Text.Contains("0 / 3"), "Footer resets after disabling features");

                    // Simulate a desktop whose work area is shorter than the form.
                    form.Height -= (int)(160 * scale);
                    Application.DoEvents();
                    Panel viewport = (Panel)form.Controls[0];
                    Assert(viewport.VerticalScroll.Visible, "Short desktop must provide vertical scrolling");
                    viewport.ScrollControlIntoView(Find(form, "CopyDiagnostics"));
                    Capture(form, Path.Combine(output, "compact" + suffix));
                    form.Close();
                }
            }
            Console.WriteLine("UI checks: " + (checks - failures) + "/" + checks + " passed. Previews: " + output);
            return failures == 0 ? 0 : 1;
        }

        private static object Field(object owner, string name) { return owner.GetType().GetField(name, Private).GetValue(owner); }
        private static void Invoke(object owner, string method, params object[] args) { owner.GetType().GetMethod(method, Private).Invoke(owner, args); }
        private static Control Find(Control root, string name)
        {
            Control[] found = root.Controls.Find(name, true);
            if (found.Length != 1) throw new Exception("Expected exactly one control: " + name);
            return found[0];
        }
        private static void Assert(bool result, string message)
        {
            checks++;
            if (result) return;
            failures++;
            Console.Error.WriteLine("FAIL: " + message);
        }
        private static void Capture(Form form, string path)
        {
            form.Refresh();
            Application.DoEvents();
            using (Bitmap image = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                image.Save(path, ImageFormat.Png);
            }
        }
        private static void VerifyLayout(Control parent, float scale)
        {
            foreach (Control control in parent.Controls)
            {
                if (control is Label || control is Button || control is CheckBox || control is NumericUpDown)
                {
                    Rectangle available = parent.ClientRectangle;
                    available.Inflate(2, 2); // WinForms DPI rounding can differ by one pixel.
                    Assert(available.Contains(control.Bounds), "Clipped " + control.GetType().Name + " '" + control.Text + "' " + control.Bounds + " in " + parent.ClientRectangle + " at " + scale);
                    Label label = control as Label;
                    if (label != null && !label.AutoEllipsis)
                    {
                        Size text = TextRenderer.MeasureText(label.Text, label.Font, new Size(5000, 5000), TextFormatFlags.SingleLine);
                        Assert(text.Width <= label.Width + 2 && text.Height <= label.Height + 2, "Text does not fit '" + label.Text + "': " + text + " vs " + label.Size + " at " + scale);
                    }
                    UiButton button = control as UiButton;
                    if (button != null)
                    {
                        Size text = TextRenderer.MeasureText(button.Text, button.Font, new Size(5000, 5000), TextFormatFlags.SingleLine);
                        Assert(text.Width <= button.Width - 8 && text.Height <= button.Height - 4, "Button text must fit: " + button.Text);
                    }
                    // Font height is evaluated in real graphics contexts with
                    // different DPI values. Pixel fonts must remain the same size.
                    using (Bitmap bitmap = new Bitmap(20, 20))
                    {
                        float first = 0;
                        foreach (float dpi in new float[] { 96, 144, 192 })
                        {
                            bitmap.SetResolution(dpi, dpi);
                            using (Graphics graphics = Graphics.FromImage(bitmap))
                            {
                                float height = control.Font.GetHeight(graphics);
                                if (first == 0) first = height;
                                Assert(Math.Abs(height - first) < .1f, "Text size must not grow with DPI: " + control.Text + " at " + dpi);
                            }
                        }
                    }
                }
                if (!(control is NumericUpDown) && !(control is TextBox)) VerifyLayout(control, scale);
            }
        }
    }
}
