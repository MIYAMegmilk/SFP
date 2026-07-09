using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;
using SFP.Presentation;

namespace SFP.Gameplay
{
    public class StatusServer : MonoBehaviour
    {
        const int Port = 8777;
        const float SnapshotInterval = 0.5f;

        HttpListener _listener;
        byte[] _snapshotBytes;
        byte[] _dashboardBytes;
        float _timer;
        readonly ConcurrentQueue<string> _cmdQueue = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindFirstObjectByType<StatusServer>() != null) return;
            if (SimulationBridge.Instance == null && FindFirstObjectByType<SimulationBridge>() == null) return;
            var go = new GameObject("StatusServer");
            go.AddComponent<StatusServer>();
        }

        void Start()
        {
            LoadDashboard();

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{Port}/");
                _listener.Start();
                _listener.BeginGetContext(OnRequest, null);
                Debug.Log($"[StatusServer] Listening on http://localhost:{Port}/");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[StatusServer] Failed to start: {ex.Message}");
            }
        }

        void LoadDashboard()
        {
            var path = Path.Combine(Application.streamingAssetsPath, "dashboard.html");
            if (File.Exists(path))
                _dashboardBytes = Encoding.UTF8.GetBytes(File.ReadAllText(path));
            else
                _dashboardBytes = Encoding.UTF8.GetBytes("<html><body>dashboard.html not found</body></html>");
        }

        void Update()
        {
            // Drain command queue on main thread
            var rc = FindFirstObjectByType<DebugRemoteControl>();
            while (_cmdQueue.TryDequeue(out var cmd))
            {
                if (rc != null) rc.Execute(cmd);
            }

            _timer += Time.unscaledDeltaTime;
            if (_timer < SnapshotInterval) return;
            _timer = 0f;

            var bridge = SimulationBridge.Instance;
            if (bridge == null) return;

            var mm = FindFirstObjectByType<MissionManager>();
            var snap = StatusSnapshot.Build(bridge, mm);
            var json = JsonUtility.ToJson(snap);
            var bytes = Encoding.UTF8.GetBytes(json);
            Interlocked.Exchange(ref _snapshotBytes, bytes);
        }

        void OnRequest(IAsyncResult ar)
        {
            if (_listener == null || !_listener.IsListening) return;

            HttpListenerContext ctx;
            try { ctx = _listener.EndGetContext(ar); }
            catch { return; }
            finally
            {
                try { _listener?.BeginGetContext(OnRequest, null); }
                catch { /* shutting down */ }
            }

            try
            {
                var path = ctx.Request.Url.AbsolutePath;

                if (path == "/api/cmd" && ctx.Request.HttpMethod == "POST")
                {
                    using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
                    var body = reader.ReadToEnd().Trim();
                    if (!string.IsNullOrEmpty(body))
                        _cmdQueue.Enqueue(body);
                    var ok = Encoding.UTF8.GetBytes("ok");
                    ctx.Response.ContentType = "text/plain";
                    ctx.Response.ContentLength64 = ok.Length;
                    ctx.Response.OutputStream.Write(ok, 0, ok.Length);
                    ctx.Response.Close();
                    return;
                }

                if (ctx.Request.HttpMethod != "GET")
                {
                    ctx.Response.StatusCode = 405;
                    ctx.Response.Close();
                    return;
                }

                if (path == "/api/status")
                {
                    var data = _snapshotBytes;
                    if (data == null)
                    {
                        ctx.Response.StatusCode = 503;
                        ctx.Response.Close();
                        return;
                    }
                    ctx.Response.ContentType = "application/json; charset=utf-8";
                    ctx.Response.Headers["Cache-Control"] = "no-store";
                    ctx.Response.ContentLength64 = data.Length;
                    ctx.Response.OutputStream.Write(data, 0, data.Length);
                }
                else if (path == "/" || path == "/index.html")
                {
#if UNITY_EDITOR
                    LoadDashboard();
#endif
                    var html = _dashboardBytes;
                    ctx.Response.ContentType = "text/html; charset=utf-8";
                    ctx.Response.ContentLength64 = html.Length;
                    ctx.Response.OutputStream.Write(html, 0, html.Length);
                }
                else
                {
                    ctx.Response.StatusCode = 404;
                }
                ctx.Response.Close();
            }
            catch { /* client disconnect */ }
        }

        void OnDestroy()
        {
            if (_listener != null && _listener.IsListening)
            {
                _listener.Close();
                Debug.Log("[StatusServer] Stopped.");
            }
            _listener = null;
        }

        void OnApplicationQuit() => OnDestroy();
    }
}
