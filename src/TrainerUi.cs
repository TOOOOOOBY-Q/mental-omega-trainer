using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MOTrainer336
{
    internal sealed partial class TrainerForm
    {
        private readonly bool previewOnly;
        private readonly ToolTip uiTips = new ToolTip();
        private readonly Color border = Color.FromArgb(52, 60, 72);
        private readonly Color input = Color.FromArgb(17, 23, 31);
        private readonly Color danger = Color.FromArgb(241, 151, 151);
        private Label connectionBadge;
        private UiButton moneyButton;
        private UiButton mapButton;
        private UiButton winButton;
        private TableLayoutPanel rootLayout;
        private string lastConnectionError;

        internal TrainerForm() : this(false) { }

        protected override bool ShowWithoutActivation { get { return previewOnly; } }

        // UI rendering/tests use the exact production form without process
        // discovery, hotkey registration or background game operations.
        internal TrainerForm(bool previewOnly)
        {
            this.previewOnly = previewOnly;
            Text = "心灵终结 · 单机修改器";
            // Keep controls and text in physical pixels. Point-sized fonts can
            // grow with the desktop DPI even when the fixed bounds do not.
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(820, 620);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = background;
            ForeColor = textPrimary;
            Font = UiFonts.Create(10.5f, FontStyle.Regular);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            DoubleBuffered = true;
            Padding = new Padding(24, 20, 24, 20);
            BuildInterface();
            UpdateAvailability(false);
            uiTips.AutoPopDelay = 15000;
            refreshTimer.Interval = 250;
            refreshTimer.Tick += OnRefresh;
            featureTimer.Interval = 15;
            featureTimer.Tick += OnFeatureTick;
            if (!previewOnly)
            {
                refreshTimer.Start();
                featureTimer.Start();
            }
            Shown += OnShown;
            FormClosing += OnClosing;
            Disposed += delegate { refreshTimer.Dispose(); featureTimer.Dispose(); uiTips.Dispose(); };
        }

        private void BuildInterface()
        {
            Panel viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = background };
            Controls.Add(viewport);
            rootLayout = new TableLayoutPanel {
                Dock = DockStyle.Top, Height = 580, ColumnCount = 1, RowCount = 7,
                BackColor = background, Margin = Padding.Empty, Padding = Padding.Empty
            };
            rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int height in new int[] { 72, 76, 88, 124, 76, 114, 30 })
                rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            viewport.Controls.Add(rootLayout);
            rootLayout.Controls.Add(BuildHeader(), 0, 0);
            rootLayout.Controls.Add(BuildStatusCard(), 0, 1);
            rootLayout.Controls.Add(BuildMoneyCard(), 0, 2);
            rootLayout.Controls.Add(BuildToggleArea(), 0, 3);
            rootLayout.Controls.Add(BuildActionCard(), 0, 4);
            rootLayout.Controls.Add(BuildDiagnosticsCard(), 0, 5);
            rootLayout.Controls.Add(BuildFooter(), 0, 6);
        }

        private Control BuildHeader()
        {
            Panel panel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Size = new Size(772, 72) };
            UiGlyph mark = new UiGlyph("omega", accent) { Location = new Point(0, 6), Size = new Size(48, 48) };
            panel.Controls.Add(mark);
            Label eyebrow = NewLabel("MENTAL OMEGA  /  3.3.6", 9, FontStyle.Bold, accent);
            eyebrow.SetBounds(64, 0, 360, 19);
            panel.Controls.Add(eyebrow);
            Label title = NewLabel("心灵终结 · 单机修改器", 21, FontStyle.Bold, textPrimary);
            title.SetBounds(61, 21, 470, 37);
            panel.Controls.Add(title);
            topMostToggle = new FeatureSwitch {
                Name = "TopMostToggle", Text = "窗口置顶", StateText = false,
                AccessibleName = "窗口置顶", Size = new Size(140, 36),
                BackColor = background, ForeColor = textSecondary,
                Location = new Point(620, 14), Anchor = AnchorStyles.Top | AnchorStyles.Right,
                TabIndex = 0
            };
            topMostToggle.CheckedChanged += delegate { TopMost = topMostToggle.Checked; };
            panel.Controls.Add(topMostToggle);
            return panel;
        }

        private Control BuildStatusCard()
        {
            Panel panel = NewCard();
            panel.Size = new Size(772, 64);
            connectionBadge = NewLabel("自动连接", 9, FontStyle.Bold, warning);
            connectionBadge.Name = "ConnectionBadge";
            connectionBadge.SetBounds(632, 17, 122, 30);
            connectionBadge.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            connectionBadge.TextAlign = ContentAlignment.MiddleCenter;
            connectionBadge.BackColor = Color.FromArgb(48, 41, 28);
            panel.Controls.Add(connectionBadge);
            statusDot = new StatusLamp { BackColor = warning, Location = new Point(18, 18), Size = new Size(10, 10) };
            panel.Controls.Add(statusDot);
            statusTitle = NewLabel("等待游戏启动", 12, FontStyle.Bold, textPrimary);
            statusTitle.SetBounds(40, 10, 550, 25);
            statusTitle.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            panel.Controls.Add(statusTitle);
            statusDetail = NewLabel("启动游戏后自动连接", 10.5f, FontStyle.Regular, textSecondary);
            statusDetail.SetBounds(40, 35, 550, 20);
            statusDetail.AutoEllipsis = true;
            statusDetail.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            panel.Controls.Add(statusDetail);
            return panel;
        }

        private Control BuildMoneyCard()
        {
            Panel panel = NewCard();
            TableLayoutPanel layout = Grid(4, 1);
            layout.Padding = new Padding(18, 12, 18, 12);
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 192));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 16));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            panel.Controls.Add(layout);
            Label title = NewLabel("资金", 14, FontStyle.Bold, textPrimary);
            title.Dock = DockStyle.Fill;
            title.TextAlign = ContentAlignment.MiddleLeft;
            title.Margin = Padding.Empty;
            layout.Controls.Add(title, 0, 0);
            moneyAmount = new NumericUpDown {
                Name = "MoneyAmount", Minimum = 0, Maximum = 99999999, Value = 1000000,
                ThousandsSeparator = true, BackColor = input, ForeColor = textPrimary,
                BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 22, FontStyle.Regular, GraphicsUnit.Pixel),
                TextAlign = HorizontalAlignment.Right, Margin = Padding.Empty,
                Width = 192, Anchor = AnchorStyles.None,
                AccessibleName = "目标资金金额", AccessibleDescription = "输入 0 到 99999999，按应用资金或 F5 生效", TabIndex = 0
            };
            layout.Controls.Add(moneyAmount, 1, 0);
            moneyButton = NewButton("应用资金  F5", 130, true);
            moneyButton.Name = "MoneyButton";
            moneyButton.AccessibleName = "应用资金，快捷键 F5";
            moneyButton.Anchor = AnchorStyles.None;
            moneyButton.TabIndex = 1;
            moneyButton.Click += delegate { SetMoney(); };
            layout.Controls.Add(moneyButton, 3, 0);
            return panel;
        }

        private Control BuildToggleArea()
        {
            TableLayoutPanel layout = Grid(3, 1);
            for (int i = 0; i < 3; i++) layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
            powerToggle = AddToggleCard(layout, 0, "无限电力", "维持供电，雷达与建筑持续运转", "F6", "power");
            buildToggle = AddToggleCard(layout, 1, "快速建造", "建筑与单位近乎即刻完成", "F9", "build");
            superToggle = AddToggleCard(layout, 2, "无限超级武器", "保持已拥有的超级武器就绪", "F10", "super");
            powerToggle.CheckedChanged += delegate { if (!internalToggle) TogglePower(); };
            buildToggle.CheckedChanged += delegate { if (!internalToggle) ToggleBuild(); };
            superToggle.CheckedChanged += delegate { if (!internalToggle) ToggleSuper(); };
            return layout;
        }

        private CheckBox AddToggleCard(TableLayoutPanel table, int column, string title, string description, string hotkey, string glyph)
        {
            SurfacePanel panel = (SurfacePanel)NewCard();
            panel.Margin = new Padding(column == 0 ? 0 : 6, 0, column == 2 ? 0 : 6, 12);
            panel.Name = glyph + "Card";
            UiGlyph icon = new UiGlyph(glyph, accent) { Location = new Point(16, 13), Size = new Size(23, 23) };
            panel.Controls.Add(icon);
            Label key = NewLabel(hotkey, 9, FontStyle.Bold, textSecondary);
            key.Name = glyph + "Key";
            key.TextAlign = ContentAlignment.MiddleCenter;
            key.BackColor = input;
            key.SetBounds(194, 13, 38, 23);
            key.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            panel.Controls.Add(key);
            Label label = NewLabel(title, 12, FontStyle.Bold, textPrimary);
            label.SetBounds(16, 43, 214, 24);
            label.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            panel.Controls.Add(label);
            FeatureSwitch toggle = new FeatureSwitch {
                Name = glyph + "Toggle", AccessibleName = title + "，快捷键 " + hotkey,
                AccessibleDescription = description, Location = new Point(12, 76), Size = new Size(220, 30),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                BackColor = card, ForeColor = textSecondary, TabIndex = 0
            };
            toggle.CheckedChanged += delegate {
                panel.LineColor = toggle.Checked ? Color.FromArgb(69, 130, 112) : border;
                panel.Invalidate();
                UpdateActiveCount();
            };
            uiTips.SetToolTip(toggle, glyph == "build" ? "资金充足时近乎即刻完成；保留正常扣款、手动暂停和出厂动画。" : description);
            panel.Controls.Add(toggle);
            table.Controls.Add(panel, column, 0);
            return toggle;
        }

        private Control BuildActionCard()
        {
            TableLayoutPanel layout = Grid(2, 1);
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            mapButton = AddAction(layout, 0, "一键全图", "揭开当前玩家的地图迷雾", "F7", "map", textPrimary);
            mapButton.Name = "MapButton";
            mapButton.Click += delegate { RevealMap(); };
            winButton = AddAction(layout, 1, "立即胜利", "结束当前对局", "F11", "win", danger);
            winButton.Name = "WinButton";
            winButton.Click += delegate { TriggerWin(); };
            return layout;
        }

        private UiButton AddAction(TableLayoutPanel layout, int column, string title, string description, string hotkey, string glyph, Color color)
        {
            Panel panel = NewCard();
            panel.Size = new Size(380, 64);
            panel.Margin = new Padding(column == 0 ? 0 : 6, 0, column == 0 ? 6 : 0, 12);
            UiGlyph icon = new UiGlyph(glyph, color) { Location = new Point(16, 20), Size = new Size(24, 24) };
            panel.Controls.Add(icon);
            Label heading = NewLabel(title, 11.5f, FontStyle.Bold, color);
            heading.SetBounds(52, 20, 190, 24);
            panel.Controls.Add(heading);
            UiButton button = NewButton("执行  " + hotkey, 90, false);
            button.ForeColor = color;
            button.AccessibleName = title + "，快捷键 " + hotkey;
            button.SetBounds(264, 13, 90, 38);
            button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            button.TabIndex = 0;
            uiTips.SetToolTip(button, description);
            panel.Controls.Add(button);
            layout.Controls.Add(panel, column, 0);
            return button;
        }

        private Control BuildDiagnosticsCard()
        {
            Panel panel = NewCard();
            panel.Padding = new Padding(16, 8, 16, 12);
            Panel heading = new Panel { Dock = DockStyle.Top, Height = 34, TabIndex = 0 };
            Label label = NewLabel("运行记录", 9, FontStyle.Bold, textPrimary);
            label.Dock = DockStyle.Left;
            label.Width = 140;
            label.TextAlign = ContentAlignment.MiddleLeft;
            heading.Controls.Add(label);
            UiButton copy = NewButton("复制诊断", 96, false);
            copy.Name = "CopyDiagnostics";
            copy.Dock = DockStyle.Right;
            copy.AccessibleName = "复制诊断信息";
            copy.Click += delegate {
                try { Clipboard.SetText(BuildDiagnosticText()); Log("诊断信息已复制"); }
                catch (Exception ex) { Log("复制失败：" + ex.Message); }
            };
            heading.Controls.Add(copy);
            diagnostics = new TextBox {
                Name = "Diagnostics", ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None, BackColor = card, ForeColor = textSecondary,
                Font = UiFonts.Create(10.5f, FontStyle.Regular), WordWrap = true,
                Dock = DockStyle.Fill, AccessibleName = "运行记录，可选择并复制文本", TabIndex = 1
            };
            panel.Controls.Add(diagnostics);
            panel.Controls.Add(heading);
            return panel;
        }

        private Control BuildFooter()
        {
            Panel panel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            activity = NewLabel("单机  ·  已开启 0 / 3", 10.5f, FontStyle.Regular, textSecondary);
            activity.Dock = DockStyle.Fill;
            activity.TextAlign = ContentAlignment.MiddleLeft;
            Label version = NewLabel("TOOOOOOBY  /  v1.0.0", 10.5f, FontStyle.Regular, textSecondary);
            version.Name = "VersionLabel";
            version.Dock = DockStyle.Right;
            version.Width = 210;
            version.TextAlign = ContentAlignment.MiddleRight;
            panel.Controls.Add(activity);
            panel.Controls.Add(version);
            return panel;
        }

        private Panel NewCard()
        {
            return new SurfacePanel { Dock = DockStyle.Fill, BackColor = card, LineColor = border,
                Margin = new Padding(0, 0, 0, 12), Size = new Size(250, 112) };
        }

        private TableLayoutPanel Grid(int columns, int rows)
        {
            return new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = rows,
                BackColor = Color.Transparent, Margin = Padding.Empty, Padding = Padding.Empty, TabStop = false, Size = new Size(772, 136) };
        }

        private Label NewLabel(string value, float size, FontStyle style, Color color)
        {
            return new Label { Text = value, Font = UiFonts.Create(size, style),
                ForeColor = color, BackColor = Color.Transparent, UseMnemonic = false, TabStop = false };
        }

        private UiButton NewButton(string value, int width, bool primary)
        {
            return new UiButton { Text = value, Size = new Size(width, 42), Primary = primary,
                BackColor = primary ? accent : card, ForeColor = primary ? background : textPrimary,
                Font = UiFonts.Create(10.5f, FontStyle.Bold), Margin = Padding.Empty,
                Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
        }

        private void UpdateActiveCount()
        {
            if (activity == null) return;
            int count = (powerToggle != null && powerToggle.Checked ? 1 : 0) +
                (buildToggle != null && buildToggle.Checked ? 1 : 0) + (superToggle != null && superToggle.Checked ? 1 : 0);
            activity.Text = "单机  ·  已开启 " + count + " / 3";
        }

        private void UpdateAvailability(bool ready)
        {
            moneyButton.Enabled = ready;
            mapButton.Enabled = ready;
            winButton.Enabled = ready;
            // Leaving a match must never prevent disabling a live override.
            powerToggle.Enabled = ready || powerToggle.Checked;
            buildToggle.Enabled = ready || buildToggle.Checked;
            superToggle.Enabled = ready || superToggle.Checked;
        }

        private void UpdateConnection(bool connected, bool ready, string path)
        {
            if (connected) lastConnectionError = null;
            statusDot.BackColor = ready ? success : warning;
            statusDot.Invalidate();
            statusTitle.Text = ready ? "已连接 · 游戏中" : connected ? "已连接 · 等待进入对局" : "等待游戏启动";
            statusDetail.Text = ready ? "功能可用" : connected ? "请先进入单机对局" : "启动游戏后自动连接";
            connectionBadge.Text = ready ? "准备就绪" : connected ? "等待对局" : "自动连接";
            connectionBadge.ForeColor = ready ? success : warning;
            connectionBadge.BackColor = ready ? Color.FromArgb(29, 49, 43) : Color.FromArgb(48, 41, 28);
            uiTips.SetToolTip(statusDetail, path ?? statusDetail.Text);
            UpdateAvailability(ready);
        }
    }

    internal static class UiFonts
    {
        // Preserve the 96-DPI design's font sizes without desktop scaling.
        internal static Font Create(float size, FontStyle style)
        { return new Font("Microsoft YaHei UI", size * 4f / 3f, style, GraphicsUnit.Pixel); }
    }

    internal static class UiShape
    {
        internal static Color Background(Control control)
        {
            while (control != null)
            {
                if (control.BackColor.A == 255) return control.BackColor;
                control = control.Parent;
            }
            return Color.FromArgb(15, 20, 28);
        }

        internal static GraphicsPath Round(RectangleF rectangle, float radius)
        {
            float d = Math.Max(1, Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height)));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rectangle.X, rectangle.Y, d, d, 180, 90);
            path.AddArc(rectangle.Right - d, rectangle.Y, d, d, 270, 90);
            path.AddArc(rectangle.Right - d, rectangle.Bottom - d, d, d, 0, 90);
            path.AddArc(rectangle.X, rectangle.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class SurfacePanel : Panel
    {
        internal Color LineColor = Color.FromArgb(52, 60, 72);
        internal SurfacePanel() { DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(UiShape.Background(Parent));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = UiShape.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 10))
            using (SolidBrush fill = new SolidBrush(BackColor))
            using (Pen line = new Pen(LineColor)) { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(line, path); }
        }
    }

    internal sealed class StatusLamp : Panel
    {
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(UiShape.Background(Parent));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush fill = new SolidBrush(BackColor)) e.Graphics.FillEllipse(fill, 0, 0, Width - 1, Height - 1);
        }
    }

    internal sealed class UiButton : Button
    {
        internal bool Primary;
        private bool hovered, pressed;
        internal UiButton() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(UiShape.Background(Parent));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = !Enabled ? Color.FromArgb(39, 46, 57) : Primary ?
                (pressed ? Color.FromArgb(208, 162, 94) : hovered ? Color.FromArgb(247, 206, 140) : BackColor) :
                (pressed ? Color.FromArgb(35, 43, 55) : hovered ? Color.FromArgb(48, 59, 73) : BackColor);
            Color foreground = Enabled ? ForeColor : Color.FromArgb(142, 153, 169);
            using (GraphicsPath path = UiShape.Round(new RectangleF(1, 1, Width - 3, Height - 3), 7))
            using (SolidBrush brush = new SolidBrush(fill))
            using (Pen line = new Pen(Focused ? Color.FromArgb(234, 190, 119) : Primary && Enabled ? fill : Color.FromArgb(68, 80, 95), Focused ? 2 : 1))
            { e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(line, path); }
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, foreground, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5), foreground, fill);
        }
    }

    internal sealed class FeatureSwitch : CheckBox
    {
        internal bool StateText = true;
        private bool hovered;
        internal FeatureSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoSize = false; Cursor = Cursors.Hand; Text = "已关闭";
            Font = UiFonts.Create(10.5f, FontStyle.Regular);
        }
        protected override void OnCheckedChanged(EventArgs e)
        {
            if (StateText) Text = Checked ? "已开启" : "已关闭";
            base.OnCheckedChanged(e); Invalidate();
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; base.OnMouseEnter(e); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; base.OnMouseLeave(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Height / (StateText ? 30f : 36f);
            if (hovered && Enabled)
                using (SolidBrush hover = new SolidBrush(Color.FromArgb(40, 50, 62)))
                using (GraphicsPath shape = UiShape.Round(new RectangleF(0, 0, Width - 1, Height - 1), 6 * s)) e.Graphics.FillPath(hover, shape);
            Color foreground = !Enabled ? Color.FromArgb(142, 153, 169) : Checked && StateText ? Color.FromArgb(109, 218, 174) : ForeColor;
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle((int)(4*s), 0, Width - (int)(56*s), Height), foreground,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            RectangleF track = new RectangleF(Width - 48*s, (Height - 22*s)/2, 42*s, 22*s);
            using (GraphicsPath shape = UiShape.Round(track, 11*s))
            using (SolidBrush fill = new SolidBrush(Checked ? Color.FromArgb(109, 218, 174) : Color.FromArgb(70, 82, 97)))
                e.Graphics.FillPath(fill, shape);
            using (SolidBrush dot = new SolidBrush(Checked ? Color.FromArgb(19, 31, 29) : Color.FromArgb(226, 232, 240)))
                e.Graphics.FillEllipse(dot, Checked ? track.Right - 19*s : track.Left + 3*s, track.Top + 3*s, 16*s, 16*s);
            if (Focused)
                using (Pen line = new Pen(Color.FromArgb(234, 190, 119), 1.5f))
                using (GraphicsPath shape = UiShape.Round(new RectangleF(1, 1, Width - 3, Height - 3), 6*s)) e.Graphics.DrawPath(line, shape);
        }
    }

    internal sealed class UiGlyph : Control
    {
        private readonly string kind;
        private readonly Color color;
        internal UiGlyph(string kind, Color color)
        { this.kind = kind; this.color = color; TabStop = false; SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); BackColor = Color.Transparent; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.ScaleTransform(Width / 24f, Height / 24f);
            using (Pen pen = new Pen(color, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                if (kind == "power") e.Graphics.DrawPolygon(pen, new PointF[] { new PointF(13, 2), new PointF(5, 13), new PointF(11, 13), new PointF(10, 22), new PointF(19, 10), new PointF(13, 10) });
                else if (kind == "build")
                { e.Graphics.DrawLines(pen, new Point[] { new Point(3, 10), new Point(12, 3), new Point(21, 10) }); e.Graphics.DrawRectangle(pen, 6, 10, 12, 11); e.Graphics.DrawLine(pen, 12, 15, 12, 21); e.Graphics.DrawLine(pen, 6, 15, 18, 15); }
                else if (kind == "super")
                { e.Graphics.DrawEllipse(pen, 5, 5, 14, 14); e.Graphics.DrawEllipse(pen, 9, 9, 6, 6); e.Graphics.DrawLine(pen, 12, 1, 12, 5); e.Graphics.DrawLine(pen, 12, 19, 12, 23); e.Graphics.DrawLine(pen, 1, 12, 5, 12); e.Graphics.DrawLine(pen, 19, 12, 23, 12); }
                else if (kind == "map")
                { e.Graphics.DrawPolygon(pen, new Point[] { new Point(3, 5), new Point(9, 3), new Point(15, 6), new Point(21, 4), new Point(21, 19), new Point(15, 21), new Point(9, 18), new Point(3, 20) }); e.Graphics.DrawLine(pen, 9, 3, 9, 18); e.Graphics.DrawLine(pen, 15, 6, 15, 21); }
                else if (kind == "win")
                { e.Graphics.DrawLine(pen, 5, 3, 5, 22); e.Graphics.DrawLines(pen, new Point[] { new Point(5, 4), new Point(12, 3), new Point(19, 5), new Point(17, 13), new Point(11, 11), new Point(5, 12) }); }
                else
                {
                    using (GraphicsPath outline = UiShape.Round(new RectangleF(1, 1, 22, 22), 5)) e.Graphics.DrawPath(pen, outline);
                    e.Graphics.DrawArc(pen, 6, 5, 12, 12, 140, 260);
                    e.Graphics.DrawLines(pen, new PointF[] { new PointF(7.5f, 15), new PointF(7.5f, 18), new PointF(4.5f, 18) });
                    e.Graphics.DrawLines(pen, new PointF[] { new PointF(16.5f, 15), new PointF(16.5f, 18), new PointF(19.5f, 18) });
                }
            }
        }
    }
}
