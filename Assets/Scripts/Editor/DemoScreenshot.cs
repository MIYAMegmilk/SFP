using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;
using SFP.Presentation;

public static class DemoScreenshot
{
    [MenuItem("SFP/Debug/Capture Demo Shots")]
    public static void Capture()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogWarning("[DemoScreenshot] Only works in play mode.");
            return;
        }

        var go = new GameObject("~DemoScreenshotRunner") { hideFlags = HideFlags.HideAndDontSave };
        var runner = go.AddComponent<DemoScreenshotRunner>();
        runner.StartCoroutine(runner.Run());
    }
}

public class DemoScreenshotRunner : MonoBehaviour
{
    const string Dir = "C:/My project/Screenshots";

    public IEnumerator Run()
    {
        Directory.CreateDirectory(Dir);

        var bridge = SimulationBridge.Instance;

        var sonar = bridge.GetSonar(0);
        sonar.IsActive = true;

        // Two-phase reactor control so the grid comes online without a meltdown
        // (heat = FissionRate*0.01*HeatRate 8, cooling = TurbineOutput*0.01*CoolingRate 5;
        //  FissionRate/TurbineOutput are on a 0-100 percent scale).
        float t = 0f;
        if (bridge.PowerGrid.Reactors.Count > 0)
        {
            var r = bridge.PowerGrid.Reactors[0];

            // Phase 1: warm up (~+3.8C/s) toward the 50C optimal temperature.
            r.FissionRate = 60f;
            r.TurbineOutput = 20f;
            while (r.Temperature < 45f && t < 40f)
            {
                t += Time.deltaTime;
                yield return null;
            }

            // Phase 2: steady state (heat 3.52 vs cooling 3.5), ~1400kW output.
            r.FissionRate = 44f;
            r.TurbineOutput = 70f;
        }

        // Wait until consumer nodes activate (PowerGrid enables nodes at
        // GridVoltage >= 0.2), then give the hologram time to rebuild
        // (1s interval after activation).
        t = 0f;
        while (bridge.PowerGrid.GridVoltage < 0.3f && t < 30f)
        {
            t += Time.deltaTime;
            yield return null;
        }
        Debug.Log($"[DemoScreenshot] GridVoltage {bridge.PowerGrid.GridVoltage:F2} after {t:F1}s");
        yield return new WaitForSeconds(2.5f);

        var flyCam = Object.FindFirstObjectByType<SFP.Gameplay.FlyCamera>();
        var player = Object.FindFirstObjectByType<SFP.Gameplay.PlayerController>();
        if (flyCam == null || player == null)
        {
            Debug.LogWarning("[DemoScreenshot] Missing FlyCamera or PlayerController; aborting.");
            Destroy(gameObject);
            yield break;
        }

        var playerCam = player.GetComponentInChildren<Camera>();
        var specCam = flyCam.GetComponent<Camera>();

        playerCam.enabled = false;
        player.enabled = false;
        playerCam.gameObject.tag = "Untagged";

        specCam.enabled = true;
        specCam.gameObject.tag = "MainCamera";
        flyCam.enabled = false;

        var specTransform = specCam.transform;

        var hologramGo = GameObject.Find("SonarHologram");
        if (hologramGo == null)
        {
            Debug.LogWarning("[DemoScreenshot] SonarHologram not found; aborting.");
            Destroy(gameObject);
            yield break;
        }
        Vector3 hologramPos = hologramGo.transform.position;

        specTransform.position = hologramPos + new Vector3(0.85f, 0.45f, -0.85f);
        specTransform.LookAt(hologramPos + Vector3.up * 0.05f);

        yield return new WaitForSeconds(3f);

        ScreenCapture.CaptureScreenshot(Dir + "/hologram.png");
        yield return new WaitForSeconds(1f);

        var sub = bridge.SubState;
        Vector3 p = new Vector3(sub.PositionX, -sub.Depth, sub.PositionZ);
        specTransform.position = p + new Vector3(-45f, 18f, -45f);
        specTransform.LookAt(p);

        yield return new WaitForSeconds(0.75f);

        ScreenCapture.CaptureScreenshot(Dir + "/exterior.png");
        yield return new WaitForSeconds(1f);

        specTransform.position = hologramPos + new Vector3(-0.7f, 0.25f, 0.7f);
        specTransform.LookAt(hologramPos);

        yield return new WaitForSeconds(0.75f);

        ScreenCapture.CaptureScreenshot(Dir + "/hologram2.png");
        yield return new WaitForSeconds(1f);

        Debug.Log("[DemoScreenshot] done");
        Destroy(gameObject);
    }
}
