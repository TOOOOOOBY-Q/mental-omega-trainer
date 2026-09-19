// 心灵终结单机修改器 (Mental Omega 3.3.6 single-player trainer)
//
// 目标游戏：Mental Omega 3.3.6（基于 Yuri's Revenge 1.001，经 Syringe 加载 Ares）。
// 工作原理：按进程名附加 gamemd.exe，通过 ReadProcessMemory / WriteProcessMemory
// 读写玩家数据；“一键全图”通过远程线程调用游戏自身的 MapClass::Reveal。
// 仅限本地单机使用，不含任何网络、广告或更新检查代码。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

[assembly: AssemblyFileVersion("3.3.6.4")]
[assembly: CompilationRelaxations(8)]
[assembly: RuntimeCompatibility(WrapNonExceptionThrows = true)]
[assembly: AssemblyCopyright("TOOOOOOBY")]
[assembly: AssemblyTitle("心灵终结单机修改器")]
[assembly: AssemblyDescription("Mental Omega 3.3.6 / Yuri's Revenge 1.001 单机修改器")]
[assembly: AssemblyCompany("TOOOOOOBY")]
[assembly: AssemblyProduct("心灵终结修改器")]
[assembly: AssemblyVersion("3.3.6.4")]
namespace MOTrainer336
{
	internal static class NativeMethods
	{
		internal const uint PROCESS_CREATE_THREAD = 2u;

		internal const uint PROCESS_VM_OPERATION = 8u;

		internal const uint PROCESS_VM_READ = 16u;

		internal const uint PROCESS_VM_WRITE = 32u;

		internal const uint PROCESS_QUERY_INFORMATION = 1024u;

		internal const uint PAGE_EXECUTE_READWRITE = 64u;

		internal const uint MOD_NOREPEAT = 16384u;

		internal const int WM_HOTKEY = 786;

		internal const uint MEM_COMMIT_RESERVE = 12288u;

		internal const uint MEM_RELEASE = 32768u;

		internal const uint WAIT_OBJECT_0 = 0u;

		[DllImport("kernel32.dll", SetLastError = true)]
		internal static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

		[DllImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static extern bool CloseHandle(IntPtr handle);

		[DllImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static extern bool ReadProcessMemory(IntPtr process, IntPtr address, [Out] byte[] buffer, int size, out IntPtr bytesRead);

		[DllImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out IntPtr bytesWritten);

		[DllImport("kernel32.dll", SetLastError = true)]
		internal static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, UIntPtr size, uint allocationType, uint protect);

		[DllImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static extern bool VirtualFreeEx(IntPtr process, IntPtr address, UIntPtr size, uint freeType);

		[DllImport("kernel32.dll", SetLastError = true)]
		internal static extern IntPtr CreateRemoteThread(IntPtr process, IntPtr threadAttributes, UIntPtr stackSize, IntPtr startAddress, IntPtr parameter, uint creationFlags, out uint threadId);

		[DllImport("kernel32.dll", SetLastError = true)]
		internal static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

		[DllImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static extern bool VirtualProtectEx(IntPtr process, IntPtr address, UIntPtr size, uint newProtect, out uint oldProtect);

		[DllImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);

		[DllImport("user32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

		[DllImport("user32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static extern bool UnregisterHotKey(IntPtr window, int id);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static extern bool SetProcessDPIAware();
	}
	internal sealed class GameMemory : IDisposable
	{
		// 以下为 Yuri's Revenge 1.001 / Mental Omega 3.3.6 的固定内存地址与偏移，
		// 仅对该版本有效；游戏或 Ares 升级后需要重新核对。
		internal const int CurrentPlayerAddress = 11025740;

		internal const int WinFlagAddress = 11025737;

		internal const int BalanceOffset = 780;

		internal const int FactoryCountOffset = 21368;

		internal const int PowerOutputOffset = 21412;

		internal const int PowerDrainOffset = 21416;

		internal const int SuperItemsOffset = 600;

		internal const int SuperCountOffset = 612;

		internal const int SuperIsChargedOffset = 111;

		internal const int SuperCameoChargeStateOffset = 120;

		internal const int MapClassAddress = 8910824;

		// MapClass::Reveal 入口（0x578090）。Ares 可能在入口写入跳转钩子（E9/EB），
		// 校验时两种情况都视为有效。
		internal const int RevealMapFunctionAddress = 5733776;

		private Process process;

		private IntPtr handle;

		internal int ProcessId
		{
			get
			{
				if (process != null)
				{
					return process.Id;
				}
				return 0;
			}
		}

		internal string ExecutablePath { get; private set; }

		internal bool IsAlive
		{
			get
			{
				try
				{
					return process != null && !process.HasExited && handle != IntPtr.Zero;
				}
				catch
				{
					return false;
				}
			}
		}

		internal static GameMemory TryAttach(out string detail)
		{
			string[] array = new string[4] { "gamemd", "gamemd-spawn", "gameares", "game" };
			string[] array2 = array;
			foreach (string processName in array2)
			{
				Process[] processesByName;
				try
				{
					processesByName = Process.GetProcessesByName(processName);
				}
				catch
				{
					continue;
				}
				Process[] array3 = processesByName;
				foreach (Process candidate in array3)
				{
					GameMemory gameMemory = new GameMemory();
					if (gameMemory.Attach(candidate, out detail))
					{
						return gameMemory;
					}
					gameMemory.Dispose();
				}
			}
			detail = "未检测到 gamemd.exe";
			return null;
		}

		private bool Attach(Process candidate, out string detail)
		{
			process = candidate;
			try
			{
				ExecutablePath = candidate.MainModule.FileName;
			}
			catch
			{
				ExecutablePath = "gamemd.exe";
			}
			uint access = 1082u;
			handle = NativeMethods.OpenProcess(access, false, candidate.Id);
			if (handle == IntPtr.Zero)
			{
				detail = "找到游戏，但无法打开进程（错误 " + Marshal.GetLastWin32Error() + "）";
				return false;
			}
			byte[] data;
			if (!TryRead(4194304, 2, out data) || data[0] != 77 || data[1] != 90)
			{
				detail = "游戏内存布局不受支持";
				return false;
			}
			detail = "已连接 gamemd.exe";
			return true;
		}

		internal bool HasCurrentPlayer(out int player)
		{
			player = 0;
			if (!IsAlive || !TryReadInt32(11025740, out player))
			{
				return false;
			}
			if (player >= 65536)
			{
				return player < 2147418112;
			}
			return false;
		}

		internal bool SetMoney(int amount, out string error)
		{
			int player;
			if (!HasCurrentPlayer(out player))
			{
				return Fail("请先进入一局游戏", out error);
			}
			if (!TryWriteInt32(player + 780, amount))
			{
				return LastError("资金写入失败", out error);
			}
			error = null;
			return true;
		}

		internal bool TriggerWin(out string error)
		{
			int player;
			if (!HasCurrentPlayer(out player))
			{
				return Fail("请先进入一局游戏", out error);
			}
			if (!TryWrite(11025737, new byte[1] { 1 }))
			{
				return LastError("胜利标记写入失败", out error);
			}
			error = null;
			return true;
		}

		internal bool RevealMap(out string error)
		{
			int player;
			if (!HasCurrentPlayer(out player))
			{
				return Fail("请先进入一局游戏", out error);
			}
			byte[] array = new byte[7] { 139, 68, 36, 4, 83, 85, 87 };
			byte[] data;
			if (!TryRead(5733776, array.Length, out data))
			{
				return LastError("全图函数校验失败", out error);
			}
			bool flag = true;
			for (int i = 0; i < array.Length; i++)
			{
				if (data[i] != array[i])
				{
					flag = false;
				}
			}
			bool flag2 = data[0] == 233 || data[0] == 235;
			if (!flag && !flag2)
			{
				return Fail("全图函数与当前游戏版本不匹配", out error);
			}
			List<byte> list = new List<byte>();
			list.Add(185);
			list.AddRange(BitConverter.GetBytes(8910824));
			list.Add(104);
			list.AddRange(BitConverter.GetBytes(player));
			list.Add(184);
			list.AddRange(BitConverter.GetBytes(5733776));
			list.Add(byte.MaxValue);
			list.Add(208);
			list.Add(51);
			list.Add(192);
			list.Add(194);
			list.Add(4);
			list.Add(0);
			byte[] array2 = list.ToArray();
			IntPtr intPtr = NativeMethods.VirtualAllocEx(handle, IntPtr.Zero, new UIntPtr((uint)array2.Length), 12288u, 64u);
			if (intPtr == IntPtr.Zero)
			{
				return LastError("全图代码内存申请失败", out error);
			}
			IntPtr bytesWritten;
			if (!NativeMethods.WriteProcessMemory(handle, intPtr, array2, array2.Length, out bytesWritten) || bytesWritten.ToInt64() != array2.Length)
			{
				int lastWin32Error = Marshal.GetLastWin32Error();
				NativeMethods.VirtualFreeEx(handle, intPtr, UIntPtr.Zero, 32768u);
				error = "全图代码写入失败（错误 " + lastWin32Error + "）";
				return false;
			}
			uint threadId;
			IntPtr intPtr2 = NativeMethods.CreateRemoteThread(handle, IntPtr.Zero, UIntPtr.Zero, intPtr, IntPtr.Zero, 0u, out threadId);
			if (intPtr2 == IntPtr.Zero)
			{
				int lastWin32Error2 = Marshal.GetLastWin32Error();
				NativeMethods.VirtualFreeEx(handle, intPtr, UIntPtr.Zero, 32768u);
				error = "全图执行失败（错误 " + lastWin32Error2 + "）";
				return false;
			}
			uint num = NativeMethods.WaitForSingleObject(intPtr2, 15000u);
			NativeMethods.CloseHandle(intPtr2);
			if (num != 0)
			{
				error = "全图执行超时；请观察游戏是否已经生效";
				return false;
			}
			NativeMethods.VirtualFreeEx(handle, intPtr, UIntPtr.Zero, 32768u);
			error = null;
			return true;
		}

		internal bool SetPower(bool enabled, out string error)
		{
			// Ares 兼容的数据模式：直接写 HouseClass 的电力字段，
			// 不修改游戏代码段，避免与 Ares 的运行时钩子冲突。
			if (!enabled)
			{
				error = null;
				return true;
			}
			int player;
			if (!HasCurrentPlayer(out player))
			{
				return Fail("请先进入一局游戏", out error);
			}
			if (!TryWriteInt32(player + 21412, 1000000))
			{
				return LastError("电力输出写入失败", out error);
			}
			if (!TryWriteInt32(player + 21416, 0))
			{
				return LastError("电力消耗写入失败", out error);
			}
			error = null;
			return true;
		}

		internal bool SetSuperWeapon(bool enabled, out string error)
		{
			if (!enabled)
			{
				error = null;
				return true;
			}
			int player;
			if (!HasCurrentPlayer(out player))
			{
				return Fail("请先进入一局游戏", out error);
			}
			int value;
			int value2;
			if (!TryReadInt32(player + 600, out value) || !TryReadInt32(player + 612, out value2))
			{
				return LastError("超级武器列表读取失败", out error);
			}
			if (value2 < 0 || value2 > 256 || (value2 > 0 && (value < 65536 || value >= 2147418112)))
			{
				return Fail("超级武器列表结构无效", out error);
			}
			for (int i = 0; i < value2; i++)
			{
				int value3;
				if (!TryReadInt32(value + i * 4, out value3))
				{
					return LastError("超级武器指针读取失败", out error);
				}
				if (value3 < 65536 || value3 >= 2147418112)
				{
					continue;
				}
				int value4;
				if (!TryReadInt32(value3 + 120, out value4))
				{
					return LastError("超级武器状态读取失败", out error);
				}
				if (value4 != -1)
				{
					byte value5;
					if (!TryReadByte(value3 + 111, out value5))
					{
						return LastError("超级武器充能状态读取失败", out error);
					}
					if (value5 == 0 && !TryWrite(value3 + 111, new byte[1] { 1 }))
					{
						return LastError("超级武器充能写入失败", out error);
					}
				}
			}
			error = null;
			return true;
		}

		internal bool ReadFactoryCounts(out int[] counts)
		{
			counts = null;
			int player;
			if (!HasCurrentPlayer(out player))
			{
				return false;
			}
			int[] array = new int[5];
			for (int i = 0; i < array.Length; i++)
			{
				if (!TryReadInt32(player + 21368 + i * 4, out array[i]))
				{
					return false;
				}
			}
			counts = array;
			return true;
		}

		internal bool SetFactoryCounts(int[] values, out string error)
		{
			int player;
			if (!HasCurrentPlayer(out player))
			{
				return Fail("请先进入一局游戏", out error);
			}
			if (values == null || values.Length != 5)
			{
				return Fail("建造参数无效", out error);
			}
			for (int i = 0; i < values.Length; i++)
			{
				if (!TryWriteInt32(player + 21368 + i * 4, values[i]))
				{
					return LastError("快速建造写入失败", out error);
				}
			}
			error = null;
			return true;
		}

		internal string SignatureSummary()
		{
			return "Ares 数据模式";
		}

		private bool TryReadInt32(int address, out int value)
		{
			value = 0;
			byte[] data;
			if (!TryRead(address, 4, out data))
			{
				return false;
			}
			value = BitConverter.ToInt32(data, 0);
			return true;
		}

		private bool TryReadByte(int address, out byte value)
		{
			value = 0;
			byte[] data;
			if (!TryRead(address, 1, out data))
			{
				return false;
			}
			value = data[0];
			return true;
		}

		private bool TryWriteInt32(int address, int value)
		{
			return TryWrite(address, BitConverter.GetBytes(value));
		}

		private bool TryRead(int address, int length, out byte[] data)
		{
			data = new byte[length];
			if (!IsAlive)
			{
				return false;
			}
			IntPtr bytesRead;
			if (NativeMethods.ReadProcessMemory(handle, new IntPtr(address), data, length, out bytesRead))
			{
				return bytesRead.ToInt64() == length;
			}
			return false;
		}

		private bool TryWrite(int address, byte[] data)
		{
			if (!IsAlive)
			{
				return false;
			}
			IntPtr bytesWritten;
			if (NativeMethods.WriteProcessMemory(handle, new IntPtr(address), data, data.Length, out bytesWritten))
			{
				return bytesWritten.ToInt64() == data.Length;
			}
			return false;
		}

		private static bool Fail(string message, out string error)
		{
			error = message;
			return false;
		}

		private static bool LastError(string message, out string error)
		{
			error = message + "（错误 " + Marshal.GetLastWin32Error() + "）";
			return false;
		}

		public void Dispose()
		{
			if (handle != IntPtr.Zero)
			{
				NativeMethods.CloseHandle(handle);
				handle = IntPtr.Zero;
			}
			if (process != null)
			{
				process.Dispose();
				process = null;
			}
		}
	}
	internal sealed class TrainerForm : Form
	{
		private const int HotkeyMoney = 101;

		private const int HotkeyPower = 102;

		private const int HotkeyMap = 106;

		private const int HotkeyBuild = 103;

		private const int HotkeySuper = 104;

		private const int HotkeyWin = 105;

		private readonly Color background = Color.FromArgb(19, 22, 29);

		private readonly Color card = Color.FromArgb(29, 34, 44);

		private readonly Color cardHover = Color.FromArgb(36, 42, 54);

		private readonly Color accent = Color.FromArgb(229, 67, 75);

		private readonly Color accentDark = Color.FromArgb(188, 47, 55);

		private readonly Color textPrimary = Color.FromArgb(242, 244, 248);

		private readonly Color textSecondary = Color.FromArgb(163, 172, 188);

		private readonly Color success = Color.FromArgb(70, 201, 137);

		private readonly Color warning = Color.FromArgb(246, 185, 59);

		private readonly Timer refreshTimer = new Timer();

		private readonly Timer featureTimer = new Timer();

		private readonly List<string> logLines = new List<string>();

		private readonly Dictionary<int, string> hotkeyNames = new Dictionary<int, string>();

		private GameMemory memory;

		private int[] factorySnapshot;

		private int factorySnapshotPlayer;

		private string lastFeatureError;

		private bool internalToggle;

		private Panel statusDot;

		private Label statusTitle;

		private Label statusDetail;

		private Label gamePath;

		private Label activity;

		private TextBox diagnostics;

		private NumericUpDown moneyAmount;

		private CheckBox powerToggle;

		private CheckBox buildToggle;

		private CheckBox superToggle;

		private CheckBox topMostToggle;

		internal TrainerForm()
		{
			Text = "心灵终结 3.3.6 · 单机修改器";
			base.AutoScaleDimensions = new SizeF(96f, 96f);
			base.AutoScaleMode = AutoScaleMode.Dpi;
			base.ClientSize = new Size(700, 700);
			base.StartPosition = FormStartPosition.CenterScreen;
			BackColor = background;
			ForeColor = textPrimary;
			Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
			base.FormBorderStyle = FormBorderStyle.FixedSingle;
			base.MaximizeBox = false;
			DoubleBuffered = true;
			base.Padding = new Padding(30, 24, 30, 22);
			try
			{
				base.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
			}
			catch
			{
			}
			BuildInterface();
			refreshTimer.Interval = 250;
			refreshTimer.Tick += OnRefresh;
			refreshTimer.Start();
			featureTimer.Interval = 15;
			featureTimer.Tick += OnFeatureTick;
			featureTimer.Start();
			base.Shown += OnShown;
			base.FormClosing += OnClosing;
		}

		private void BuildInterface()
		{
			TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
			tableLayoutPanel.Dock = DockStyle.Fill;
			tableLayoutPanel.ColumnCount = 1;
			tableLayoutPanel.RowCount = 7;
			tableLayoutPanel.BackColor = background;
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 72f));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 82f));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 82f));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 196f));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 74f));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
			base.Controls.Add(tableLayoutPanel);
			tableLayoutPanel.Controls.Add(BuildHeader(), 0, 0);
			tableLayoutPanel.Controls.Add(BuildStatusCard(), 0, 1);
			tableLayoutPanel.Controls.Add(BuildMoneyCard(), 0, 2);
			tableLayoutPanel.Controls.Add(BuildToggleArea(), 0, 3);
			tableLayoutPanel.Controls.Add(BuildActionCard(), 0, 4);
			tableLayoutPanel.Controls.Add(BuildDiagnosticsCard(), 0, 5);
			tableLayoutPanel.Controls.Add(BuildFooter(), 0, 6);
		}

		private Control BuildHeader()
		{
			Panel panel = new Panel();
			panel.Dock = DockStyle.Fill;
			panel.BackColor = background;
			Panel panel2 = panel;
			Label label = NewLabel("心灵终结 单机修改器", 20f, FontStyle.Bold, textPrimary);
			label.Location = new Point(0, 0);
			label.AutoSize = true;
			panel2.Controls.Add(label);
			Label label2 = NewLabel("Mental Omega 3.3.6  ·  Yuri's Revenge 1.001 + Ares", 9f, FontStyle.Regular, textSecondary);
			label2.Location = new Point(2, 40);
			label2.AutoSize = true;
			panel2.Controls.Add(label2);
			topMostToggle = new CheckBox();
			topMostToggle.Text = "窗口置顶";
			topMostToggle.ForeColor = textSecondary;
			topMostToggle.AutoSize = true;
			topMostToggle.Location = new Point(490, 10);
			topMostToggle.CheckedChanged += delegate
			{
				base.TopMost = topMostToggle.Checked;
			};
			panel2.Controls.Add(topMostToggle);
			return panel2;
		}

		private Control BuildStatusCard()
		{
			Panel panel = NewCard();
			statusDot = new Panel
			{
				Size = new Size(12, 12),
				Location = new Point(18, 19),
				BackColor = warning
			};
			statusDot.Paint += delegate(object sender, PaintEventArgs e)
			{
				e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
				using (SolidBrush brush = new SolidBrush(statusDot.BackColor))
				{
					e.Graphics.FillEllipse(brush, 0, 0, 11, 11);
				}
			};
			panel.Controls.Add(statusDot);
			statusTitle = NewLabel("等待游戏", 11f, FontStyle.Bold, textPrimary);
			statusTitle.Location = new Point(42, 12);
			statusTitle.AutoSize = true;
			panel.Controls.Add(statusTitle);
			statusDetail = NewLabel("启动 Mental Omega 后会自动连接", 8.5f, FontStyle.Regular, textSecondary);
			statusDetail.Location = new Point(42, 37);
			statusDetail.AutoSize = true;
			panel.Controls.Add(statusDetail);
			gamePath = NewLabel("", 8f, FontStyle.Regular, textSecondary);
			gamePath.AutoEllipsis = true;
			gamePath.Location = new Point(330, 17);
			gamePath.Size = new Size(220, 38);
			gamePath.TextAlign = ContentAlignment.MiddleRight;
			panel.Controls.Add(gamePath);
			return panel;
		}

		private Control BuildMoneyCard()
		{
			Panel panel = NewCard();
			Label label = NewLabel("资金", 11f, FontStyle.Bold, textPrimary);
			label.Location = new Point(18, 14);
			label.AutoSize = true;
			panel.Controls.Add(label);
			moneyAmount = new NumericUpDown();
			moneyAmount.Minimum = 0m;
			moneyAmount.Maximum = 99999999m;
			moneyAmount.Value = 1000000m;
			moneyAmount.ThousandsSeparator = true;
			moneyAmount.BackColor = Color.FromArgb(20, 24, 32);
			moneyAmount.ForeColor = textPrimary;
			moneyAmount.BorderStyle = BorderStyle.FixedSingle;
			moneyAmount.Font = new Font(Font.FontFamily, 10f, FontStyle.Bold);
			moneyAmount.Location = new Point(350, 20);
			moneyAmount.Size = new Size(122, 30);
			panel.Controls.Add(moneyAmount);
			Button button = NewButton("应用  F5", 78);
			button.Location = new Point(480, 15);
			button.Click += delegate
			{
				SetMoney();
			};
			panel.Controls.Add(button);
			return panel;
		}

		private Control BuildToggleArea()
		{
			TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
			tableLayoutPanel.Dock = DockStyle.Fill;
			tableLayoutPanel.ColumnCount = 1;
			tableLayoutPanel.RowCount = 3;
			tableLayoutPanel.Padding = new Padding(0, 4, 0, 4);
			tableLayoutPanel.BackColor = background;
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333f));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333f));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 33.334f));
			powerToggle = AddToggleCard(tableLayoutPanel, 0, "无限电力", "F6");
			buildToggle = AddToggleCard(tableLayoutPanel, 1, "快速建造", "F9");
			superToggle = AddToggleCard(tableLayoutPanel, 2, "无限超级武器", "F10");
			powerToggle.CheckedChanged += delegate
			{
				if (!internalToggle)
				{
					TogglePower();
				}
			};
			buildToggle.CheckedChanged += delegate
			{
				if (!internalToggle)
				{
					ToggleBuild();
				}
			};
			superToggle.CheckedChanged += delegate
			{
				if (!internalToggle)
				{
					ToggleSuper();
				}
			};
			return tableLayoutPanel;
		}

		private CheckBox AddToggleCard(TableLayoutPanel table, int row, string title, string hotkey)
		{
			Panel panel = NewCard();
			panel.Margin = new Padding(0, 4, 0, 4);
			Label label = NewLabel(title, 10.5f, FontStyle.Bold, textPrimary);
			label.Location = new Point(18, 15);
			label.AutoSize = true;
			panel.Controls.Add(label);
			Label label2 = NewLabel(hotkey, 8.5f, FontStyle.Bold, textSecondary);
			label2.TextAlign = ContentAlignment.MiddleCenter;
			label2.BackColor = Color.FromArgb(45, 51, 64);
			label2.Location = new Point(472, 14);
			label2.Size = new Size(42, 24);
			panel.Controls.Add(label2);
			CheckBox toggle = new CheckBox();
			toggle.Appearance = Appearance.Button;
			toggle.FlatStyle = FlatStyle.Flat;
			toggle.FlatAppearance.BorderSize = 1;
			toggle.FlatAppearance.BorderColor = Color.FromArgb(85, 94, 112);
			toggle.FlatAppearance.CheckedBackColor = accent;
			toggle.BackColor = Color.FromArgb(44, 50, 62);
			toggle.ForeColor = textPrimary;
			toggle.Text = "OFF";
			toggle.TextAlign = ContentAlignment.MiddleCenter;
			toggle.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
			toggle.Location = new Point(520, 12);
			toggle.Size = new Size(42, 28);
			toggle.CheckedChanged += delegate
			{
				toggle.Text = (toggle.Checked ? "ON" : "OFF");
			};
			panel.Controls.Add(toggle);
			table.Controls.Add(panel, 0, row);
			return toggle;
		}

		private Control BuildActionCard()
		{
			Panel panel = NewCard();
			Label label = NewLabel("一键全图", 11f, FontStyle.Bold, textPrimary);
			label.Location = new Point(18, 19);
			label.AutoSize = true;
			panel.Controls.Add(label);
			Button button = NewButton("执行  F7", 96);
			button.Location = new Point(130, 11);
			button.Click += delegate
			{
				RevealMap();
			};
			panel.Controls.Add(button);
			Label label2 = NewLabel("立即胜利", 11f, FontStyle.Bold, textPrimary);
			label2.Location = new Point(330, 19);
			label2.AutoSize = true;
			panel.Controls.Add(label2);
			Button button2 = NewButton("执行  F11", 96);
			button2.Location = new Point(466, 11);
			button2.Click += delegate
			{
				TriggerWin();
			};
			panel.Controls.Add(button2);
			return panel;
		}

		private Control BuildDiagnosticsCard()
		{
			Panel panel = NewCard();
			Label label = NewLabel("运行记录", 9.5f, FontStyle.Bold, textPrimary);
			label.Location = new Point(14, 9);
			label.AutoSize = true;
			panel.Controls.Add(label);
			Button button = NewSecondaryButton("复制诊断", 82);
			button.Location = new Point(480, 5);
			button.Click += delegate
			{
				try
				{
					Clipboard.SetText(BuildDiagnosticText());
					Log("诊断信息已复制");
				}
				catch (Exception ex)
				{
					Log("复制失败：" + ex.Message);
				}
			};
			panel.Controls.Add(button);
			diagnostics = new TextBox();
			diagnostics.ReadOnly = true;
			diagnostics.Multiline = true;
			diagnostics.ScrollBars = ScrollBars.Vertical;
			diagnostics.BorderStyle = BorderStyle.None;
			diagnostics.BackColor = card;
			diagnostics.ForeColor = textSecondary;
			diagnostics.Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
			diagnostics.WordWrap = false;
			diagnostics.Location = new Point(15, 39);
			diagnostics.Size = new Size(545, 60);
			diagnostics.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
			panel.Controls.Add(diagnostics);
			return panel;
		}

		private Control BuildFooter()
		{
			Panel panel = new Panel();
			panel.Dock = DockStyle.Fill;
			panel.BackColor = background;
			Panel panel2 = panel;
			activity = NewLabel("仅用于本地单机游戏  ·  无广告  ·  无更新检查", 8f, FontStyle.Regular, textSecondary);
			activity.Location = new Point(0, 6);
			activity.AutoSize = true;
			panel2.Controls.Add(activity);
			Label label = NewLabel("TOOOOOOBY", 8f, FontStyle.Bold, textSecondary);
			label.Location = new Point(485, 6);
			label.AutoSize = true;
			panel2.Controls.Add(label);
			return panel2;
		}

		private Panel NewCard()
		{
			Panel panel = new Panel();
			panel.Dock = DockStyle.Fill;
			panel.BackColor = card;
			panel.Margin = new Padding(0, 4, 0, 4);
			panel.Padding = new Padding(0);
			return panel;
		}

		private Label NewLabel(string value, float size, FontStyle style, Color color)
		{
			Label label = new Label();
			label.Text = value;
			label.Font = new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point);
			label.ForeColor = color;
			label.BackColor = Color.Transparent;
			return label;
		}

		private Button NewButton(string value, int width)
		{
			Button button = new Button();
			button.Text = value;
			button.Size = new Size(width, 36);
			button.FlatStyle = FlatStyle.Flat;
			button.FlatAppearance.BorderSize = 0;
			button.BackColor = accent;
			button.ForeColor = Color.White;
			button.Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold);
			button.Cursor = Cursors.Hand;
			button.MouseEnter += delegate
			{
				button.BackColor = accentDark;
			};
			button.MouseLeave += delegate
			{
				button.BackColor = accent;
			};
			return button;
		}

		private Button NewSecondaryButton(string value, int width)
		{
			Button button = NewButton(value, width);
			button.Size = new Size(width, 28);
			button.BackColor = Color.FromArgb(55, 62, 76);
			button.FlatAppearance.BorderSize = 1;
			button.FlatAppearance.BorderColor = Color.FromArgb(82, 90, 106);
			button.MouseEnter += delegate
			{
				button.BackColor = cardHover;
			};
			button.MouseLeave += delegate
			{
				button.BackColor = Color.FromArgb(55, 62, 76);
			};
			return button;
		}

		private void OnShown(object sender, EventArgs e)
		{
			RegisterAllHotkeys();
			Log("已启动：等待 gamemd.exe");
		}

		private void RegisterAllHotkeys()
		{
			RegisterOne(101, Keys.F5, "F5 资金");
			RegisterOne(102, Keys.F6, "F6 电力");
			RegisterOne(106, Keys.F7, "F7 全图");
			RegisterOne(103, Keys.F9, "F9 建造");
			RegisterOne(104, Keys.F10, "F10 超武");
			RegisterOne(105, Keys.F11, "F11 胜利");
		}

		private void RegisterOne(int id, Keys key, string name)
		{
			if (NativeMethods.RegisterHotKey(base.Handle, id, 16384u, (uint)key))
			{
				hotkeyNames[id] = name;
			}
			else
			{
				Log(name + " 快捷键注册失败，按钮仍可用");
			}
		}

		protected override void WndProc(ref Message m)
		{
			if (m.Msg == 786)
			{
				switch (m.WParam.ToInt32())
				{
				case 101:
					SetMoney();
					break;
				case 102:
					powerToggle.Checked = !powerToggle.Checked;
					break;
				case 106:
					RevealMap();
					break;
				case 103:
					buildToggle.Checked = !buildToggle.Checked;
					break;
				case 104:
					superToggle.Checked = !superToggle.Checked;
					break;
				case 105:
					TriggerWin();
					break;
				}
			}
			else
			{
				base.WndProc(ref m);
			}
		}

		private void OnRefresh(object sender, EventArgs e)
		{
			if (memory == null || !memory.IsAlive)
			{
				if (memory != null)
				{
					memory.Dispose();
					memory = null;
					factorySnapshot = null;
					factorySnapshotPlayer = 0;
					SetAllToggles(false);
					Log("游戏已退出，功能状态已重置");
				}
				string detail;
				GameMemory gameMemory = GameMemory.TryAttach(out detail);
				if (gameMemory == null)
				{
					ShowDisconnected(detail);
					return;
				}
				memory = gameMemory;
				Log("已连接 PID " + memory.ProcessId + "；" + memory.SignatureSummary());
			}
			int player;
			bool flag = memory.HasCurrentPlayer(out player);
			statusDot.BackColor = (flag ? success : warning);
			statusDot.Invalidate();
			statusTitle.Text = (flag ? "已连接 · 游戏中" : "已连接 · 等待进入对局");
			statusDetail.Text = "gamemd.exe  ·  PID " + memory.ProcessId + "  ·  " + memory.SignatureSummary();
			gamePath.Text = memory.ExecutablePath;
			if (flag && buildToggle.Checked && player != factorySnapshotPlayer)
			{
				if (!memory.ReadFactoryCounts(out factorySnapshot))
				{
					LogOnce("无法读取新对局的建造现场值");
					SetToggle(buildToggle, false);
					factorySnapshotPlayer = 0;
				}
				else
				{
					factorySnapshotPlayer = player;
					Log("已识别新对局，快速建造现场值已刷新");
				}
			}
		}

		private void OnFeatureTick(object sender, EventArgs e)
		{
			int player;
			if (memory == null || !memory.IsAlive || !memory.HasCurrentPlayer(out player))
			{
				return;
			}
			string text = null;
			string error;
			if (powerToggle.Checked && !memory.SetPower(true, out error))
			{
				text = error;
			}
			if (text == null && buildToggle.Checked && !memory.SetFactoryCounts(new int[5] { 15, 15, 15, 15, 15 }, out error))
			{
				text = error;
			}
			if (text == null && superToggle.Checked && !memory.SetSuperWeapon(true, out error))
			{
				text = error;
			}
			if (!string.IsNullOrEmpty(text))
			{
				if (text != lastFeatureError)
				{
					Log(text);
				}
				lastFeatureError = text;
			}
			else
			{
				lastFeatureError = null;
			}
		}

		private void ShowDisconnected(string detail)
		{
			statusDot.BackColor = warning;
			statusDot.Invalidate();
			statusTitle.Text = "等待游戏";
			statusDetail.Text = detail + "；启动 Mental Omega 后会自动连接";
			gamePath.Text = "";
		}

		private bool EnsureReady()
		{
			int player;
			if (memory != null && memory.IsAlive && memory.HasCurrentPlayer(out player))
			{
				return true;
			}
			Log("操作未执行：请先进入一局游戏");
			return false;
		}

		private void SetMoney()
		{
			if (EnsureReady())
			{
				string error = null;
				int amount = decimal.ToInt32(moneyAmount.Value);
				if (memory.SetMoney(amount, out error))
				{
					Log("资金已设为 " + amount.ToString("N0"));
				}
				else
				{
					Log(error);
				}
			}
		}

		private void TriggerWin()
		{
			if (EnsureReady())
			{
				string error;
				if (memory.TriggerWin(out error))
				{
					Log("已发送立即胜利标记");
				}
				else
				{
					Log(error);
				}
			}
		}

		private void RevealMap()
		{
			if (EnsureReady())
			{
				string error;
				if (memory.RevealMap(out error))
				{
					Log("地图迷雾已全部揭开");
				}
				else
				{
					Log(error);
				}
			}
		}

		private void TogglePower()
		{
			if (!EnsureReady())
			{
				SetToggle(powerToggle, false);
				return;
			}
			string error;
			if (memory.SetPower(powerToggle.Checked, out error))
			{
				Log("无限电力 " + (powerToggle.Checked ? "已开启" : "已关闭"));
				return;
			}
			Log(error);
			SetToggle(powerToggle, !powerToggle.Checked);
		}

		private void ToggleBuild()
		{
			if (!EnsureReady())
			{
				SetToggle(buildToggle, false);
				return;
			}
			string error = null;
			if (buildToggle.Checked)
			{
				if (!memory.HasCurrentPlayer(out factorySnapshotPlayer))
				{
					Log("无法读取当前玩家");
					SetToggle(buildToggle, false);
				}
				else if (!memory.ReadFactoryCounts(out factorySnapshot))
				{
					Log("无法读取建造现场值");
					factorySnapshotPlayer = 0;
					SetToggle(buildToggle, false);
				}
				else if (memory.SetFactoryCounts(new int[5] { 15, 15, 15, 15, 15 }, out error))
				{
					Log("快速建造已开启");
				}
				else
				{
					Log(error);
					factorySnapshot = null;
					factorySnapshotPlayer = 0;
					SetToggle(buildToggle, false);
				}
			}
			else
			{
				if (factorySnapshot != null && memory.SetFactoryCounts(factorySnapshot, out error))
				{
					Log("快速建造已关闭并恢复现场值");
				}
				else if (factorySnapshot != null)
				{
					Log(error);
				}
				factorySnapshot = null;
				factorySnapshotPlayer = 0;
			}
		}

		private void ToggleSuper()
		{
			if (!EnsureReady())
			{
				SetToggle(superToggle, false);
				return;
			}
			string error;
			if (memory.SetSuperWeapon(superToggle.Checked, out error))
			{
				Log("无限超级武器 " + (superToggle.Checked ? "已开启" : "已关闭"));
				return;
			}
			Log(error);
			SetToggle(superToggle, !superToggle.Checked);
		}

		private void SetToggle(CheckBox toggle, bool value)
		{
			internalToggle = true;
			toggle.Checked = value;
			internalToggle = false;
		}

		private void SetAllToggles(bool value)
		{
			internalToggle = true;
			powerToggle.Checked = value;
			buildToggle.Checked = value;
			superToggle.Checked = value;
			internalToggle = false;
		}

		private void Log(string message)
		{
			string item = DateTime.Now.ToString("HH:mm:ss") + "  " + message;
			logLines.Add(item);
			while (logLines.Count > 80)
			{
				logLines.RemoveAt(0);
			}
			if (diagnostics != null)
			{
				diagnostics.Lines = logLines.ToArray();
				diagnostics.SelectionStart = diagnostics.TextLength;
				diagnostics.ScrollToCaret();
			}
		}

		private void LogOnce(string message)
		{
			if (!string.IsNullOrEmpty(message) && (logLines.Count == 0 || !logLines[logLines.Count - 1].EndsWith(message)))
			{
				Log(message);
			}
		}

		private string BuildDiagnosticText()
		{
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.AppendLine("心灵终结修改器 3.3.6.4 / TOOOOOOBY");
			stringBuilder.AppendLine("OS: " + Environment.OSVersion);
			stringBuilder.AppendLine("Process: " + ((memory == null) ? "not attached" : memory.ProcessId.ToString()));
			if (memory != null)
			{
				stringBuilder.AppendLine("Path: " + memory.ExecutablePath);
				stringBuilder.AppendLine("Mode: " + memory.SignatureSummary());
			}
			stringBuilder.AppendLine("Hotkeys: " + string.Join(", ", new List<string>(hotkeyNames.Values).ToArray()));
			stringBuilder.AppendLine("--- Log ---");
			foreach (string logLine in logLines)
			{
				stringBuilder.AppendLine(logLine);
			}
			return stringBuilder.ToString();
		}

		private void OnClosing(object sender, FormClosingEventArgs e)
		{
			refreshTimer.Stop();
			featureTimer.Stop();
			if (memory != null && memory.IsAlive)
			{
				string error;
				if (buildToggle.Checked && factorySnapshot != null)
				{
					memory.SetFactoryCounts(factorySnapshot, out error);
				}
				if (powerToggle.Checked)
				{
					memory.SetPower(false, out error);
				}
				if (superToggle.Checked)
				{
					memory.SetSuperWeapon(false, out error);
				}
			}
			foreach (int key in hotkeyNames.Keys)
			{
				NativeMethods.UnregisterHotKey(base.Handle, key);
			}
			if (memory != null)
			{
				memory.Dispose();
			}
		}
	}
	internal static class Program
	{
		[STAThread]
		private static void Main()
		{
			try
			{
				NativeMethods.SetProcessDPIAware();
			}
			catch
			{
			}
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.Run(new TrainerForm());
		}
	}
}
