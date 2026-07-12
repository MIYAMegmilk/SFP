using UnityEngine;

namespace SFP.Presentation
{
    // Auto-registers a sibling Light with BackscatterLightManager on enable/disable.
    // Attached to hull headlights by FloodTestShipBuilder.
    [RequireComponent(typeof(Light))]
    public sealed class BackscatterLightRegistrar : MonoBehaviour
    {
        Light _light;

        void Awake()
        {
            _light = GetComponent<Light>();
        }

        void OnEnable()
        {
            BackscatterLightManager.RegisterLight(_light);
        }

        void OnDisable()
        {
            BackscatterLightManager.UnregisterLight(_light);
        }
    }
}
