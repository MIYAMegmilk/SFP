# SFP (Submarine Flooding Prototype)

3D Barotrauma風の潜水艦浸水・ダメージコントロールシミュレーター。Unity 6.3 LTS (URP)。

## 設計思想

**物理現象は現実の法則に準拠する。** 浸水量、空気圧、浮力、水流、衝撃力などは実在する物理公式をベースに実装すること。
- 流体: トリチェリの定理（開口部流量）、ベルヌーイの原理、shallow water equations
- 気圧: ボイルの法則（密封区画の圧縮空気）、ダルトンの法則（酸素分圧）
- 浮力: アルキメデスの原理（排水量 vs 総質量）、バラスト注排水による深度制御
- 衝撃: 運動量保存則（衝突ダメージ）、生物の体当たりは質量×速度²ベースの運動エネルギー計算
- 海流: カールノイズによる発散フリーの流れ場、深度減衰
- 簡略化は許容するが、単位系(SI)と次元を保ち、マジックナンバーには物理的根拠をコメントする

## アーキテクチャ

### アセンブリ構成（依存方向は一方通行）

```
SFP.Simulation  ← 純粋C# (noEngineReferences: true)
    ↑
SFP.Presentation ← UnityEngine使用可、Simulation参照可、Gameplay参照不可
    ↑
SFP.Gameplay     ← Simulation + Presentation + InputSystem参照可
```

- **SFP.Simulation** (`Assets/Scripts/Simulation/`): 全ゲームロジック。UnityEngine禁止、System.Math使用。決定論的（ハッシュベースの乱数、System.Random禁止）。
- **SFP.Presentation** (`Assets/Scripts/Presentation/`): MonoBehaviourラッパー、ビジュアル、SimulationBridge。Gameplay参照不可。
- **SFP.Gameplay** (`Assets/Scripts/Gameplay/`): プレイヤー操作、UI、ツール。両方参照可。
- **Editor** (`Assets/Scripts/Editor/`): asmdefなし（Assembly-CSharp-Editor）。FloodTestShipBuilder、SFPAutoRunner。
- **Tests** (`Assets/Scripts/Tests/EditMode/`): SFP.Simulation のみ参照。NUnit。noEngineReferences。

### 座標系

- **シミュレーション空間 = 船ローカル空間 = 設計時座標** (x 0..24, y 0..18, z 0..6)
- **ワールド空間**: 海面 Y=0、深度は Y=-Depth。マップ座標 = ワールド座標。
- **ShipRoot**: 船内全体の親。`ShipRootDriver` が毎フレーム `(PositionX, -Depth, PositionZ)` に配置、Heading-90°でヨー回転。
- **変換**: `SimulationBridge.WorldToShip()` / `ShipToWorld()` でランタイム変換。BuildGraph時（Awake）は ShipRoot が identity なので変換不要。

### デバイスパターン

```
*Definition (Presentation MonoBehaviour) — シーン上の設置物、ビルダーで作成
*State (Simulation POCO)                — シミュレーション状態
*Interaction (Gameplay MonoBehaviour)    — プレイヤー操作 (E使用/Esc終了)
*VisualManager (Presentation)           — ビジュアル同期
```

## コード規約

- 名前空間: アセンブリ名と一致 (`SFP.Simulation`, `SFP.Presentation`, `SFP.Gameplay`)
- private フィールド: `_camelCase`、ローカル変数: `camelCase`
- Simulation クラス: `sealed class`、public mutable フィールド、XMLドキュメント不要
- ブレーススタイル: Allman (新しい行)
- コメント: 最小限。物理定数や非自明な判断のみ。WHYを書く
- .meta ファイル: 手動作成禁止（Unityが自動生成）
- `isStatic = true`: ShipRoot配下のオブジェクトには設定禁止（静的バッチングが移動を妨げる）

## ビルド・実行

- **シーンビルド**: メニュー `SFP > Build FloodTestShip Scene`
- **SFPAutoRunner**: スクリプト変更を検知 → 5秒静止後に自動コンパイル → シーン再構築 → Play開始
- **テスト**: EditMode のみ。Play中はテスト不可 → 先に `set_play_mode_status stop` してから `run_tests`
- **Unity MCP**: エディタ非フォーカス時にタイムアウトする → `AppActivate(pid)` でフォーカス維持

## 開発ワークフロー

- **実装設計**: Fable モデル（エフォート高）
- **コーディング**: Sonnet 5 または Opus 4.6（エフォート高）
- **並列エージェント**: ファイル所有権を重複させない。設計書をスクラッチパッドに書き、各エージェントに担当ファイルを明示
- **コミットメッセージ**: マイルストーン形式 (`M3: ...`, `M5: ...`)

## テスト

- 場所: `Assets/Scripts/Tests/EditMode/`
- フレームワーク: NUnit (`[TestFixture]`, `[Test]`)
- 純粋C#（UnityEngine不可）、SFP.Simulation のみ参照
- ヘルパーメソッドでミニマルなグラフ構築 (`DefaultMap()`, `TwoRoomGraph()`)
- 決定論テスト: 同一シード → 同一結果

## 主要ファイル

| レイヤー | ファイル | 役割 |
|---------|---------|------|
| Sim | `SubmarineState.cs` | 浮力・推進・ノイズ・海流ドリフト |
| Sim | `CompartmentGraph.cs` | 区画グラフ、開口部、水位 |
| Sim | `ShallowWaterSystem.cs` | flux-based 浅水方程式 |
| Sim | `FlowSolver.cs` | 開口部間の流量計算 |
| Sim | `AtmosphereSystem.cs` | 酸素・気圧シミュレーション |
| Sim | `DamageSystem.cs` | 船殻ダメージ・破孔生成 |
| Sim | `FireSystem.cs` | 火災・延焼・酸素消費 |
| Sim | `PowerGrid.cs` | 原子炉・バッテリー・配電 |
| Sim | `MapGenerator.cs` | 決定論的地形生成（fBm + リッジ） |
| Sim | `OceanCurrentField.cs` | カールノイズ海流場 |
| Sim | `CreatureSystem.cs` | 生物AI（巡回/接近/攻撃/逃走） |
| Pres | `SimulationBridge.cs` | シングルトン、全システム統合・tick |
| Pres | `ShipRootDriver.cs` | 船体ワールド配置（LateUpdate） |
| Pres | `TerrainRenderer.cs` | チャンク化地形メッシュ |
| Editor | `FloodTestShipBuilder.cs` | シーン構築（12区画、全デバイス） |
| Editor | `SFPAutoRunner.cs` | 自動コンパイル・リビルド・再生 |
| Game | `PlayerController.cs` | 歩行/水泳、酸素、火災ダメージ |
