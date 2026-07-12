using SFP.Presentation;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Registers the underwater rendering system on the URP asset and generates its Volume profile
// assets (design doc §6.1/§9). Idempotent - safe to run from the menu repeatedly and to call
// from FloodTestShipBuilder on every scene rebuild. Editor-only, no direct YAML/.asset edits:
// everything goes through SerializedObject / AssetDatabase like Unity's own inspector code
// (see ScriptableRendererDataEditor.AddComponent, which this mirrors).
public static class UnderwaterRenderingSetup
{
    const string RendererPath = "Assets/Settings/PC_Renderer.asset";
    const string OceanProfilePath = "Assets/Settings/UnderwaterOceanProfile.asset";
    const string InteriorProfilePath = "Assets/Settings/UnderwaterInteriorProfile.asset";
    const string ShaderName = "SFP/Underwater";

    [MenuItem("SFP/Setup Underwater Rendering")]
    public static void Setup() => EnsureSetup();

    public static void EnsureSetup()
    {
        EnsureRendererFeature();
        EnsureOceanProfile();
        EnsureInteriorProfile();
    }

    // ===== Renderer Feature registration =====

    static void EnsureRendererFeature()
    {
        var rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererPath);
        if (rendererData == null)
        {
            Debug.LogError($"UnderwaterRenderingSetup: no ScriptableRendererData at {RendererPath}");
            return;
        }

        var shader = Shader.Find(ShaderName);

        if (rendererData.TryGetRendererFeature<UnderwaterRendererFeature>(out var existingFeature))
        {
            AssignShader(existingFeature, shader);
            return;
        }

        var feature = ScriptableObject.CreateInstance<UnderwaterRendererFeature>();
        feature.name = nameof(UnderwaterRendererFeature);

        // Sub-asset first, so AssetDatabase can resolve its local file id for the feature map
        // below (mirrors ScriptableRendererDataEditor.AddComponent).
        AssetDatabase.AddObjectToAsset(feature, rendererData);
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);

        var so = new SerializedObject(rendererData);
        var featuresProp = so.FindProperty("m_RendererFeatures");
        var mapProp = so.FindProperty("m_RendererFeatureMap");

        featuresProp.arraySize++;
        featuresProp.GetArrayElementAtIndex(featuresProp.arraySize - 1).objectReferenceValue = feature;

        mapProp.arraySize++;
        mapProp.GetArrayElementAtIndex(mapProp.arraySize - 1).longValue = localId;

        so.ApplyModifiedProperties();

        AssignShader(feature, shader);

        EditorUtility.SetDirty(rendererData);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    static void AssignShader(UnderwaterRendererFeature feature, Shader shader)
    {
        if (shader == null)
        {
            // Shader is added in a separate task; the feature just stays inactive (Create()
            // no-ops on a null shader) until it exists and this runs again.
            Debug.LogWarning($"UnderwaterRenderingSetup: shader '{ShaderName}' not found; " +
                              "UnderwaterRendererFeature will stay inactive until it does.");
            return;
        }

        var featureSo = new SerializedObject(feature);
        var shaderProp = featureSo.FindProperty("_underwaterShader");
        if (shaderProp.objectReferenceValue == shader)
            return;

        shaderProp.objectReferenceValue = shader;
        featureSo.ApplyModifiedProperties();
        EditorUtility.SetDirty(feature);
        AssetDatabase.SaveAssets();
    }

    // ===== Volume profiles =====

    // Exterior/open-ocean defaults. All parameters overridden so this profile alone (weight 1,
    // priority 10) fully defines the baseline optical state (design doc §4.3 table).
    static void EnsureOceanProfile()
    {
        if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(OceanProfilePath) != null)
            return;

        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "UnderwaterOceanProfile";
        AssetDatabase.CreateAsset(profile, OceanProfilePath);

        // overrides:true bakes UnderwaterVolumeComponent's own field defaults (already the
        // physical/gameplay values from design doc §4.2) into this profile as explicit values.
        VolumeProfileFactory.CreateVolumeComponent<UnderwaterVolumeComponent>(profile, overrides: true, saveAsset: true);
    }

    // Flooded-interior overrides: turbid, greenish water with no sun-driven caustics/god rays and
    // no exterior marine snow (design doc §4.3). Only these 5 parameters are overridden; every
    // other value blends in from UnderwaterOceanProfile via the priority-10/20 Volume stack.
    static void EnsureInteriorProfile()
    {
        if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(InteriorProfilePath) != null)
            return;

        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "UnderwaterInteriorProfile";
        AssetDatabase.CreateAsset(profile, InteriorProfilePath);

        var comp = VolumeProfileFactory.CreateVolumeComponent<UnderwaterVolumeComponent>(profile, overrides: false, saveAsset: false);

        comp.turbidity.value = 0.4f;
        comp.turbidity.overrideState = true;

        comp.scatterColor.value = new Color(0.12f, 0.30f, 0.25f, 1f);
        comp.scatterColor.overrideState = true;

        comp.causticsIntensity.value = 0f;
        comp.causticsIntensity.overrideState = true;

        comp.godRayIntensity.value = 0f;
        comp.godRayIntensity.overrideState = true;

        comp.marineSnowDensity.value = 0f;
        comp.marineSnowDensity.overrideState = true;

        EditorUtility.SetDirty(comp);
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}
