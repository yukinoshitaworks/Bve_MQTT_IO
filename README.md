# Bve_MQTT_IO

BVE Trainsim 5 (BveTs5) 用の [BveEx](https://github.com/automatic9045/BveEx) 車両プラグインです。
Bveの走行状態(速度・位置・ノッチ・パネル値・サウンド値など)を MQTT へ Publish し、逆に MQTT 経由でBveの各種状態を外部から操作(Subscribe)できます。BVE と実物機器を MQTT で連携させるための橋渡し役です。

## リポジトリ構成

```
Bve_MQTT_IO/
├─ src/
│  ├─ BveEX_20251119.sln          … Visual Studio ソリューション
│  └─ BveEX_20251119/
│     ├─ Class1.cs                … プラグイン本体（PluginMain）
│     ├─ BveEX_20251119.csproj
│     ├─ packages.config          … NuGet 依存パッケージ一覧
│     └─ config.json              … （現状コード内では未使用。将来の設定外だし用の下書き）
└─ Scenarios/
   └─ Yokokura/
      └─ MQTT_IO/
         ├─ Uchibo20_E217r/       … 実配置例①（内房線E217系）
         └─ Rock_On_115_taka_T1040/ … 実配置例②（Rock_On氏115系 高タカT1040編成）
```

`Scenarios/Yokokura/MQTT_IO/` 配下は、実際にプラグインを組み込んで運用している車両フォルダの実例です。
**`Vehicle.txt` は他の車両フォルダ（例: `Uchibo20\E217r`、`Rock_On\Train\JR\...\Formation\taka`）を相対パスで参照しており、それらの参照先データ自体はこのリポジトリに含まれていません**（BVE路線・車両データは著作権上、再配布しない方針のため）。配置例の参考としてご覧ください。

## 必要環境

- BVE Trainsim 5 (Mackoy's BveTs5) がインストール済みで、 BveEx のランタイムが有効な状態
- .NET Framework 4.8
- ビルドする場合: Visual Studio 2022 (Community 可) または MSBuild
- MQTT ブローカー（例: [Mosquitto](https://mosquitto.org/)）が `localhost:1883` で稼働していること。[Node-RED Dashboard](https://github.com/yukinoshitaworks/Bve_Node-RED_Dashboard)の構築も併せてご参照ください。
  - ホスト/ポートを変えたい場合は `Class1.cs` 内の `MqttHost` / `MqttPort` 定数を編集して再ビルドしてください

## ビルド方法

1. `src/BveEX_20251119.sln` を Visual Studio 2022 で開く
2. NuGet パッケージを復元（`packages.config` に記載の BveEx.CoreExtensions / BveEx.PluginHost / BveEx.Diagnostics / MQTTnet / MQTTnet.Extensions.ManagedClient）
   - `Mackoy.IInputDevice` の参照は BveTs5 のインストール先（既定 `Program Files (x86)\mackoy\BveTs5\Mackoy.IInputDevice.DLL`）を直接参照しています。インストール先が異なる場合は `.csproj` の `HintPath` を修正してください
3. 構成 `Release / AnyCPU` でビルド

ビルドすると `bin\Release\` に `BveEX_20251119.dll` と、依存する以下の dll 一式が出力されます。

- `BveEx.CoreExtensions.dll`
- `BveEx.Diagnostics.dll`
- `BveEx.PluginHost.dll`
- `BveTypes.dll`
- `TypeWrapping.dll`
- `FastCaching.dll`
- `FastMember.dll`
- `MQTTnet.dll`
- `MQTTnet.Extensions.ManagedClient.dll`
- `Mackoy.IInputDevice.DLL`

## インストール方法（車両への組み込み）

- BVE Trainsim 5 の `Scenarios` フォルダ以下に車両フォルダを配置することで、BVE からシナリオ／車両として認識・使用できるようになります（`Scenarios/Yokokura/MQTT_IO/Uchibo20_E217r`、`Scenarios/Yokokura/MQTT_IO/Rock_On_115_taka_T1040` は実際にその配置で運用している実例です）。

1. 組み込みたい車両の `Vehicle.txt` と同じフォルダに、上記ビルド出力の dll 一式（`BveEX_20251119.dll` を含む）をすべてコピーする
   - 複数車両に組み込む場合、`BveEX_20251119.dll` は車両ごとに分かりやすい名前にリネームしてよい（例: `Scenarios/Yokokura/MQTT_IO/Rock_On_115_taka_T1040` では `BveEX_RockOn_115.dll` にリネームして使用）
   - 依存 dll（`BveEx.*`, `BveTypes.dll` など）は同じセットをそのまま一緒にコピーする（リネーム不要）
2. `Vehicle.txt` と同じフォルダに、`Vehicle.txt` と同じベース名の `*.VehiclePluginUsing.xml` を作成する

   ```xml
   <?xml version="1.0" encoding="utf-8" ?>
   <BveExPluginUsing>
    <Assembly Path="BveEX_20251119.dll" />
   </BveExPluginUsing>
   ```

   `Path` は手順1でリネームした場合そのファイル名に合わせる（`Scenarios/Yokokura/MQTT_IO/Uchibo20_E217r`、`Scenarios/Yokokura/MQTT_IO/Rock_On_115_taka_T1040` の実例を参照）
3. MQTT ブローカーを起動しておく
4. BVE でシナリオを開始する。プラグインがロードされるとデバッグ用コンソールウィンドウが表示され、MQTT 接続ログが確認できる

## MQTT トピック仕様

### Publish（BVE → MQTT）

| トピック | 内容 | 形式 |
|---|---|---|
| `bve/time` | シナリオ内経過時間 | ミリ秒（整数文字列） |
| `bve/speed` | 速度 | km/h、小数点2桁 |
| `bve/location` | キロ程 | m、小数点1桁 |
| `bve/pilot` | 全戸閉状態 | `1`=閉 / `0`=開 |
| `bve/am` | 電流計（アンペア） | 小数点1桁 |
| `bve/panel` | パネル配列 `PanelArray[0..8]` | JSON配列 `[v0,v1,...,v8]` |
| `bve/sound` | サウンド配列の一部 `SoundArray[0,1,3,4]` | JSON配列 `[v0,v1,v3,v4]` |

### Subscribe（MQTT → BVE、外部操作用）

| トピック | 内容 | ペイロード |
|---|---|---|
| `bve/reverser` | 逆転器位置を設定 | 整数文字列（`ReverserPosition` にキャスト） |
| `bve/power` | 力行ノッチを設定 | 整数文字列 |
| `bve/brake` | ブレーキノッチを設定 | 整数文字列 |

## ログ出力

プラグイン dll と同じフォルダの `Log\` に、Tick ごとの走行状態を Shift-JIS の CSV で追記します（ファイル名はシナリオ開始時刻 `yyyyMMddHHmmss.csv`）。列は次の順です。

```
Time, Location, Speed, Reverser, Power, Brake, ConstantSpeed, Pilot, Ampere, BrakeCylinderPressure, PanelArray[0..8]
```

`Log\` フォルダは実行のたびに肥大化するため、このリポジトリには含めていません（`.gitignore` で除外）。

##その他
本コードの(生成AIを用いた)改造/Pull Request/Isuueは歓迎いたします。ご自身の車両データや路線データに組み込む際も連絡は不要です(readme等に記載いただければ嬉しいです)。
他の作者さんのデータに組み込んだうえで配布するなどの場合は、関係の作者さんに許可を取るようお願いいたします。

## 既知の制限

- MQTT のホスト/ポートはソースコード内の定数のみで設定可能（`config.json` は現状未使用）
- `bve/reverser` `bve/power` `bve/brake` の受信ハンドラは、値の妥当性チェックを最小限しか行っていない
