# 3D区画浸水プロトタイプ 設計書

**プロジェクト名(仮):** Submarine Flooding Prototype (SFP)
**エンジン:** Unity 6 (URP)
**言語:** C#
**目的:** Barotrauma型の区画浸水モデルを3Dで実装可能か、および負荷が許容範囲(浸水シミュレーション部でCPU 1ms/フレーム未満)かを技術検証する。ゲーム本体の開発ではない。

---

## 0. 検証で答えるべき問い(Success Criteria)

| # | 問い | 合格基準 |
|---|------|---------|
| Q1 | 区画グラフによる浸水更新の負荷 | 100区画・200開口部で浸水更新 < 1.0ms/フレーム (CPU, メインスレッド) |
| Q2 | 水面メッシュ表現の負荷 | 全区画水面描画込みで 60FPS維持 (RTX 3060クラス想定) |
| Q3 | 密閉ボリューム検出(flood-fill)の負荷 | 構造変更1回あたり再計算 < 16ms (1フレーム内、非同期化可否も判定) |
| Q4 | 見た目の説得力 | 水位上昇・破孔からの流入・ドア越しの流れが視覚的に成立するか(主観評価、動画記録) |

Q1〜Q3のいずれかが不合格なら、アーキテクチャ再検討(非同期化、区画数上限、LOD)を行い本設計書を改訂する。

---

## 1. 背景・根拠(なぜこの方式か)

- Barotraumaの浸水はハル(部屋)単位の水位スカラー値で管理されている(公式Status Monitorが区画ごとの水位単一値を表示することから裏付け)。粒子流体ではない。
- 3D先行事例としてStormworks: Build and Rescueが「密閉ボリューム判定+区画単位の浸水+開口部流量」で商業的に成立している。SPH等の粒子流体は使っていない。
- SPHをUnityで実装した2025年の研究では、最適化済みでも流体シミュレーション単体で5.7〜6.0ms/フレームを消費する。60FPSのフレーム予算16.6msの約1/3であり、AI・描画・ネット同期を載せるゲームには不適。さらにSPHは浸水量に比例して負荷が増える特性を持つ。
- したがって本プロトタイプは **粒子流体を採用しない**。区画スカラー+見た目のメッシュ/ローカルパーティクルで構成する。

---

## 2. アーキテクチャ概要

```
┌─────────────────────────────────────────────┐
│ Simulation Layer (純C#、Unity非依存にする)     │
│  CompartmentGraph                            │
│   ├── Compartment (水量, 気圧, 酸素量)        │
│   ├── Opening (ドア/ハッチ/破孔, 開口面積)     │
│   └── FlowSolver (毎Tick流量計算)             │
│  SealedVolumeDetector (flood-fill, 構造変更時) │
├─────────────────────────────────────────────┤
│ Presentation Layer (Unity/URP)               │
│  WaterSurfaceRenderer (区画ごとの水面メッシュ)  │
│  BreachVFX (破孔パーティクル, ParticleSystem)  │
│  DebugOverlay (区画水位/流量の可視化)          │
├─────────────────────────────────────────────┤
│ Gameplay Layer (最小限)                       │
│  BreachTool (クリックで壁に破孔を開ける)        │
│  DoorInteraction (ドア開閉)                   │
│  Pump (指定区画の水量を毎秒減らす)             │
│  DeviceDegradation (水没設備の劣化加速)        │
└─────────────────────────────────────────────┘
```

**設計原則:**
- Simulation LayerはMonoBehaviour禁止・UnityEngine非依存の純C#とする。理由: (a) 単体テスト可能、(b) 将来のマルチプレイヤー化でサーバー側実行が可能、(c) プロファイリングでUnityオーバーヘッドと分離できる。
- Simulationは固定タイムステップ(既定 30Hz)で更新。描画補間はPresentation側で行う。

---

## 3. データモデル

### 3.1 Compartment(区画)

```csharp
public sealed class Compartment
{
    public int Id;
    public Bounds LocalBounds;      // 区画の直方体境界(複数直方体の合成は将来課題)
    public float Volume;            // m^3 (LocalBoundsから算出)
    public float WaterVolume;       // m^3, 0 <= WaterVolume <= Volume
    public float OxygenRatio;       // 0..1 (本プロトタイプでは表示のみ、消費ロジックは対象外)
    public float FloorY;            // ワールド座標での床高さ
    public float WaterLevelY =>     // 現在の水面のワールドY (直方体前提)
        FloorY + (WaterVolume / Volume) * (LocalBounds.size.y);
    public List<int> OpeningIds;
}
```

注意: 非直方体区画(L字部屋等)は本プロトタイプでは対象外。Barotraumaも非矩形部屋は複数ハルの合成で表現しており、同じ方針を将来採用する。

### 3.2 Opening(開口部)

```csharp
public enum OpeningKind { Door, Hatch, Breach }

public sealed class Opening
{
    public int Id;
    public OpeningKind Kind;
    public int CompartmentA;        // -1 = 外海(Sea)
    public int CompartmentB;
    public float Area;              // m^2 開口面積(破孔は成長可能)
    public float CenterY;           // 開口部中心のワールドY
    public float Height;            // 開口部の縦寸法(部分水没時の有効面積計算用)
    public bool IsOpen;             // ドア/ハッチの開閉状態(Breachは常にtrue)
}
```

### 3.3 外海(Sea)

- `CompartmentA == -1` を外海とみなす特別ノード。水量無限、水面Yは`SeaLevel`定数(潜水艦なので通常は艦全体が水没扱い=外圧側の水位は常に開口部より上)。
- 外圧(深度による圧力差)は流速係数として扱う(§4.2)。

---

## 4. 浸水シミュレーション仕様

### 4.1 更新ループ(固定30Hz)

```
foreach tick:
  1. 各Openingの有効開口面積を計算(両側の水位と開口部の位置関係から)
  2. 各Openingの流量 Q を計算(§4.2)
  3. Q * dt を CompartmentA/B の WaterVolume に加減算(クランプ処理)
  4. Pump処理(対象区画の WaterVolume -= pumpRate * dt)
  5. DeviceDegradation更新(設備のY座標 < 区画WaterLevelY なら劣化レート倍率適用)
```

全区画・全開口部を毎Tick線形走査する。計算量 O(C + E)。100区画・200開口部で数万命令オーダーであり、Q1の1ms基準に対して理論上2〜3桁の余裕がある(実測で確認する)。

### 4.2 流量モデル

トリチェリの定理ベースの簡易オリフィス流:

```
Q = Cd * A_eff * sqrt(2 * g * Δh)
```

- `Cd`: 流量係数(既定 0.6、チューニングパラメータ)
- `A_eff`: 有効開口面積。開口部が両側の水面より完全に下なら`Area`、部分的なら線形補間
- `Δh`: 両側の水頭差。外海が絡む場合は `Δh = 深度差 + 外圧ボーナス係数`(ゲームバランス用スカラー、物理的厳密性は要求しない)
- 数値安定性: 1Tickで移動できる水量を `min(Q*dt, 送出側WaterVolume, 受入側残容量)` にクランプ。振動(2区画間で水が往復する)が発生した場合はダンピング係数を導入する(実測で判断)

**根拠:** Stormworksも開口部ごとの流量ベースで浸水速度を制御しており(密閉空間へのドア経由浸水は非密閉空間の瞬時浸水より遅い)、この抽象度でゲームとして成立することが実証されている。

### 4.3 密閉ボリューム検出

- ボクセルグリッド(セルサイズ既定 0.5m)で艦内をスキャンし、外気に連結していないセル集合をflood-fillで検出→Compartment自動生成。
- **実行タイミング:** 構造変更時のみ(壁の設置/破壊)。ランタイム毎Tickでは実行しない。
- 破孔(Breach)は構造変更ではなくOpening追加として扱い、flood-fill再実行を回避する(Q3の負荷対策)。
- Q3計測: 20m×8m×10mの艦体(ボクセル数 約51,200セル @0.5m)でflood-fill 1回の実行時間を計測。16ms超過なら`Task.Run`による非同期化+完了までの旧グラフ継続使用を実装。

---

## 5. 表現(Presentation)仕様

### 5.1 水面メッシュ

- 区画ごとに1枚のクアッド(水平面)を生成し、`WaterLevelY`(描画補間済み)に追従させる。
- シェーダ: URP Shader Graphで法線スクロール+フレネル+簡易透過。**Asset Storeの既製水シェーダは使用しない**(依存を減らし、Claude Codeで完結させるため)。
- 水没区画のカメラ内演出(色被り・歪み)はスコープ外(検証項目でないため)。

### 5.2 破孔VFX

- Unity標準ParticleSystemで噴出表現。パーティクル数は破孔1つあたり最大200に制限。
- 流量Qに応じてemission rateを変化させる。

### 5.3 デバッグ表示(必須)

- 画面オーバーレイ: 区画ごとの水量%・流量ベクトル・Tick処理時間(ms)・flood-fill所要時間。
- Q1/Q2/Q3の計測値を`Unity Profiler`マーカー(`ProfilerMarker` API)で常時記録。

---

## 6. ゲームプレイ最小実装

検証に必要な操作のみ。プレイヤーキャラは1人称の簡易フライカメラで可(キャラクターコントローラ・溺死・酸素はスコープ外)。

| 機能 | 仕様 |
|---|---|
| 破孔生成 | 壁面クリックで Breach Opening追加(Area=0.05m², 時間経過で最大0.3m²まで成長) |
| ドア開閉 | インタラクトキーで IsOpen トグル |
| ポンプ | 区画に配置、稼働中 WaterVolume を 0.1m³/s 減少。水没しても稼働(Barotrauma仕様準拠: 設備は水没で停止せず劣化加速) |
| 設備劣化 | 設備は Condition 0-100 を持つ。通常劣化 0.01/s、水没時 0.5/s(Barotraumaの原子炉が水没時1dmg/sである仕様を参考にスケール調整)。Condition 0 で機能停止 |

---

## 7. テストシーン構成

**Scene: `FloodTestShip`**

- 艦体: 20m(L) × 8m(H) × 10m(W)、3デッキ構成、区画数12(最小検証)→負荷測定時はスクリプトで100区画艦を自動生成
- 区画例: 機関室(下層)、通路×4、居住区×3、艦橋(上層)、バラスト×2、エアロック×1
- 各区画にポンプ1基、機関室にダミー設備(原子炉相当)3基

**負荷測定シナリオ(自動実行スクリプト化する):**

1. `S1_SingleBreach`: 最下層1区画に破孔→満水までのフレームタイム推移をCSV出力
2. `S2_CascadeFlood`: 全ドア開放+破孔3箇所→全艦浸水までの推移
3. `S3_100Compartments`: 自動生成100区画艦で S2 相当を実行(Q1判定用)
4. `S4_StructureEdit`: 壁の設置/破壊を10回連続実行し flood-fill 時間を計測(Q3判定用)

---

## 8. プロジェクト構成

```
Assets/
  Scripts/
    Simulation/          # 純C#(asmdef: SFP.Simulation, UnityEngine参照なし ※Boundsのみ自前structで代替)
      Compartment.cs
      Opening.cs
      CompartmentGraph.cs
      FlowSolver.cs
      SealedVolumeDetector.cs
    Presentation/        # asmdef: SFP.Presentation
      WaterSurfaceRenderer.cs
      BreachVFX.cs
      DebugOverlay.cs
      SimulationBridge.cs   # 固定Tick駆動と補間
    Gameplay/
      BreachTool.cs
      DoorInteraction.cs
      Pump.cs
      DeviceDegradation.cs
    Tests/
      EditMode/            # FlowSolver単体テスト(NUnit)
      Benchmarks/          # S1-S4自動計測
  Shaders/
    WaterSurface.shadergraph
  Scenes/
    FloodTestShip.unity
```

- **単体テスト必須項目:** 質量保存(全区画+排出量の総和が一定)、クランプ動作、水位計算の境界値(空/満水)。

---

## 9. マイルストーン

| M | 内容 | 完了条件 |
|---|---|---|
| M1 | Simulation Layer単体 + EditModeテスト | 質量保存テスト green、2区画間の水移動がテストで検証済み |
| M2 | 12区画テスト艦 + 水面メッシュ + 破孔 | S1/S2が目視で成立、動画記録 |
| M3 | flood-fill による区画自動検出 | S4計測完了、Q3判定 |
| M4 | 100区画負荷測定 | S3計測完了、Q1/Q2判定、計測レポート(CSV+所見)出力 |

M4完了時点で本設計書§0の表を実測値で埋め、ゲーム化続行/方式変更を判断する。

---

## 10. スコープ外(明示)

- SPH/粒子ベース流体(§1の根拠により不採用)
- マルチプレイヤー同期(区画水位はスカラーなので帯域上は有望だが、本検証の対象外)
- キャラクター(溺死・酸素消費・遊泳)
- 火災・電力網・配線
- 非直方体区画、艦の傾斜による水面の傾き(Barotrauma同様、艦は常に水平と仮定)
- セーブ/ロード

## 11. 既知のリスクと対応方針

| リスク | 根拠 | 対応 |
|---|---|---|
| 区画間流量の振動・不安定化 | 数値積分の一般的問題 | クランプ+ダンピング係数、Tick 30→60Hz引き上げをパラメータ化しておく |
| 大型艦での流量詰まり | Stormworksでアップデート後に開口部流量が低下し隣室へ水が流れないバグが報告されている前例 | S2/S3で「開口部があるのに水が流れない」状態を自動検知するアサーションを入れる |
| flood-fillの負荷スパイク | 未実測(データなし) | Q3で実測、超過時は非同期化 |
| 傾斜対応が将来必要になった場合の手戻り | Barotraumaは水平前提で成立しているが3Dでは傾斜の期待値が上がる可能性 | WaterLevelY計算をinterface化し、傾斜対応実装に差し替え可能な構造にしておく |
