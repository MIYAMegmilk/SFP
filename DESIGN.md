# DESIGN.md — M13: Device Command Relay + Full State Sync

## Goal

全プレイヤーインタラクション（操舵・原子炉・バラスト・ポンプ・HVAC・ソナー・砲塔・エアロック・消火・クルー指示）をサーバー権威型RPCで中継し、拡張スナップショットで全クライアントに同期する。M13完了後、マルチプレイで潜水艦の全操作が実際に機能する。

## Success Criteria

- [ ] クライアントが操舵コンソールを操作 → サーバーのSimulationに反映 → 全クライアントに同期
- [ ] クライアントが原子炉・バラスト・ポンプ・HVAC・ソナー・砲塔・エアロック・消火を操作 → 同上
- [ ] スナップショットに航法・バラスト・電力詳細・船殻HP・生物位置・デバイス有効状態が含まれる
- [ ] クライアント側のStatusMonitor/ADCPがスナップショットデータを正しく表示
- [ ] 既存シングルプレイが壊れない（DeviceRpcRelay未接続時はローカルフォールバック）
- [ ] 帯域: スナップショット < 1KB/tick (10Hz LAN向け)

## Architecture

### DeviceCommand — 汎用コマンドRPC

M12のドア専用RPCを汎用化。全デバイス操作を1つのServerRpcで処理する。

```csharp
// SFP.Simulation に配置（純粋C#、enum + struct）
public enum DeviceCommandKind : byte
{
    // 操舵 (SteeringInteraction)
    SetThrottle,         // FloatVal = throttle [-1..1]
    SetRudder,           // FloatVal = angle
    SetDesiredDepth,     // FloatVal = depth
    SetDesiredHeading,   // FloatVal = heading
    SetDesiredSpeed,     // FloatVal = speed
    ToggleAutoPilot,     // no value
    ToggleDepthHold,     // no value

    // 原子炉 (ReactorInteraction)
    SetReactorFission,   // IntVal = reactor index, FloatVal = rate
    SetReactorTurbine,   // IntVal = reactor index, FloatVal = output

    // バラスト (BallastInteraction)
    SetBallastTarget,    // IntVal = tank index, FloatVal = fill level

    // ポンプ (PumpInteraction)
    TogglePump,          // IntVal = pump index

    // エアロック (AirlockInteraction)
    AirlockFlood,        // IntVal = airlock index
    AirlockDrain,        // IntVal = airlock index

    // HVAC
    ToggleO2Generator,   // IntVal = device index
    ToggleCO2Scrubber,   // IntVal = device index
    ToggleVent,          // IntVal = device index

    // 消火 (ExtinguisherInteraction)
    Extinguish,          // IntVal = compartment id, FloatVal = amount

    // 自動消火 (SuppressionInteraction)
    ToggleSuppression,   // IntVal = device index

    // ソナー (SonarInteraction)
    ToggleSonarActive,   // no value
    ToggleSonarPassive,  // no value

    // 砲塔 (TurretInteraction)
    SetTurretRotation,   // IntVal = turret index, FloatVal = angle
    SetTurretElevation,  // IntVal = turret index, FloatVal = angle
    FireTurret,          // IntVal = turret index

    // クルー (CrewCommandInteraction)
    IssueCrewOrder,      // IntVal = memberId, IntVal2 = orderId, FloatVal = targetId
    CancelCrewOrder,     // IntVal = memberId

    // ドア/ハッチ (DoorInteraction — M12の専用RPCを置換)
    ToggleDoor,          // IntVal = opening index

    // ファブリケーター (FabricatorInteraction)
    StartCraft,          // IntVal = fabricator index, IntVal2 = recipe index

    // 潜水服 (DivingSuitInteraction)
    TakeSuit,            // IntVal = locker index
    ReturnSuit,          // IntVal = locker index
}

public struct DeviceCommand : INetworkSerializable
{
    public DeviceCommandKind Kind;
    public int IntVal;
    public int IntVal2;
    public float FloatVal;
    // 合計: 1 + 4 + 4 + 4 = 13 bytes/command
}
```

### DeviceRpcRelay 拡張

```csharp
// SFP.Presentation
public class DeviceRpcRelay : NetworkBehaviour
{
    // クライアント → サーバー: 全デバイス操作の統一エントリポイント
    public void RequestCommand(DeviceCommand cmd)
    {
        if (NetworkBootstrap.Instance?.IsServer == true)
            ExecuteCommand(cmd); // ホスト: 直接実行
        else
            DeviceCommandServerRpc(cmd);
    }

    [ServerRpc(RequireOwnership = false)]
    void DeviceCommandServerRpc(DeviceCommand cmd, ServerRpcParams p = default)
    {
        ExecuteCommand(cmd);
    }

    void ExecuteCommand(DeviceCommand cmd)
    {
        var bridge = SimulationBridge.Instance;
        if (bridge == null) return;

        switch (cmd.Kind)
        {
            case SetThrottle: bridge.Engine.ThrottleSetting = Mathf.Clamp(cmd.FloatVal, -1f, 1f); break;
            case SetRudder:   bridge.SubState.RudderAngle = cmd.FloatVal; break;
            // ... 各コマンドのサーバー側処理
        }
    }

    // M12のToggleDoorServerRpcは削除 → ToggleDoor commandに統合
}
```

### Interaction リファクタパターン

全 *Interaction クラスに同じパターンを適用:

```csharp
// Before (直接変更 — クライアントでは無効):
bridge.Engine.ThrottleSetting = newThrottle;

// After (RPC経由 — ネットワーク透過):
var relay = DeviceRpcRelay.Instance;
if (relay != null)
    relay.RequestCommand(new DeviceCommand { Kind = DeviceCommandKind.SetThrottle, FloatVal = newThrottle });
else
    bridge.Engine.ThrottleSetting = newThrottle; // フォールバック (シングルプレイ)
```

### SimSnapshot 拡張

```
現在のフィールド (M12):                  追加フィールド (M13):
─────────────────────────                ─────────────────────────
Depth, Heading, Speed,                   NavDesiredDepth/Heading/Speed (3 float)
PositionX, PositionZ                     NavFlags byte (AutoPilot, DepthHold)
Throttle, Rudder
PowerVoltage                             ReactorFission[] (per reactor)
                                         ReactorTurbine[] (per reactor)
WaterVolumes[14]                         ReactorTemp[] (per reactor)
OxygenLevels[14]                         BatteryCharge[] (per battery)
Pressures[14]
FireIntensities[14]                      BallastFillLevels[] (per tank)

OpeningBitfield (32 bits)                DeviceEnabledBits (uint, 32 bits)
                                           bit 0-13: BilgePumps
                                           bit 14-16: Vents
                                           bit 17-18: O2Generators
                                           bit 19-20: CO2Scrubbers
                                           bit 21-22: SuppressionSystems

Timestamp                                AirlockPhase (byte)
                                         SonarFlags (byte: Active|Passive)

                                         TurretRotation, TurretElevation (2 float)
                                         TurretAmmo (int)

                                         HullIntegrities[] (per section, 84)

                                         CreatureX/Z/Depth/Health[] (6 each)
                                         CreatureAliveBits (byte)
```

**帯域見積もり**: 現行~280B + 追加~530B = ~810B/tick × 10Hz = ~8 KB/s（LAN十分）

### ローカルフォールバック方針

`DeviceRpcRelay.Instance == null` のとき（シングルプレイ、NetworkManager未起動）、各Interactionは従来どおり直接変更。これによりシングルプレイ互換を保証。

### 対象外（M14以降に延期）

| 項目 | 理由 |
|------|------|
| BreachTool / RepairTool | ランタイムのOpening生成・削除 → ネットワークオブジェクト生成が必要 |
| PumpPlacer / BuildTool | ランタイム構造物生成 → NetworkObject.Spawn |
| EVAWeaponController | クリーチャーダメージ → クリーチャー同期の上に構築 |
| デルタ圧縮 | 帯域最適化。LAN環境ではフルステート送信で十分 |
| SWEグリッド同期 | データ量大。クライアントは独立SWEで見た目のみ |

## Task Breakdown

| # | Task | Agent | Deps | Status |
|---|------|-------|------|--------|
| 1 | `DeviceCommandKind` enum + `DeviceCommand` struct を `SFP.Simulation` に作成 | fast-worker | None | ✅ |
| 2 | `DeviceRpcRelay` 拡張: 汎用 `RequestCommand` / `DeviceCommandServerRpc` + `ExecuteCommand` switch | deep-reasoner | 1 | ✅ |
| 3 | `SimSnapshot` 拡張: 新フィールド追加 + `BuildSnapshot` + `Interpolate` + `ApplySnapshot` 更新 | deep-reasoner | None | ✅ |
| 4 | コアInteractionリファクタ: `SteeringInteraction`, `ReactorInteraction`, `BallastInteraction` → コマンドRPC | fast-worker | 2 | ✅ |
| 5 | ダメコンInteractionリファクタ: `PumpInteraction`, `AirlockInteraction`, `ExtinguisherInteraction`, `SuppressionInteraction` → コマンドRPC | fast-worker | 2 | ✅ |
| 6 | HVACリファクタ: `OxygenGeneratorInteraction`, `CO2ScrubberInteraction`, `VentInteraction` → コマンドRPC | fast-worker | 2 | ✅ |
| 7 | センサー・武装リファクタ: `SonarInteraction`, `TurretInteraction`, `CrewCommandInteraction`, `FabricatorInteraction` → コマンドRPC | fast-worker | 2 | ✅ |
| 8 | `DoorInteraction` を汎用コマンドに移行 + M12の `ToggleDoorServerRpc` 削除 | fast-worker | 2 | ✅ |
| 9 | コンパイル確認 + 統合テスト（ローカル） | manual | all | ✅ |

## Risks & Open Questions

1. **INetworkSerializableの配列長変動**: コンパートメント数やクリーチャー数が接続中に変わることは想定しない（シーン構築時に固定）
2. **コマンドのバリデーション**: サーバー側ExecuteCommandで境界チェック（配列範囲、値クランプ）を必ず行う
3. **DivingSuitInteraction**: プレイヤーローカル状態。NetworkTransformで位置は同期されるが、装備ビジュアルの同期は別途必要（M14）
4. **StatusMonitor/ADCP**: 読み取り専用 — スナップショットデータを表示するだけで変更不要
5. **uint OpeningBitfield 32bit制限**: 現在20個以下なので問題なし。増加時はulongに拡張

## Review Log

<!-- Phase 4で追記 -->
