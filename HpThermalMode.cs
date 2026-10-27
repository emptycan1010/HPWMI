using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HpThermal
{
    // HP BIOS 의 System Control 값 (HP.SystemControl.BiosWmi 에서 사용하는 값과 동일)
    enum Mode
    {
        Performance = 0,
        Balanced = 1,
        Cool = 2,
        Quiet = 3,
        PowerSaver = 4,
        Auto = 5,
        Silent = 6
    }

    class ModeInfo
    {
        public Mode Mode;
        public string Name;
        public string Description;
        public int SupportBit;
        public string[] Aliases;
    }

    class BiosResult
    {
        public uint ReturnCode;
        public byte[] Data;
    }

    static class HpBios
    {
        const uint CommandRead = 1;
        const uint CommandWrite = 2;
        const uint TypeSupportedModes = 0x0D;
        const uint TypeThermalMode = 0x4C;

        static readonly byte[] Sign = { 0x53, 0x45, 0x43, 0x55 }; // "SECU"

        public static BiosResult Call(uint command, uint commandType, byte[] data, int outSize)
        {
            using (ManagementClass inClass = new ManagementClass(@"root\wmi:hpqBDataIn"))
            using (ManagementObject inData = inClass.CreateInstance())
            using (ManagementClass biosClass = new ManagementClass(@"root\wmi:hpqBIntM"))
            {
                inData["Sign"] = Sign;
                inData["Command"] = command;
                inData["CommandType"] = commandType;
                inData["Size"] = (uint)data.Length;
                inData["hpqBData"] = data;

                ManagementObject target = null;
                foreach (ManagementObject mo in biosClass.GetInstances())
                {
                    target = mo;
                    break;
                }
                if (target == null)
                    throw new InvalidOperationException("HP BIOS WMI(hpqBIntM)를 찾을 수 없습니다.");

                string method = "hpqBIOSInt" + outSize;
                using (target)
                using (ManagementBaseObject p = target.GetMethodParameters(method))
                {
                    p["InData"] = inData;
                    using (ManagementBaseObject r = target.InvokeMethod(method, p, null))
                    using (ManagementBaseObject o = (ManagementBaseObject)r["OutData"])
                    {
                        BiosResult res = new BiosResult();
                        res.ReturnCode = Convert.ToUInt32(o["rwReturnCode"]);
                        res.Data = outSize > 0 ? (byte[])o["Data"] : new byte[0];
                        return res;
                    }
                }
            }
        }

        public static int GetMode()
        {
            BiosResult r = Call(CommandRead, TypeThermalMode, new byte[0], 4);
            if (r.ReturnCode != 0)
                throw new InvalidOperationException("현재 모드를 읽지 못했습니다. (BIOS 반환 코드 " + r.ReturnCode + ")");
            return r.Data[0];
        }

        public static void SetMode(Mode mode)
        {
            BiosResult r = Call(CommandWrite, TypeThermalMode, new byte[] { (byte)mode, 0, 0, 0 }, 0);
            if (r.ReturnCode != 0)
                throw new InvalidOperationException("모드를 변경하지 못했습니다. (BIOS 반환 코드 " + r.ReturnCode + ")");
        }

        // null 이면 지원 여부를 알 수 없음
        public static byte? GetSupportByte()
        {
            try
            {
                BiosResult r = Call(CommandRead, TypeSupportedModes, new byte[0], 128);
                if (r.ReturnCode != 0 || r.Data.Length < 7)
                    return null;
                return r.Data[6];
            }
            catch
            {
                return null;
            }
        }
    }

    static class WinPower
    {
        static readonly Guid BestPerformance = new Guid("ded574b5-45a0-4f42-8737-46345c09c238");
        static readonly Guid BestEfficiency = new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a");

        [DllImport("powrprof.dll")]
        static extern uint PowerSetUserConfiguredACPowerMode(ref Guid mode);

        [DllImport("powrprof.dll")]
        static extern uint PowerSetUserConfiguredDCPowerMode(ref Guid mode);

        // HP 소프트웨어와 동일하게 성능=최고 성능, 전원 절약=최고 전원 효율, 나머지=균형
        public static void Apply(Mode mode)
        {
            Guid g = Guid.Empty;
            if (mode == Mode.Performance) g = BestPerformance;
            else if (mode == Mode.PowerSaver) g = BestEfficiency;

            bool ac = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
            uint rc = ac ? PowerSetUserConfiguredACPowerMode(ref g) : PowerSetUserConfiguredDCPowerMode(ref g);
            if (rc != 0)
                throw new InvalidOperationException("Windows 전원 모드 변경 실패 (코드 " + rc + ")");
        }
    }

    static class HpProcess
    {
        const string Name = "HP.SystemControl.Background";

        public static bool IsRunning()
        {
            Process[] ps = Process.GetProcessesByName(Name);
            bool running = ps.Length > 0;
            foreach (Process p in ps) p.Dispose();
            return running;
        }

        public static int Kill()
        {
            int n = 0;
            foreach (Process p in Process.GetProcessesByName(Name))
            {
                try { p.Kill(); p.WaitForExit(3000); n++; }
                catch { }
                finally { p.Dispose(); }
            }
            return n;
        }
    }

    static class Modes
    {
        public static readonly ModeInfo[] All = new ModeInfo[]
        {
            new ModeInfo { Mode = Mode.Auto, Name = "Smart Sense", SupportBit = 1,
                Aliases = new[] { "smartsense", "smart", "auto" },
                Description = "사용하는 애플리케이션, 노트북 위치, 배터리 상태에 따라 성능·팬 소음·온도를 자동으로 조정합니다." },
            new ModeInfo { Mode = Mode.Balanced, Name = "균형", SupportBit = 2,
                Aliases = new[] { "balanced", "balance" },
                Description = "팬 속도, 성능, 온도를 적절하게 조정합니다." },
            new ModeInfo { Mode = Mode.Cool, Name = "저온", SupportBit = 4,
                Aliases = new[] { "cool" },
                Description = "팬 속도를 높이고 CPU 성능을 낮춰 장치 온도를 낮춥니다." },
            new ModeInfo { Mode = Mode.Quiet, Name = "저소음", SupportBit = 5,
                Aliases = new[] { "quiet" },
                Description = "최소 속도 동작을 유지합니다. CPU 성능이 저하되며 조용한 환경에 적합합니다." },
            new ModeInfo { Mode = Mode.Silent, Name = "저소음", SupportBit = 7,
                Aliases = new[] { "silent", "lownoise" },
                Description = "팬이 최소 속도로 동작합니다. CPU 성능이 저하되며 매우 조용한 환경에 적합합니다." },
            new ModeInfo { Mode = Mode.PowerSaver, Name = "전원 절약", SupportBit = 6,
                Aliases = new[] { "powersaver", "powersave", "saver" },
                Description = "전력을 절약해 배터리 수명을 연장합니다. CPU 성능에 제한이 생깁니다." },
            new ModeInfo { Mode = Mode.Performance, Name = "성능", SupportBit = 3,
                Aliases = new[] { "performance", "perf" },
                Description = "CPU 사용량이 높은 소프트웨어를 사용할 때 팬 속도를 높여 성능을 우선합니다. 팬 소음과 장치 온도가 높아질 수 있습니다." },
        };

        public static ModeInfo Find(Mode m)
        {
            foreach (ModeInfo i in All)
                if (i.Mode == m) return i;
            return null;
        }

        public static ModeInfo Parse(string s)
        {
            s = s.Trim().ToLowerInvariant();
            foreach (ModeInfo i in All)
            {
                if (i.Name.ToLowerInvariant() == s || ((int)i.Mode).ToString() == s)
                    return i;
                foreach (string a in i.Aliases)
                    if (a == s) return i;
            }
            return null;
        }

        // 지원 모드 목록. 지원 정보를 읽을 수 없으면 null
        public static List<ModeInfo> Supported(byte? bits)
        {
            if (!bits.HasValue || (bits.Value & 1) == 0)
                return null;
            List<ModeInfo> list = new List<ModeInfo>();
            foreach (ModeInfo i in All)
                if ((bits.Value & (1 << i.SupportBit)) != 0)
                    list.Add(i);
            return list;
        }

        public static string DisplayName(Mode m, List<ModeInfo> supported)
        {
            if (m == Mode.Silent && supported != null && supported.Exists(i => i.Mode == Mode.Quiet))
                return "저소음 (강화)";
            ModeInfo info = Find(m);
            return info != null ? info.Name : "알 수 없음 (" + (int)m + ")";
        }
    }

    class MainForm : Form
    {
        readonly Label currentLabel = new Label();
        readonly Label statusLabel = new Label();
        readonly CheckBox powerCheck = new CheckBox();
        readonly Button applyButton = new Button();
        readonly Button refreshButton = new Button();
        readonly Label hpProcLabel = new Label();
        readonly Button killButton = new Button();
        readonly Timer verifyTimer = new Timer();
        Mode pendingMode;
        readonly Dictionary<Mode, RadioButton> radios = new Dictionary<Mode, RadioButton>();
        List<ModeInfo> supported;

        public MainForm()
        {
            Text = "HP 시스템 관리 모드 선택";
            Font = new Font("Malgun Gothic", 9F);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ClientSize = new Size(500, 560);

            Label title = new Label();
            title.Text = "시스템 관리 모드";
            title.Font = new Font("Malgun Gothic", 13F, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(16, 14);
            Controls.Add(title);

            currentLabel.AutoSize = true;
            currentLabel.Location = new Point(18, 48);
            currentLabel.Text = "현재 상태: 확인 중...";
            Controls.Add(currentLabel);

            supported = Modes.Supported(HpBios.GetSupportByte());
            int y = 76;
            foreach (ModeInfo info in Modes.All)
            {
                if (supported != null && !supported.Contains(info))
                    continue;

                RadioButton rb = new RadioButton();
                rb.Text = Modes.DisplayName(info.Mode, supported);
                rb.Font = new Font("Malgun Gothic", 10F, FontStyle.Bold);
                rb.AutoSize = true;
                rb.Location = new Point(20, y);
                rb.Tag = info;
                radios[info.Mode] = rb;
                Controls.Add(rb);

                Label desc = new Label();
                desc.Text = info.Description;
                desc.ForeColor = Color.DimGray;
                desc.Location = new Point(42, y + 24);
                desc.Size = new Size(440, 34);
                Controls.Add(desc);

                y += 68;
            }

            powerCheck.Text = "Windows 전원 모드도 함께 변경 (성능/전원 절약 선택 시)";
            powerCheck.Checked = false;
            powerCheck.AutoSize = true;
            powerCheck.Location = new Point(20, y + 6);
            Controls.Add(powerCheck);

            applyButton.Text = "적용";
            applyButton.Size = new Size(110, 32);
            applyButton.Location = new Point(20, y + 38);
            applyButton.Click += delegate { Apply(); };
            Controls.Add(applyButton);

            refreshButton.Text = "새로고침";
            refreshButton.Size = new Size(110, 32);
            refreshButton.Location = new Point(140, y + 38);
            refreshButton.Click += delegate { RefreshCurrent(); };
            Controls.Add(refreshButton);

            Label sep = new Label();
            sep.BorderStyle = BorderStyle.Fixed3D;
            sep.Location = new Point(20, y + 84);
            sep.Size = new Size(460, 2);
            Controls.Add(sep);

            hpProcLabel.AutoSize = false;
            hpProcLabel.Location = new Point(20, y + 96);
            hpProcLabel.Size = new Size(335, 52);
            Controls.Add(hpProcLabel);

            killButton.Text = "HP 프로세스 종료";
            killButton.Size = new Size(120, 32);
            killButton.Location = new Point(360, y + 96);
            killButton.Click += delegate { KillHp(); };
            Controls.Add(killButton);

            statusLabel.AutoSize = false;
            statusLabel.Location = new Point(20, y + 154);
            statusLabel.Size = new Size(460, 44);
            Controls.Add(statusLabel);

            ClientSize = new Size(500, y + 204);

            verifyTimer.Interval = 3000;
            verifyTimer.Tick += delegate { Verify(); };
            UpdateHpProcessLabel();

            if (supported == null)
                SetStatus("지원 모드 정보를 읽지 못해 모든 모드를 표시합니다.", false);

            Shown += delegate { RefreshCurrent(); };
        }

        void SetStatus(string text, bool error)
        {
            statusLabel.ForeColor = error ? Color.Firebrick : Color.DarkGreen;
            statusLabel.Text = text;
        }

        void UpdateHpProcessLabel()
        {
            bool running = HpProcess.IsRunning();
            hpProcLabel.Text = running
                ? "HP.SystemControl.Background 실행 중\n전원 모드 변경 등의 이벤트가 생기면 HP가 자신이 저장한 모드로 되돌릴 수 있습니다."
                : "HP.SystemControl.Background 실행 중이 아님";
            hpProcLabel.ForeColor = running ? Color.DarkOrange : Color.DarkGreen;
            killButton.Enabled = running;
        }

        void KillHp()
        {
            int n = HpProcess.Kill();
            UpdateHpProcessLabel();
            SetStatus(n > 0 ? "HP 백그라운드 프로세스를 종료했습니다. (myHP 앱을 열면 다시 실행될 수 있음)" : "종료할 프로세스가 없습니다.", n == 0);
        }

        void Verify()
        {
            verifyTimer.Stop();
            try
            {
                int now = HpBios.GetMode();
                currentLabel.Text = "현재 상태: " + Modes.DisplayName((Mode)now, supported);
                RadioButton rb;
                if (radios.TryGetValue((Mode)now, out rb)) rb.Checked = true;
                if (now != (int)pendingMode)
                    SetStatus("선택한 모드가 HP 프로세스에 의해 다른 값으로 되돌려졌습니다. 아래 \"HP 프로세스 종료\" 후 다시 적용하세요.", true);
            }
            catch (Exception ex) { SetStatus(ex.Message, true); }
            UpdateHpProcessLabel();
        }

        void RefreshCurrent()
        {
            UpdateHpProcessLabel();
            try
            {
                int v = HpBios.GetMode();
                Mode m = (Mode)v;
                currentLabel.Text = "현재 상태: " + Modes.DisplayName(m, supported);
                RadioButton rb;
                if (radios.TryGetValue(m, out rb))
                    rb.Checked = true;
            }
            catch (Exception ex)
            {
                currentLabel.Text = "현재 상태: 읽기 실패";
                SetStatus(ex.Message, true);
            }
        }

        void Apply()
        {
            ModeInfo selected = null;
            foreach (RadioButton rb in radios.Values)
                if (rb.Checked) selected = (ModeInfo)rb.Tag;
            if (selected == null)
            {
                SetStatus("적용할 모드를 선택하세요.", true);
                return;
            }

            try
            {
                Cursor = Cursors.WaitCursor;
                HpBios.SetMode(selected.Mode);
                string warn = "";
                if (powerCheck.Checked)
                {
                    try { WinPower.Apply(selected.Mode); }
                    catch (Exception ex) { warn = " (" + ex.Message + ")"; }
                }
                System.Threading.Thread.Sleep(300);
                int now = HpBios.GetMode();
                currentLabel.Text = "현재 상태: " + Modes.DisplayName((Mode)now, supported);
                if (now == (int)selected.Mode)
                {
                    SetStatus("'" + Modes.DisplayName(selected.Mode, supported) + "' 모드로 변경했습니다." + warn, false);
                    pendingMode = selected.Mode;
                    verifyTimer.Stop();
                    verifyTimer.Start();
                }
                else
                    SetStatus("변경 요청은 성공했지만 BIOS 가 다른 값(" + now + ")을 보고합니다." + warn, true);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }
    }

    static class Program
    {
        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int pid);

        static void Print(string s)
        {
            Console.WriteLine(s);
        }

        static int RunCli(string[] args)
        {
            AttachConsole(-1);

            List<ModeInfo> supported = Modes.Supported(HpBios.GetSupportByte());
            string cmd = args[0].ToLowerInvariant();
            try
            {
                if (cmd == "--get")
                {
                    Print(Modes.DisplayName((Mode)HpBios.GetMode(), supported));
                    return 0;
                }
                if (cmd == "--killhp")
                {
                    Print("종료한 HP 프로세스: " + HpProcess.Kill());
                    return 0;
                }
                if (cmd == "--list")
                {
                    foreach (ModeInfo i in Modes.All)
                        if (supported == null || supported.Contains(i))
                            Print(i.Aliases[0] + "\t" + Modes.DisplayName(i.Mode, supported));
                    return 0;
                }
                if (cmd == "--set" && args.Length >= 2)
                {
                    ModeInfo info = Modes.Parse(args[1]);
                    if (info == null || (supported != null && !supported.Contains(info)))
                    {
                        Print("지원하지 않는 모드: " + args[1]);
                        return 2;
                    }
                    HpBios.SetMode(info.Mode);
                    if (args.Length >= 3 && args[2].ToLowerInvariant() == "--power")
                    {
                        try { WinPower.Apply(info.Mode); } catch (Exception ex) { Print(ex.Message); }
                    }
                    Print("변경 완료: " + Modes.DisplayName(info.Mode, supported));
                    return 0;
                }
                Print("사용법: HpThermalMode.exe [--get | --list | --killhp | --set <모드> [--power]]");
                return 1;
            }
            catch (Exception ex)
            {
                Print(ex.Message);
                return 3;
            }
        }

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0)
                return RunCli(args);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "HP 시스템 관리 모드", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            return 0;
        }
    }
}
