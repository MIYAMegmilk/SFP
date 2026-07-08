using UnityEngine;
using SFP.Presentation;

namespace SFP.Gameplay
{
    public class CrushDepthDamage : MonoBehaviour
    {
        public bool Enabled = true;

        void Update()
        {
            var ds = SimulationBridge.Instance?.DamageSystem;
            if (ds != null) ds.CrushStressEnabled = Enabled;
        }
    }
}
