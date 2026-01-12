using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using XGCommLib;

namespace WpfAppPLC
{
    public partial class MainWindow : Window
    {
        // ============================================================
        // 1) PLC 디바이스 주소(엑셀/래더 기준)
        // ============================================================

        // ---- 버튼 입력(명령) : C#에서 펄스로 써야 하는 %MX 비트 ----
        private const int MX_DOOR_CLOSE_BTN = 1;   // %MX1  문닫힘버튼
        private const int MX_DOOR_OPEN_BTN = 2;   // %MX2  문열림버튼
        private const int MX_F_BTN = 10;  // %MX10 1층 내부 버튼(F_BTN)
        private const int MX_S_BTN = 11;  // %MX11 2층 내부 버튼(S_BTN)
        private const int MX_UP_BTN_1F = 18;  // %MX18 1층 외부 UP
        private const int MX_DOWN_BTN_2F = 19;  // %MX19 2층 외부 DOWN

        // ---- 상태 표시(모니터링) : 읽기 전용 %MX 비트 ----
        private const int MX_FIR_FLOOR = 20;  // %MX20 1층 상태
        private const int MX_SEC_FLOOR = 21;  // %MX21 2층 상태
        private const int MX_DOOR_CLOSED = 30;  // %MX30 문닫힘 상태
        private const int MX_DOOR_OPENED = 31;  // %MX31 문열림 상태
        private const int MX_AUTO_DOOR_SAVE = 32;  // %MX32 자동문열림저장
        private const int MX_EV_MOVING = 34;  // %MX34 EV이동중

        // ---- 이동 목표층 판단용 HOLD ----
        private const int MX_FIR_HOLD = 36;  // %MX36 1층으로 이동중(유지)
        private const int MX_SEC_HOLD = 37;  // %MX37 2층으로 이동중(유지)

        private const int MX_DOOR_CLOSE_MTR = 40;  // %MX40 문닫힘모터
        private const int MX_DOOR_OPEN_MTR = 41;  // %MX41 문열림모터

        // (옵션) 외부 호출 관련 상태 비트
        private const int MX_EXT_UP = 27;  // %MX27 외부UP버튼(저장/상태)
        private const int MX_EXT_UP_2F = 47;  // %MX47 외부UP버튼2층
        private const int MX_EXT_DOWN_H = 28;  // %MX28 외부downH버튼
        private const int MX_EXT_DOWN_NH_2F = 48;  // %MX48 외부DOWNNH버튼2층

        // ---- 이벤트 코드(엑셀 1~20 저장) : %MB100 ----
        private const int MB_EVENT_CODE = 100; // %MB100 (1바이트)

        // ============================================================
        // 2) XGCommLib 통신 객체
        // ============================================================

        private readonly CommObjectFactory20 _factory = new CommObjectFactory20();

        // 상태 읽기 전용 드라이버
        private CommObject20? _drvStatus;
        private readonly List<DeviceKey> _statusOrder = new();
        private int _statusBufLen;

        // 명령 쓰기 전용 드라이버
        private CommObject20? _drvCmd;
        private readonly List<DeviceKey> _cmdOrder = new();
        private int _cmdBufLen;
        private bool _cmdUsesOneShot = false;

        private readonly SemaphoreSlim _cmdLock = new(1, 1);
        private CancellationTokenSource? _pollCts;

        private PlcStatus _last = new PlcStatus();

        // ============================================================
        // 3) 시각화(애니메이션) 파라미터
        // ============================================================

        private const double CAR_Y_1F = 0.0;
        private const double CAR_Y_2F = -380.0;

        private const double DOOR_OPEN_X = 60.0;

        private static readonly TimeSpan TRAVEL_TIME = TimeSpan.FromSeconds(7);
        private static readonly TimeSpan DOOR_TIME = TimeSpan.FromSeconds(3);

        private bool _carAnimating = false;
        private DateTime _carAnimStart;
        private double _carFromY, _carToY;

        private bool _doorAnimating = false;
        private DateTime _doorAnimStart;
        private bool _doorOpening = false;

        public MainWindow()
        {
            InitializeComponent();
            Closing += (_, __) => SafeDisconnect();
            SetDisconnectedUi();
        }

        // ============================================================
        // 4) UI 이벤트 핸들러
        // ============================================================

        private void Button_Connect_Click(object sender, RoutedEventArgs e)
        {
            string ip = (TextBox_IP.Text ?? "").Trim();
            string port = (TextBox_Port.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(ip)) ip = "127.0.0.1";
            if (string.IsNullOrWhiteSpace(port)) port = "2004";

            string endpoint = $"{ip}:{port}";

            try
            {
                Connect(endpoint);
                AppendLog($"[연결] 성공: {endpoint}");
            }
            catch (Exception ex)
            {
                AppendLog($"[연결] 실패: {ex.Message}");
                MessageBox.Show(ex.Message, "연결 실패", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Button_Disconnect_Click(object sender, RoutedEventArgs e)
        {
            SafeDisconnect();
            AppendLog("[연결] 해제");
        }

        private void Button_ClearLog_Click(object sender, RoutedEventArgs e)
        {
            TextBox_Log.Clear();
        }

        // ---- 내부 패널 ----
        private async void Button_DoorOpen_Click(object sender, RoutedEventArgs e)
            => await PulseMX(MX_DOOR_OPEN_BTN, "문 열기", onMs: 80);

        private async void Button_DoorClose_Click(object sender, RoutedEventArgs e)
            => await PulseMX(MX_DOOR_CLOSE_BTN, "문 닫기", onMs: 80);

        private async void Button_Floor1_Click(object sender, RoutedEventArgs e)
            => await PulseMX(MX_F_BTN, "내부 1층", onMs: 80);

        private async void Button_Floor2_Click(object sender, RoutedEventArgs e)
            => await PulseMX(MX_S_BTN, "내부 2층", onMs: 80);

        // ---- 외부 호출 ----
        private async void Button_Up1F_Click(object sender, RoutedEventArgs e)
            => await PulseMX(MX_UP_BTN_1F, "외부 1F ▲", onMs: 80);

        private async void Button_Down2F_Click(object sender, RoutedEventArgs e)
            => await PulseMX(MX_DOWN_BTN_2F, "외부 2F ▼", onMs: 80);

        // ============================================================
        // 5) PLC 연결/해제
        // ============================================================

        private void Connect(string endpoint)
        {
            SafeDisconnect();

            // (1) 상태 읽기 드라이버 연결
            _drvStatus = _factory.GetMLDPCommObject20(endpoint);
            if (_drvStatus.Connect("") != 1)
                throw new InvalidOperationException("상태읽기용 드라이버 Connect() 실패");

            BuildStatusDeviceList();

            // (2) 명령 쓰기 드라이버 연결
            _drvCmd = _factory.GetMLDPCommObject20(endpoint);
            if (_drvCmd.Connect("") == 1)
            {
                _cmdUsesOneShot = false;
                BuildCmdDeviceList();
            }
            else
            {
                _cmdUsesOneShot = true;
                _drvCmd = null;
                AppendLog("[경고] 명령용 2번째 연결 실패 → 버튼은 OneShot(임시 연결) 방식으로 전송합니다.");
            }

            StartPolling();
            SetConnectedUi();
        }

        private void SafeDisconnect()
        {
            try
            {
                StopPolling();

                if (_drvCmd != null)
                {
                    try { _drvCmd.Disconnect(); } catch { }
                    ReleaseCom(_drvCmd);
                    _drvCmd = null;
                }

                if (_drvStatus != null)
                {
                    try { _drvStatus.Disconnect(); } catch { }
                    ReleaseCom(_drvStatus);
                    _drvStatus = null;
                }
            }
            catch { }

            _statusOrder.Clear();
            _cmdOrder.Clear();
            _statusBufLen = 0;
            _cmdBufLen = 0;

            _last = new PlcStatus();

            Dispatcher.Invoke(() =>
            {
                CarTranslate.Y = CAR_Y_1F;
                DoorLeftTransform.X = 0;
                DoorRightTransform.X = 0;

                Text_CarFloor.Text = "-";
                Text_CarRoute.Text = "";          // [추가] CAR 내부 이동표시 라인 초기화
                Text_CurrentFloor.Text = "-";
                Text_Moving.Text = "-";
                Text_TargetFloor.Text = "-";
                Text_Door.Text = "-";
                TextEventCode.Text = "-";
                Text_LastCmd.Text = "마지막 이벤트(MB100): -";

                Lamp_Moving.Fill = BrushFrom("#FFE2E8F0");
                Lamp_Door.Fill = BrushFrom("#FFE2E8F0");
            });

            SetDisconnectedUi();
        }

        // ============================================================
        // 6) 디바이스 목록 구성(AddDeviceInfo)
        // ============================================================

        private void BuildStatusDeviceList()
        {
            if (_drvStatus == null) return;

            _statusOrder.Clear();

            AddStatusMx(MX_FIR_FLOOR);
            AddStatusMx(MX_SEC_FLOOR);

            AddStatusMx(MX_DOOR_CLOSED);
            AddStatusMx(MX_DOOR_OPENED);

            AddStatusMx(MX_DOOR_CLOSE_MTR);
            AddStatusMx(MX_DOOR_OPEN_MTR);

            AddStatusMx(MX_EV_MOVING);

            AddStatusMx(MX_FIR_HOLD);
            AddStatusMx(MX_SEC_HOLD);

            AddStatusMx(MX_AUTO_DOOR_SAVE);

            AddStatusMx(MX_EXT_UP);
            AddStatusMx(MX_EXT_UP_2F);
            AddStatusMx(MX_EXT_DOWN_H);
            AddStatusMx(MX_EXT_DOWN_NH_2F);

            AddStatusMb(MB_EVENT_CODE);

            _statusBufLen = _statusOrder.Count;
        }

        private void BuildCmdDeviceList()
        {
            if (_drvCmd == null) return;

            _cmdOrder.Clear();

            AddCmdMx(MX_DOOR_CLOSE_BTN);
            AddCmdMx(MX_DOOR_OPEN_BTN);
            AddCmdMx(MX_F_BTN);
            AddCmdMx(MX_S_BTN);
            AddCmdMx(MX_UP_BTN_1F);
            AddCmdMx(MX_DOWN_BTN_2F);

            _cmdBufLen = _cmdOrder.Count;
        }

        private void AddStatusMx(int mxBit)
        {
            if (_drvStatus == null) return;
            AddMxBitDevice(_drvStatus, mxBit);
            _statusOrder.Add(DeviceKey.Mx(mxBit));
        }

        private void AddStatusMb(int mbByte)
        {
            if (_drvStatus == null) return;
            AddMbByteDevice(_drvStatus, mbByte);
            _statusOrder.Add(DeviceKey.Mb(mbByte));
        }

        private void AddCmdMx(int mxBit)
        {
            if (_drvCmd == null) return;
            AddMxBitDevice(_drvCmd, mxBit);
            _cmdOrder.Add(DeviceKey.Mx(mxBit));
        }

        private void AddMxBitDevice(CommObject20 drv, int mxBit)
        {
            var dev = _factory.CreateDevice();
            dev.ucDeviceType = (byte)'M';
            dev.ucDataType = (byte)'X';
            dev.lOffset = mxBit / 8;
            dev.lSize = mxBit % 8;
            drv.AddDeviceInfo(dev);
        }

        private void AddMbByteDevice(CommObject20 drv, int mbByteOffset)
        {
            var dev = _factory.CreateDevice();
            dev.ucDeviceType = (byte)'M';
            dev.ucDataType = (byte)'B';
            dev.lOffset = mbByteOffset;
            dev.lSize = 1;
            drv.AddDeviceInfo(dev);
        }

        // ============================================================
        // 7) 제어방법
        // ============================================================

        private void StartPolling()
        {
            StopPolling();

            if (_drvStatus == null) return;
            if (_statusBufLen <= 0) return;

            _pollCts = new CancellationTokenSource();
            var token = _pollCts.Token;

            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var st = ReadStatusOnce();
                        if (st.HasValue)
                        {
                            var prev = _last;
                            _last = st.Value;
                            UpdateUiFromStatus(prev, _last);
                        }
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"[폴링] 예외: {ex.Message}");
                    }

                    await Task.Delay(100, token);
                }
            }, token);
        }

        private void StopPolling()
        {
            if (_pollCts == null) return;

            try
            {
                _pollCts.Cancel();
                _pollCts.Dispose();
            }
            catch { }
            finally
            {
                _pollCts = null;
            }
        }

        private PlcStatus? ReadStatusOnce()
        {
            if (_drvStatus == null) return null;
            if (_statusBufLen <= 0) return null;

            byte[] buf = new byte[_statusBufLen];

            int ret = _drvStatus.ReadRandomDevice(buf);
            if (ret != 1)
            {
                AppendLog($"[폴링] ReadRandomDevice 실패(ret={ret})");
                return null;
            }

            bool GetMx(int mx) => buf[IndexOfStatus(DeviceKey.Mx(mx))] != 0;
            byte GetMb(int mb) => buf[IndexOfStatus(DeviceKey.Mb(mb))];

            return new PlcStatus
            {
                FirFloor = GetMx(MX_FIR_FLOOR),
                SecFloor = GetMx(MX_SEC_FLOOR),

                DoorClosed = GetMx(MX_DOOR_CLOSED),
                DoorOpened = GetMx(MX_DOOR_OPENED),

                DoorCloseMotor = GetMx(MX_DOOR_CLOSE_MTR),
                DoorOpenMotor = GetMx(MX_DOOR_OPEN_MTR),

                EvMoving = GetMx(MX_EV_MOVING),

                FirHold = GetMx(MX_FIR_HOLD),
                SecHold = GetMx(MX_SEC_HOLD),

                AutoDoorSave = GetMx(MX_AUTO_DOOR_SAVE),

                ExtUp = GetMx(MX_EXT_UP),
                ExtUp2F = GetMx(MX_EXT_UP_2F),
                ExtDownH = GetMx(MX_EXT_DOWN_H),
                ExtDownNh2F = GetMx(MX_EXT_DOWN_NH_2F),

                EventCode = GetMb(MB_EVENT_CODE)
            };
        }

        private int IndexOfStatus(DeviceKey key)
        {
            int idx = _statusOrder.IndexOf(key);
            if (idx < 0) throw new InvalidOperationException($"상태 디바이스 인덱스를 찾지 못했습니다: {key}");
            return idx;
        }

        // ============================================================
        // 8) 버튼 입력: MX 펄스(0→1→0)
        // ============================================================

        private async Task PulseMX(int mxBit, string label, int onMs = 80)
        {
            if (_last.EvMoving && (mxBit == MX_DOOR_OPEN_BTN || mxBit == MX_DOOR_CLOSE_BTN))
            {
                AppendLog($"[차단] EV 이동중 → '{label}' 명령 무시");
                return;
            }

            await _cmdLock.WaitAsync();
            try
            {
                AppendLog($"[명령] {label} : ON");
                await WriteMxBit(mxBit, true);

                await Task.Delay(onMs);

                AppendLog($"[명령] {label} : OFF");
                await WriteMxBit(mxBit, false);
            }
            catch (Exception ex)
            {
                AppendLog($"[명령] {label} 예외: {ex.Message}");
            }
            finally
            {
                _cmdLock.Release();
            }
        }

        private async Task WriteMxBit(int mxBit, bool value)
        {
            if (!_cmdUsesOneShot && _drvCmd != null)
            {
                int idx = _cmdOrder.IndexOf(DeviceKey.Mx(mxBit));
                if (idx < 0) throw new InvalidOperationException($"명령 디바이스 목록에 MX{mxBit}가 없습니다.");

                byte[] wbuf = new byte[_cmdBufLen];
                wbuf[idx] = (byte)(value ? 1 : 0);

                int ret = _drvCmd.WriteRandomDevice(wbuf);
                if (ret != 1) throw new InvalidOperationException($"WriteRandomDevice 실패(ret={ret})");
                return;
            }

            await Task.Run(() =>
            {
                CommObject20? drv = null;
                try
                {
                    string ip = (TextBox_IP.Text ?? "").Trim();
                    string port = (TextBox_Port.Text ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(ip)) ip = "192.168.0.201";
                    if (string.IsNullOrWhiteSpace(port)) port = "2004";
                    string endpoint = $"{ip}:{port}";

                    drv = _factory.GetMLDPCommObject20(endpoint);
                    if (drv.Connect("") != 1)
                        throw new InvalidOperationException("OneShot Connect() 실패");

                    AddMxBitDevice(drv, mxBit);

                    byte[] wbuf = new byte[1];
                    wbuf[0] = (byte)(value ? 1 : 0);

                    int ret = drv.WriteRandomDevice(wbuf);
                    if (ret != 1) throw new InvalidOperationException($"OneShot WriteRandomDevice 실패(ret={ret})");
                }
                finally
                {
                    if (drv != null)
                    {
                        try { drv.Disconnect(); } catch { }
                        ReleaseCom(drv);
                    }
                }
            });
        }

        // ============================================================
        // 9) UI 갱신 + 시각화 갱신
        //    [핵심 추가] CAR 내부에 Hold 기반 방향/목표층 표시
        // ============================================================

        private void UpdateUiFromStatus(PlcStatus prev, PlcStatus cur)
        {
            Dispatcher.Invoke(() =>
            {
                // ------------------------------
                // (1) 현재층 계산(실제 층 상태 비트 기준)
                // ------------------------------
                int curFloor = cur.FirFloor ? 1 : (cur.SecFloor ? 2 : 0);
                int prevFloor = prev.FirFloor ? 1 : (prev.SecFloor ? 2 : 0);

                Text_CurrentFloor.Text = curFloor == 0 ? "-" : curFloor.ToString();

                // ------------------------------
                // (2) 목표층(Hold 기반)
                // ------------------------------
                int targetFloor =
                    cur.SecHold ? 2 :
                    cur.FirHold ? 1 : 0;

                Text_TargetFloor.Text = targetFloor == 0 ? "-" : $"{targetFloor}F";

                // ------------------------------
                // (3) 이동 텍스트(좌측 상태)
                // ------------------------------
                if (cur.EvMoving)
                {
                    if (targetFloor == 2) Text_Moving.Text = "2층으로 이동중";
                    else if (targetFloor == 1) Text_Moving.Text = "1층으로 이동중";
                    else Text_Moving.Text = "이동중";
                }
                else
                {
                    Text_Moving.Text = "정지";
                }

                Lamp_Moving.Fill = cur.EvMoving ? BrushFrom("#FF2563EB") : BrushFrom("#FFE2E8F0");

                // ------------------------------
                // (4) CAR 내부 표시(요청하신 부분)
                // ------------------------------
                // - 층 비트가 이동 중에는 꺼져서 '-'가 뜨는 경우가 많아서
                //   이동중에는 Hold로 "↓ 1F / ↑ 2F"를 CAR 상단에 표시합니다.
                // - 아래 작은 문구로 "2F → 1F 이동중" 같이 보기 좋게 보강합니다.
                if (cur.EvMoving)
                {
                    // 메인(큰 글자): 방향 + 목표층
                    if (targetFloor == 2) Text_CarFloor.Text = "↑ 2F";
                    else if (targetFloor == 1) Text_CarFloor.Text = "↓ 1F";
                    else Text_CarFloor.Text = "이동중";

                    // 서브(작은 글자): 출발층→목표층(가능하면)
                    if (prevFloor != 0 && targetFloor != 0)
                        Text_CarRoute.Text = $"{prevFloor}F → {targetFloor}F 이동중";
                    else if (targetFloor != 0)
                        Text_CarRoute.Text = $"{targetFloor}F로 이동중";
                    else
                        Text_CarRoute.Text = "이동중";
                }
                else
                {
                    // 정지 시에는 실제 층 표시로 복귀
                    Text_CarFloor.Text = curFloor == 0 ? "-" : $"{curFloor}F";
                    Text_CarRoute.Text = ""; // 정지 시 서브 문구 숨김(깔끔하게)
                }

                // ------------------------------
                // (5) 문 상태
                // ------------------------------
                string doorText =
                    cur.DoorOpenMotor ? "여는중" :
                    cur.DoorCloseMotor ? "닫는중" :
                    cur.DoorOpened ? "열림" :
                    cur.DoorClosed ? "닫힘" : "-";

                Text_Door.Text = doorText;

                bool doorLampOn = cur.DoorOpened || cur.DoorClosed || cur.DoorOpenMotor || cur.DoorCloseMotor;
                Lamp_Door.Fill = doorLampOn ? BrushFrom("#FF2563EB") : BrushFrom("#FFE2E8F0");

                // ------------------------------
                // (6) 이벤트 코드(MB100)
                // ------------------------------
                TextEventCode.Text = cur.EventCode.ToString();
                Text_LastCmd.Text = $"마지막 이벤트(MB100): {cur.EventCode}";

                // ------------------------------
                // (7) 애니메이션 트리거
                // ------------------------------
                if (cur.EvMoving && !prev.EvMoving)
                {
                    _carAnimating = true;
                    _carAnimStart = DateTime.Now;

                    _carFromY = prev.SecFloor ? CAR_Y_2F : CAR_Y_1F;

                    if (cur.SecHold) _carToY = CAR_Y_2F;
                    else if (cur.FirHold) _carToY = CAR_Y_1F;
                    else _carToY = _carFromY;
                }

                if (cur.DoorOpenMotor && !prev.DoorOpenMotor)
                {
                    _doorAnimating = true;
                    _doorAnimStart = DateTime.Now;
                    _doorOpening = true;
                }

                if (cur.DoorCloseMotor && !prev.DoorCloseMotor)
                {
                    _doorAnimating = true;
                    _doorAnimStart = DateTime.Now;
                    _doorOpening = false;
                }

                ApplyVisualizationFrame(cur);
            });
        }

        private void ApplyVisualizationFrame(PlcStatus cur)
        {
            if (_carAnimating)
            {
                double t = (DateTime.Now - _carAnimStart).TotalMilliseconds / TRAVEL_TIME.TotalMilliseconds;
                if (t >= 1.0)
                {
                    t = 1.0;
                    _carAnimating = false;
                }

                CarTranslate.Y = Lerp(_carFromY, _carToY, EaseInOut(t));
            }
            else
            {
                if (cur.SecFloor) CarTranslate.Y = CAR_Y_2F;
                else if (cur.FirFloor) CarTranslate.Y = CAR_Y_1F;
            }

            if (_doorAnimating)
            {
                double t = (DateTime.Now - _doorAnimStart).TotalMilliseconds / DOOR_TIME.TotalMilliseconds;
                if (t >= 1.0)
                {
                    t = 1.0;
                    _doorAnimating = false;
                }

                double p = _doorOpening ? t : (1.0 - t);
                p = EaseInOut(p);

                DoorLeftTransform.X = -DOOR_OPEN_X * p;
                DoorRightTransform.X = DOOR_OPEN_X * p;
            }
            else
            {
                if (cur.DoorOpened)
                {
                    DoorLeftTransform.X = -DOOR_OPEN_X;
                    DoorRightTransform.X = DOOR_OPEN_X;
                }
                else if (cur.DoorClosed)
                {
                    DoorLeftTransform.X = 0;
                    DoorRightTransform.X = 0;
                }
            }
        }

        // ============================================================
        // 10) UI 연결상태 표시
        // ============================================================

        private void SetConnectedUi()
        {
            Dispatcher.Invoke(() =>
            {
                ButtonConnect.IsEnabled = false;
                ButtonDisconnect.IsEnabled = true;
                Text_ConnState.Text = "Connected";
                Text_ConnState.Foreground = BrushFrom("#FF059669");
            });
        }

        private void SetDisconnectedUi()
        {
            Dispatcher.Invoke(() =>
            {
                ButtonConnect.IsEnabled = true;
                ButtonDisconnect.IsEnabled = false;
                Text_ConnState.Text = "Disconnected";
                Text_ConnState.Foreground = BrushFrom("Crimson");
            });
        }

        // ============================================================
        // 11) 로그
        // ============================================================

        private void AppendLog(string msg)
        {
            Dispatcher.Invoke(() =>
            {
                string line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}";
                TextBox_Log.AppendText(line);
                TextBox_Log.ScrollToEnd();
            });
        }

        // ============================================================
        // 12) COM 해제
        // ============================================================

        private static void ReleaseCom(object com)
        {
            try
            {
                if (Marshal.IsComObject(com))
                    Marshal.FinalReleaseComObject(com);
            }
            catch { }
        }

        // ============================================================
        // 13) 시각화 보조 함수들
        // ============================================================

        private static SolidColorBrush BrushFrom(string color)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        private static double EaseInOut(double t)
        {
            return t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
        }

        // ============================================================
        // 내부 자료구조
        // ============================================================

        private readonly struct DeviceKey : IEquatable<DeviceKey>
        {
            public readonly char Area;  // 'X' or 'B'
            public readonly int Index;

            private DeviceKey(char area, int index) { Area = area; Index = index; }

            public static DeviceKey Mx(int bit) => new DeviceKey('X', bit);
            public static DeviceKey Mb(int byt) => new DeviceKey('B', byt);

            public bool Equals(DeviceKey other) => Area == other.Area && Index == other.Index;
            public override bool Equals(object? obj) => obj is DeviceKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Area, Index);
            public override string ToString() => Area == 'X' ? $"MX{Index}" : $"MB{Index}";
        }

        private struct PlcStatus
        {
            public bool FirFloor;
            public bool SecFloor;

            public bool DoorClosed;
            public bool DoorOpened;
            public bool DoorCloseMotor;
            public bool DoorOpenMotor;

            public bool EvMoving;

            public bool FirHold;
            public bool SecHold;
            public bool AutoDoorSave;

            public bool ExtUp;
            public bool ExtUp2F;
            public bool ExtDownH;
            public bool ExtDownNh2F;

            public byte EventCode;
        }
    }
}
