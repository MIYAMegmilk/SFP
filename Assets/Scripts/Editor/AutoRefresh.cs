using UnityEditor;

[InitializeOnLoad]
static class AutoRefresh
{
    static double _nextRefreshTime;

    static AutoRefresh()
    {
        EditorApplication.update += Update;
    }

    static void Update()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;

        if (EditorApplication.timeSinceStartup < _nextRefreshTime)
            return;

        _nextRefreshTime = EditorApplication.timeSinceStartup + 3.0;
        AssetDatabase.Refresh(ImportAssetOptions.Default);
    }
}
