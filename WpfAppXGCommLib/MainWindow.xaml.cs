using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using XGCommLib;

namespace WpfAppXGCommLib
{
    public partial class MainWindow : Window
    {
        private readonly CommObjectFactory20 _factory = new CommObjectFactory20();
        private bool _connected = false;
        private string _endpoint = "";

        // [추가] 엘리베이터 창 참조 변수
        private ElevatorWindow _elevatorUI;

        private const bool USE_LITTLE_ENDIAN = true;
        public MainWindow()
        {
            InitializeComponent();

            ComboBox_DataType.SelectedIndex = 0;   // B
            ComboBox_DeviceType.SelectedIndex = 0; // M
            SetConnState(false);

            // [추가] 메인 창 로드 시 엘리베이터 창 띄우기
            this.Loaded += (s, e) =>
            {
                _elevatorUI = new ElevatorWindow();
                _elevatorUI.Owner = this;
                _elevatorUI.Show();
            };
        }

        // ================== 연결 ==================
        private void ButtonConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var ip = (TextBox_Ip.Text ?? "").Trim();
                var portStr = (TextBox_Port.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(ip) || !int.TryParse(portStr, out int port))
                {
                    MessageBox.Show("IP/Port를 확인하세요.");
                    return;
                }

                var ep = $"{ip}:{port}";

                var drv = _factory.GetMLDPCommObject20(ep);
                var ret = drv.Connect("");
                try
                {
                    if (ret != 1)
                    {
                        MessageBox.Show("연결 실패");
                        return;
                    }
                }
                finally { try { drv.Disconnect(); } catch { } }

                _endpoint = ep;
                _connected = true;
                SetConnState(true);
                Append($"Connected (validated): {_endpoint}");
                MessageBox.Show("연결 성공");
            }
            catch (Exception ex)
            {
                MessageBox.Show("연결 오류: " + ex.Message);
            }
        }

        private void ButtonDisconnect_Click(object sender, RoutedEventArgs e)
        {
            _connected = false;
            SetConnState(false);
            Append("Disconnected");
        }

        // ================== 쓰기 ==================

        private void ButtonWrite_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;

            try
            {
                if (!TryGetCommonInputs(out char dataType, out char deviceType, out int offset, out int size))
                    return;

                string raw = (TextBox_WriteValues.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    MessageBox.Show("값을 입력하세요. 예) B: 10 또는 0x0A / W: 1 또는 0x1234");
                    return;
                }

                byte[] elem;

                if (dataType == 'B')
                {
                    var arr = ParseByteList(raw);
                    if (arr == null || arr.Length == 0)
                    {
                        MessageBox.Show("값(바이트)을 입력하세요. 예) 10 또는 0x0A");
                        return;
                    }
                    elem = new byte[] { arr[0] };
                }
                else if (dataType == 'W')
                {
                    var words = ParseUShortList(raw);
                    if (words == null || words.Length == 0)
                    {
                        MessageBox.Show("값(워드)을 입력하세요. 예) 1 또는 0x1234");
                        return;
                    }

                    ushort w = words[0];
                    elem = USE_LITTLE_ENDIAN
                        ? new byte[] { (byte)(w & 0xFF), (byte)((w >> 8) & 0xFF) }
                        : new byte[] { (byte)((w >> 8) & 0xFF), (byte)(w & 0xFF) };
                }
                else
                {
                    MessageBox.Show("지원하지 않는 DataType입니다. (B/W만 지원)");
                    return;
                }

                int success = 0;

                for (int i = 0; i < size; i++)
                {
                    int addr = CalcAddress(offset, i, dataType); // B: +1, W: +2

                    int ret = WithFreshDriver(drv =>
                    {
                        var device = _factory.CreateDevice();
                        device.ucDataType = (byte)dataType;        // 'B' or 'W'
                        device.ucDeviceType = (byte)deviceType;    // M/R/W
                        device.lOffset = addr;
                        device.lSize = 1;
                        drv.AddDeviceInfo(device);
                        return drv.WriteRandomDevice(elem);
                    });
                    Append($"WRITE i={i} [{(char)deviceType}:{(char)dataType}] off={addr} payload={ToHex(elem)} ret={ret}");
                    if (ret == 1) success++;
                }
                bool allOk = (success == size);
                Append($"WRITE summary: {success}/{size} 성공");
                MessageBox.Show(allOk ? "쓰기 성공" : "일부 쓰기 실패");
            }
            catch (Exception ex)
            {
                MessageBox.Show("쓰기 오류: " + ex.Message);
            }
        }

        // ================== 읽기 ==================
        private void ButtonRead_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;

            try
            {
                if (!TryGetCommonInputs(out char dataType, out char deviceType, out int offset, out int size))
                    return;

                int unitBytes = (dataType == 'W') ? 2 : 1;
                var total = new byte[size * unitBytes];

                int success = 0;

                for (int i = 0; i < size; i++)
                {
                    int addr = CalcAddress(offset, i, dataType);
                    var elem = new byte[unitBytes];

                    int ret = WithFreshDriver(drv =>
                    {
                        var device = _factory.CreateDevice();
                        device.ucDataType = (byte)dataType;     // 'B' or 'W'
                        device.ucDeviceType = (byte)deviceType; // M/R/W
                        device.lOffset = addr;
                        device.lSize = 1;

                        drv.AddDeviceInfo(device);
                        return drv.ReadRandomDevice(elem);
                    });

                    Append($"READ  i={i} [{(char)deviceType}:{(char)dataType}] off={addr} ret={ret} payload={ToHex(elem)}");

                    if (ret == 1)
                    {
                        Array.Copy(elem, 0, total, i * unitBytes, unitBytes);
                        success++;
                    }
                }

                Append($"READ summary: {success}/{size} 성공, TOTAL HEX: {ToHex(total)}");
                MessageBox.Show(success == size ? "읽기 성공" : "일부 읽기 실패");
            }
            catch (Exception ex)
            {
                MessageBox.Show("읽기 오류: " + ex.Message);
            }
        }

        // ================== 로그 ==================

        private void ButtonClearLog_Click(object sender, RoutedEventArgs e)
        {
            TextBox_Log.Clear();
        }

        // ================== Helper Methods ==================

        private static int CalcAddress(int baseOffset, int index, char dataType)
        {
            int unitBytes = (dataType == 'W') ? 2 : 1;
            return baseOffset + (index * unitBytes);
        }

        private bool EnsureConnected()
        {
            if (_connected && !string.IsNullOrWhiteSpace(_endpoint)) return true;
            MessageBox.Show("먼저 Connect로 IP/Port를 설정하세요.");
            return false;
        }

        private void SetConnState(bool connected)
        {
            ButtonConnect.IsEnabled = !connected;
            ButtonDisconnect.IsEnabled = connected;
            ButtonRead.IsEnabled = connected;
            ButtonWrite.IsEnabled = connected;

            Text_ConnState.Text = connected ? "Connected" : "Disconnected";
            Text_ConnState.Foreground = connected
                ? System.Windows.Media.Brushes.SeaGreen
                : System.Windows.Media.Brushes.Tomato;
        }

        private bool TryGetCommonInputs(out char dataType, out char deviceType, out int offset, out int size)
        {
            dataType = 'B'; deviceType = 'M'; offset = 0; size = 1;

            if (!(ComboBox_DataType.SelectedItem is ComboBoxItem dtItem) ||
                !(ComboBox_DeviceType.SelectedItem is ComboBoxItem devItem))
            {
                MessageBox.Show("DataType/DeviceType을 선택하세요.");
                return false;
            }

            dataType = dtItem.Content.ToString()[0];   // 'B' or 'W'
            deviceType = devItem.Content.ToString()[0];

            if (!int.TryParse((TextBox_ByteOffset.Text ?? "").Trim(), out offset) || offset < 0)
            {
                MessageBox.Show("Offset이 올바르지 않습니다.");
                return false;
            }
            if (!int.TryParse((TextBox_Size.Text ?? "").Trim(), out size) || size <= 0)
            {
                MessageBox.Show("Size가 올바르지 않습니다. (B:바이트수 / W:워드수)");
                return false;
            }
            return true;
        }

        private T WithFreshDriver<T>(Func<CommObject20, T> work)
        {
            if (string.IsNullOrWhiteSpace(_endpoint))
                throw new InvalidOperationException("엔드포인트가 설정되지 않았습니다.");

            CommObject20 drv = null;
            try
            {
                drv = _factory.GetMLDPCommObject20(_endpoint);
                int c = drv.Connect("");
                if (c != 1) throw new Exception("작업용 연결 실패");
                return work(drv);
            }
            finally
            {
                try { drv?.Disconnect(); } catch { }
            }
        }

        private void Append(string msg)
        {
            TextBox_Log.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
            TextBox_Log.ScrollToEnd();
        }

        private static string ToHex(byte[] data)
        {
            if (data == null || data.Length == 0) return string.Empty;
            var sb = new StringBuilder(data.Length * 3);
            foreach (var b in data) sb.Append(b.ToString("X2")).Append(' ');
            return sb.ToString().TrimEnd();
        }

        private static byte[] ParseByteList(string text)
        {
            try
            {
                var list = new List<byte>();
                var parts = text.Split(new[] { ',', ' ', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var p in parts)
                {

                    if (p.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        if (byte.TryParse(p.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out byte hx))
                            list.Add(hx);
                        else return null;
                    }
                    else
                    {
                        if (int.TryParse(p, out int v) && v >= 0 && v <= 255)
                            list.Add((byte)v);
                        else return null;
                    }
                }
                return list.ToArray();
            }
            catch { return null; }
        }

        private static ushort[] ParseUShortList(string text)
        {
            try
            {
                var list = new List<ushort>();
                var parts = text.Split(new[] { ',', ' ', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var p in parts)
                {
                    if (p.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        if (ushort.TryParse(p.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out ushort hx))
                            list.Add(hx);
                        else return null;
                    }
                    else
                    {
                        if (int.TryParse(p, out int v) && v >= 0 && v <= 65535)
                            list.Add((ushort)v);
                        else return null;
                    }
                }
                return list.ToArray();
            }
            catch { return null; }
        }
    }
}