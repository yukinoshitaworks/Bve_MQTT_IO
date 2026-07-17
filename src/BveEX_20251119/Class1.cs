using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BveTypes.ClassWrappers;
using BveEx.Extensions.Native;
using BveEx.Extensions.Native.Input;
using BveEx.PluginHost.Plugins;
using BveEx.PluginHost.Plugins.Extensions;
using BveEx.PluginHost;
using BveEx.PluginHost.Input;
using System.Diagnostics;
using System.Threading;
using System.Reflection;
using MQTTnet;
using MQTTnet.Client;

namespace BveEX_20251119
{
    [Plugin(PluginType.VehiclePlugin)]
    public class PluginMain : AssemblyPluginBase
    {
        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        // ======================
        // ▼ MQTT トピック定義
        // ======================

        // Publish Topics
        private const string TopicTime = "bve/time";
        private const string TopicSpeed = "bve/speed";
        private const string TopicLocation = "bve/location";
        private const string TopicPilot = "bve/pilot";
        private const string TopicPanel = "bve/panel";
        private const string TopicSound = "bve/sound";
        private const string TopicAm = "bve/am";
        private const string TopicBc = "bve/bc";

        // Subscribe Topics
        private const string SubReverser = "bve/reverser";
        private const string SubPower = "bve/power";
        private const string SubBrake = "bve/brake";

        // ======================
        // ▼ MQTT 接続設定
        // ======================
        private const string MqttHost = "localhost";
        private const int MqttPort = 1883;

        // ======================
        // ▼ ログ出力設定
        // ======================
        // プラグインDLLと同じフォルダを基準にした相対パス。
        // 例: BveEx\Plugins\xxx\Log\ 以下にログファイルが作成される。
        private const string RelativeLogDirectory = "Log";

        // panelArray のインデックス（意味を持たせて可読性向上）
        private const int PanelIndexCount = 9; // panelArray[0]～[8] を使用

        private IMqttClient mqttClient;
        private bool isSubscribed = false;

        private readonly string logFilePath;

        static PluginMain()
        {
            AllocConsole();
        }

        public PluginMain(PluginBuilder builder) : base(builder)
        {
            string logDirectory = ResolveLogDirectory();
            Directory.CreateDirectory(logDirectory);

            string logFileTime = DateTime.Now.ToString("yyyyMMddHHmmss");
            logFilePath = Path.Combine(logDirectory, $"{logFileTime}.csv");
        }

        /// <summary>
        /// プラグインDLLと同じフォルダを基準に、相対パスで指定したログフォルダの
        /// フルパスを解決する（フォルダが無い場合は Tick 開始前に自動作成する）。
        /// </summary>
        private static string ResolveLogDirectory()
        {
            string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            return Path.Combine(assemblyDir, RelativeLogDirectory);
        }

        public override async void Dispose()
        {
            if (mqttClient != null && mqttClient.IsConnected)
            {
                await mqttClient.DisconnectAsync();
                Console.WriteLine("MQTT disconnected.");
            }
        }

        public override async void Tick(TimeSpan elapsed)
        {
            await EnsureMqttConnectedAsync();
            await EnsureSubscribedAsync();

            VehicleSnapshot snapshot = CollectVehicleSnapshot();

            WriteLog(snapshot);
            await PublishSnapshotAsync(snapshot);
        }

        /// <summary>
        /// MQTT クライアントが未生成なら生成し、接続とメッセージ受信ハンドラの登録を行う。
        /// （mqttClient は接続完了を待たずに即座に非nullとなるため、次回以降の Tick では再実行されない）
        /// </summary>
        private async Task EnsureMqttConnectedAsync()
        {
            if (mqttClient != null)
                return;

            var factory = new MqttFactory();
            mqttClient = factory.CreateMqttClient();
            mqttClient.ApplicationMessageReceivedAsync += OnMqttMessageReceivedAsync;

            var options = new MqttClientOptionsBuilder()
                .WithTcpServer(MqttHost, MqttPort)
                .WithCleanSession()
                .Build();

            Console.WriteLine("Connecting to MQTT...");
            await mqttClient.ConnectAsync(options);
            Console.WriteLine("MQTT Connected.");
        }

        /// <summary>
        /// 未購読であれば、必要なトピックを一度だけ購読する。
        /// </summary>
        private async Task EnsureSubscribedAsync()
        {
            if (isSubscribed || mqttClient == null || !mqttClient.IsConnected)
                return;

            await mqttClient.SubscribeAsync(SubReverser);
            await mqttClient.SubscribeAsync(SubPower);
            await mqttClient.SubscribeAsync(SubBrake);

            Console.WriteLine("Subscribed: reverser / power / brake");
            isSubscribed = true;
        }

        /// <summary>
        /// MQTT でメッセージを受信した際に、対応するハンドル操作へ反映する。
        /// </summary>
        private async Task OnMqttMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
        {
            try
            {
                string topic = e.ApplicationMessage.Topic;
                string payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);

                Console.WriteLine($"[RECV] {topic} : {payload}");

                if (!int.TryParse(payload, out int val))
                    return;

                var handles = BveHacker.Scenario.Vehicle.Instruments.AtsPlugin.Handles;

                switch (topic)
                {
                    case SubReverser:
                        handles.ReverserPosition = (ReverserPosition)val;
                        break;
                    case SubPower:
                        handles.PowerNotch = val;
                        break;
                    case SubBrake:
                        handles.BrakeNotch = val;
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to handle MQTT message: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 現在フレームの車両状態を BVE から取得し、まとめて返す。
        /// </summary>
        private VehicleSnapshot CollectVehicleSnapshot()
        {
            var instruments = BveHacker.Scenario.Vehicle.Instruments;
            HandleSet handles = instruments.AtsPlugin.Handles;

            return new VehicleSnapshot
            {
                Time = BveHacker.Scenario.TimeManager.TimeMilliseconds,
                Location = BveHacker.Scenario.VehicleLocation.Location,
                Speed = BveHacker.Scenario.VehicleLocation.Speed * 3.6,

                Power = handles.PowerNotch,
                Brake = handles.BrakeNotch,
                Reverser = handles.ReverserPosition,
                ConstantSpeed = handles.ConstantSpeedMode,

                Pilot = instruments.AtsPlugin.Doors.AreAllClosed,
                Ampere = instruments.Electricity.MotorState.Current,
                PowerStepIndex = instruments.Electricity.Performance.Power.CurrentStepIndex,
                BrakeCylinderPressure = instruments.BrakeSystem.Ecb.OutputPressure.Value,

                PanelArray = instruments.AtsPlugin.PanelArray,
                SoundArray = instruments.AtsPlugin.SoundArray
            };
        }

        /// <summary>
        /// 車両状態を CSV 形式でログファイルに追記する。
        /// </summary>
        private void WriteLog(VehicleSnapshot s)
        {
            using (var sw = new StreamWriter(logFilePath, append: true, Encoding.GetEncoding("shift_jis")))
            {
                string panelValues = string.Join(",", s.PanelArray.Take(PanelIndexCount));

                sw.Write(
                    $"{s.Time},{s.Location},{s.Speed},{s.Reverser},{s.Power},{s.Brake},{s.ConstantSpeed},{s.Pilot},{s.Ampere},{s.BrakeCylinderPressure}," +
                    $"{panelValues}\n");
            }
        }

        /// <summary>
        /// 車両状態を MQTT へ Publish する。
        /// </summary>
        private async Task PublishSnapshotAsync(VehicleSnapshot s)
        {
            if (mqttClient == null || !mqttClient.IsConnected)
                return;

            await mqttClient.PublishStringAsync(TopicTime, s.Time.ToString());
            await mqttClient.PublishStringAsync(TopicSpeed, s.Speed.ToString("F2"));
            await mqttClient.PublishStringAsync(TopicLocation, s.Location.ToString("F1"));
            await mqttClient.PublishStringAsync(TopicPilot, s.Pilot ? "1" : "0");
            await mqttClient.PublishStringAsync(TopicAm, s.Ampere.ToString("F1"));
            // await mqttClient.PublishStringAsync(TopicBc, s.BrakeCylinderPressure.ToString("F1"));

            string panelJson = "[" + string.Join(",", s.PanelArray.Take(PanelIndexCount)) + "]";
            await mqttClient.PublishStringAsync(TopicPanel, panelJson);

            string soundJson = "[" + string.Join(",", s.SoundArray[0], s.SoundArray[1], s.SoundArray[3],s.SoundArray[4]) + "]";
            await mqttClient.PublishStringAsync(TopicSound, soundJson);
        }

        /// <summary>
        /// 1フレーム分の車両状態をまとめて保持するデータ構造。
        /// </summary>
        private struct VehicleSnapshot
        {
            public int Time;
            public double Location;
            public double Speed;

            public int Power;
            public int Brake;
            public ReverserPosition Reverser;
            public ConstantSpeedMode ConstantSpeed;

            public bool Pilot;
            public double Ampere;
            public int PowerStepIndex;
            public double BrakeCylinderPressure;

            public int[] PanelArray;
            public int[] SoundArray;
        }
    }

    // MQTT helper extension
    public static class MqttExtensions
    {
        public static Task PublishStringAsync(this IMqttClient client, string topic, string payload)
        {
            var msg = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .Build();

            return client.PublishAsync(msg);
        }
    }
}