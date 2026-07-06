using UnityEngine;
using UnityEditor;

public static class MaterialUpgrader
{
    [MenuItem("SFP/Upgrade All Materials to URP")]
    public static void UpgradeAll()
    {
        var urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null)
        {
            Debug.LogError("URP Lit shader not found");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:Material", new[]
        {
            "Assets/SciFi Warehouse Kit",
            "Assets/FreeLowpolyScifiObjects",
            "Assets/Free LowPoly SciFi Pack",
        });

        int converted = 0;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == urpLit) continue;

            Color color = Color.white;
            Texture mainTex = null;
            Texture normalMap = null;
            float metallic = 0f;
            float smoothness = 0.5f;

            if (mat.HasProperty("_Color"))
                color = mat.color;
            if (mat.HasProperty("_MainTex"))
                mainTex = mat.GetTexture("_MainTex");
            if (mat.HasProperty("_BumpMap"))
                normalMap = mat.GetTexture("_BumpMap");
            if (mat.HasProperty("_Metallic"))
                metallic = mat.GetFloat("_Metallic");
            if (mat.HasProperty("_Glossiness"))
                smoothness = mat.GetFloat("_Glossiness");

            mat.shader = urpLit;
            mat.SetColor("_BaseColor", color);
            if (mainTex != null) mat.SetTexture("_BaseMap", mainTex);
            if (normalMap != null) mat.SetTexture("_BumpMap", normalMap);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);

            EditorUtility.SetDirty(mat);
            converted++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"Upgraded {converted} materials to URP Lit");
    }
}
