using UnityEngine;
using SFP.Simulation;

namespace SFP.Presentation
{
    public class BreachVFX : MonoBehaviour
    {
        ParticleSystem _ps;
        ParticleSystem _bubblePs;
        Opening _opening;

        public void Init(Opening opening)
        {
            _opening = opening;

            var go = new GameObject("BreachParticles");
            go.transform.SetParent(transform);
            go.transform.localPosition = Vector3.zero;

            _ps = go.AddComponent<ParticleSystem>();
            var main = _ps.main;
            main.maxParticles = 200;
            main.startLifetime = 0.8f;
            main.startSpeed = 3f;
            main.startSize = 0.08f;
            main.startColor = new Color(0.3f, 0.6f, 0.9f, 0.7f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.5f;

            var emission = _ps.emission;
            emission.rateOverTime = 0f;

            var shape = _ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 25f;
            shape.radius = 0.1f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            renderer.material.color = new Color(0.3f, 0.6f, 0.9f, 0.7f);

            InitBubbles();
        }

        // Bubble sub-system (design doc §5.2): air escaping from a pressurized compartment
        // through a sea-facing breach rises as bubbles. Terminal velocity of mm-scale bubbles
        // is 0.2-0.35 m/s (Clift et al. 1978).
        void InitBubbles()
        {
            var go = new GameObject("BreachBubbles");
            go.transform.SetParent(transform);
            go.transform.localPosition = Vector3.zero;

            _bubblePs = go.AddComponent<ParticleSystem>();
            var main = _bubblePs.main;
            main.maxParticles = 500;
            main.startLifetime = 8f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.05f);
            main.startColor = new Color(1f, 1f, 1f, 0.5f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;

            var emission = _bubblePs.emission;
            emission.rateOverTime = 0f;

            var shape = _bubblePs.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.15f;

            // Buoyant rise at terminal velocity
            var vel = _bubblePs.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = 0f;
            vel.y = 0.25f;
            vel.z = 0f;

            // Zigzag wobble (real bubbles follow helical paths)
            var noise = _bubblePs.noise;
            noise.enabled = true;
            noise.strength = 0.05f;
            noise.frequency = 0.5f;
            noise.scrollSpeed = 0.3f;

            // Boyle's law expansion: P·V=const, r ∝ (P0/P)^(1/3).
            // At typical play depth (100-300m) the expansion over 2m rise is negligible;
            // only matters near surface. Use a gentle size-over-lifetime curve.
            var sizeOverLife = _bubblePs.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(0.5f, 1.02f),
                new Keyframe(1f, 1.08f)));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            mat.color = new Color(1f, 1f, 1f, 0.5f);
            renderer.material = mat;
        }

        void Update()
        {
            if (_opening == null || _ps == null) return;

            var bridge = SimulationBridge.Instance;
            if (bridge == null) return;

            bool hasWater = false;
            if (_opening.CompartmentA == Opening.Sea || _opening.CompartmentB == Opening.Sea)
            {
                hasWater = true;
            }
            else
            {
                var compA = bridge.Graph.GetCompartment(_opening.CompartmentA);
                var compB = bridge.Graph.GetCompartment(_opening.CompartmentB);
                hasWater = compA.WaterVolume > 0.01f || compB.WaterVolume > 0.01f;
            }

            bool active = _opening.IsOpen && hasWater;
            var emission = _ps.emission;
            emission.rateOverTime = active ? Mathf.Lerp(20f, 200f, _opening.Area / 0.3f) : 0f;

            UpdateBubbles(bridge, active);
        }

        void UpdateBubbles(SimulationBridge bridge, bool breachActive)
        {
            if (_bubblePs == null) return;

            // Bubbles emit when air escapes to sea: breach is open, connects to sea,
            // and the interior compartment still has air (not fully flooded).
            bool bubbleActive = false;
            if (breachActive && (_opening.CompartmentA == Opening.Sea || _opening.CompartmentB == Opening.Sea))
            {
                int interiorComp = _opening.CompartmentA == Opening.Sea
                    ? _opening.CompartmentB
                    : _opening.CompartmentA;

                if (interiorComp >= 0)
                {
                    var comp = bridge.Graph.GetCompartment(interiorComp);
                    bubbleActive = comp.WaterFraction < 0.95f;
                }
            }

            var bubbleEmission = _bubblePs.emission;
            bubbleEmission.rateOverTime = bubbleActive
                ? Mathf.Lerp(10f, 60f, _opening.Area / 0.3f)
                : 0f;
        }
    }
}
