# DESIGN.md — M12: Multiplayer Foundation

## Goal

NGO (Netcode for GameObjects) によるサーバー権威型マルチプレイヤー基盤を構築し、2〜4人の協力プレイで潜水艦を操作できるようにする。

## Success Criteria

- [ ] Host/Join によるLAN接続が成立する（IP直接入力）
- [ ] 複数プレイヤーが同一船内で歩行・水泳し、互いの姿が見える
- [ ] シミュレーションはホスト上のみで実行され、クライアントは状態スナップショットを受信・補間する
- [ ] ドア開閉がRPC経由で全クライアントに同期される（デバイス同期のPoCとして）
- [ ] クライアント切断時にホストがクラッシュしない
- [ ] 既存のシングルプレイが壊れない（ホスト＝ソロプレイヤー）

## Architecture

### ネットワークモデル: Server-Authoritative (Host式)

```
┌─────────────────────────────────────────────────┐
│ HOST (= Server + Client 0)                      │
│                                                  │
│  SimulationBridge ─── tick 30Hz ───▶ SimSnapshot │
│       │                                  │       │
│       ▼                                  ▼       │
│  Presentation Layer              NetworkManager  │
│  (ローカル描画)                    │           │ │
│                            ┌──────┘           │  │
│                            ▼                  ▼  │
│                     ClientRpc(snapshot)  ServerRpc│
│                            │            (input)  │
└────────────────────────────│──────────────│───────┘
                             ▼              │
                    ┌────────────────────┐   │
                    │ CLIENT N           │   │
                    │                    │◀──┘
                    │  SimSnapshotBuffer │
                    │  (補間・適用)       │
                    │  Presentation Layer│
                    │  (ローカル描画)     │
                    └────────────────────┘
```

### データフロー

1. **クライアント → サーバー**: `PlayerInputCommand` (移動ベクトル、インタラクション) を `ServerRpc` で送信
2. **サーバー**: `SimulationBridge.Tick()` で全システム更新（既存ロジック変更なし）
3. **サーバー → クライアント**: `SimSnapshot` (潜水艦 + 区画水位 + デバイス状態) を `ClientRpc` でブロードキャスト
4. **クライアント**: 受信スナップショットを補間してPresentationに適用

### 新規クラス

```
SFP.Presentation
├── NetworkBootstrap.cs        — NetworkManager設定、PlayerPrefab登録、接続管理
├── SimSnapshotSync.cs         — NetworkBehaviour: スナップショット送受信・補間バッファ
└── DeviceRpcRelay.cs          — NetworkBehaviour: デバイス操作のRPC中継 (PoC: ドア)

SFP.Gameplay
├── LobbyUI.cs                 — Host/Join画面 (uGUI, IP入力)
├── PlayerInputCommand.cs      — INetworkSerializable 入力コマンド構造体
└── PlayerNetworkController.cs — NetworkBehaviour: 位置同期、入力コマンド送信
```

### SimSnapshot構造

```csharp
struct SimSnapshot : INetworkSerializable
{
    // 潜水艦 (5 floats)
    float Depth, Heading, Speed, PositionX, PositionZ;

    // 区画 (12室)
    float[] WaterVolumes;
    float[] OxygenLevels;
    float[] Pressures;

    // 開口部 (~20個, bit-packed)
    uint OpeningBitfield; // bit0=IsOpen, per opening

    // 電力
    float PowerVoltage;
}
```

### プレイヤー同期

| 要素 | 方式 |
|------|------|
| 自キャラ移動 | Owner権威: ローカル `CharacterController.Move()` → `NetworkTransform` で同期 |
| 他キャラ位置 | NetworkTransform 補間 |
| カメラ | 完全ローカル (同期不要) |
| デバイス操作 | ServerRpc → サーバー検証 → SimSnapshot反映 |

### SimulationBridge変更方針

```csharp
// 追加フィールド
public bool IsServer { get; set; } = true; // デフォルトtrue = シングルプレイ互換

void Update()
{
    if (!IsServer) return;  // クライアント: tickスキップ
    // ... 既存のtickロジック (変更なし)
}

// 新規: クライアント用スナップショット適用
public void ApplySnapshot(SimSnapshot snapshot)
{
    _subState.Depth = snapshot.Depth;
    _subState.Heading = snapshot.Heading;
    // ... 区画水位、開口部状態
}
```

## Task Breakdown

| # | Task | Agent | Deps | Status |
|---|------|-------|------|--------|
| 1 | NGOパッケージ追加 (`netcode.gameobjects` + `transport`) + asmdef参照更新 | fast-worker | None | ✅ |
| 2 | `PlayerInputCommand.cs` — INetworkSerializable入力構造体 | fast-worker | 1 | ✅ |
| 3 | `NetworkBootstrap.cs` — NetworkManager、Transport、PlayerPrefab登録 | deep-reasoner | 1 | ✅ |
| 4 | `PlayerNetworkController.cs` — NetworkBehaviour、NetworkTransform、入力送信 | deep-reasoner | 2,3 | ✅ |
| 5 | `SimSnapshotSync.cs` — スナップショット生成・送信・受信・補間 | deep-reasoner | 3 | ✅ |
| 6 | `DeviceRpcRelay.cs` — ドア開閉RPCの実装 (PoC) | fast-worker | 3 | ✅ |
| 7 | `LobbyUI.cs` — Host/Join UI (IP入力、状態表示) | fast-worker | 3 | ✅ |
| 8 | `SimulationBridge` 修正 — IsServerガード + ApplySnapshot | deep-reasoner | 5 | ✅ |
| 9 | `PlayerController` 修正 — ローカル入力→コマンド変換、ネットワーク連携 | deep-reasoner | 4 | ✅ |
| 10 | `DoorInteraction` 修正 — 直接変更→DeviceRpcRelay経由に切り替え | fast-worker | 6 | ✅ |
| 11 | FloodTestShipBuilder修正 — NetworkManagerプレハブ配置 | fast-worker | 3 | ✅ |
| 12 | 統合テスト — ParrelSync等で2窓確認 | manual (ローカル) | all | ✅ |

## Risks & Open Questions

1. **NGO vs Mirror**: NGOはUnity公式だがカスタムシミュレーション同期に制約がある可能性。M12で問題が出ればM13でMirror移行を検討
2. **スナップショット帯域**: 30Hz全状態送信は過剰。M12では10Hz + 線形補間で開始。デルタ圧縮はM13
3. **ShallowWater同期**: SWEグリッドはデータ量大。M12では区画水位のみ同期、SWEはクライアントで独立シミュレーション（見た目のみ）
4. **BuildGraph on Client**: クライアントでもシーン構造は必要（描画用）。BuildGraphは両サイドで実行するが、クライアントではSimulation初期化をスキップ
5. **ParrelSync**: Editor2窓テストに必要。パッケージ追加が必要
6. **asmdef更新**: `SFP.Presentation.asmdef` と `SFP.Gameplay.asmdef` にNGO参照を追加する必要あり

## Review Log

<!-- Phase 4で追記 -->
