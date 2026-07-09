using UnityEditor;
using UnityEngine;
using SFP.Gameplay;

public static class DebugRemoteMenu
{
    static DebugRemoteControl FindRC()
    {
        var rc = Object.FindFirstObjectByType<DebugRemoteControl>();
        if (rc == null)
            Debug.Log("[RC] DebugRemoteControl not found — is Play mode active?");
        return rc;
    }

    [MenuItem("SFP/Debug/Ship Status")]
    static void ShipStatus() { var rc = FindRC(); if (rc) rc.Command = "status"; }

    [MenuItem("SFP/Debug/Mission Status")]
    static void MissionStatus() { var rc = FindRC(); if (rc) rc.Command = "mission"; }

    [MenuItem("SFP/Debug/Damage Report")]
    static void DamageReport() { var rc = FindRC(); if (rc) rc.Command = "damage"; }

    [MenuItem("SFP/Debug/Power Status")]
    static void PowerStatus() { var rc = FindRC(); if (rc) rc.Command = "power"; }

    [MenuItem("SFP/Debug/Flood Status")]
    static void FloodStatus() { var rc = FindRC(); if (rc) rc.Command = "flood"; }

    [MenuItem("SFP/Debug/All Stop")]
    static void AllStop() { var rc = FindRC(); if (rc) rc.Command = "stop"; }

    [MenuItem("SFP/Debug/Emergency Surface")]
    static void Surface() { var rc = FindRC(); if (rc) rc.Command = "surface"; }

    [MenuItem("SFP/Debug/Navigate to Mission")]
    static void Navigate() { var rc = FindRC(); if (rc) rc.Command = "navigate"; }

    [MenuItem("SFP/Debug/Help")]
    static void Help() { var rc = FindRC(); if (rc) rc.Command = "help"; }
}
