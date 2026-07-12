# DESIGN.md — Underwater Rendering System (Phase 1)

## Goal

Beer-Lambert則に基づく波長別RGB吸収フォグをURP Renderer Featureとして実装し、既存のRenderSettings.fogベースの水中表現を置き換える。

## Success Criteria

- [ ] 艦内ドライ区画でフォグが掛からない（現行バグ修正）
- [ ] EVAで距離に応じ赤→緑→青の順に色が失われる
- [ ] 深度500mで環境がVisibilityFloor色まで暗くなる
- [ ] 水面より上ではGPUコスト増ゼロ（パスが積まれない）
- [ ] Unityコンパイルエラーなし

## Architecture

詳細設計書: `scratchpad/underwater_rendering_design.md`

```
UnderwaterEnvironmentController (Presentation MonoBehaviour)
  +-- 水中判定 (CPU, 毎フレーム1回)
  +-- グローバルシェーダ定数設定
  +-- Volume weight制御

UnderwaterVolumeComponent (VolumeComponent)
  +-- 全パラメータ定義 (Beer-Lambert係数, カスティクス, 光芒等)

UnderwaterRendererFeature -> UnderwaterPass (RenderGraph)
  +-- Underwater.shader (full-screen composite)
       +-- SFPUnderwaterCommon.hlsl (CBUFFER, noise, depth reconstruction)

SimulationBridge.FindCompartmentAt() <-- 新設API
FloodTestShipBuilder <-- 配置差替
UnderwaterAmbience.cs <-- 削除
```

## Task Breakdown

| # | Task | Files | Agent | Deps | Status |
|---|------|-------|-------|------|--------|
| 1 | HLSL共通インクルード + シェーダ本体 | `Shaders/Includes/SFPUnderwaterCommon.hlsl`, `Shaders/Underwater.shader` | deep-reasoner | None | ✅ |
| 2 | VolumeComponent定義 | `Presentation/Rendering/UnderwaterVolumeComponent.cs` | fast-worker | None | ✅ |
| 3 | SimulationBridge.FindCompartmentAt追加 | `Presentation/SimulationBridge.cs` | fast-worker | None | ✅ |
| 4 | RendererFeature + UnderwaterPass | `Presentation/Rendering/UnderwaterRendererFeature.cs` | deep-reasoner | 1,2 | ✅ |
| 5 | EnvironmentController | `Presentation/Rendering/UnderwaterEnvironmentController.cs` | deep-reasoner | 2,3 | ✅ |
| 6 | Editorセットアップ + ビルダー差替 | `Editor/UnderwaterRenderingSetup.cs`, `Editor/FloodTestShipBuilder.cs` | deep-reasoner | 4,5 | ✅ |
| 7 | UnderwaterAmbience.cs削除 | `Presentation/UnderwaterAmbience.cs` | (ローカル対応) | 5 | ⬜ |

## Risks & Open Questions

- PC_Renderer.asset への Feature 登録は Editor スクリプト経由（クラウドでは実行不可 -> ローカル対応）
- Volume プロファイルアセット生成も同様
- UnderwaterAmbience.cs の削除は .meta 整合のためローカルで実施

## Review Log
<!-- Phase 4で追記 -->
