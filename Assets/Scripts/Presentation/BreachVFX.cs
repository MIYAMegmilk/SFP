using UnityEngine;
using SFP.Simulation;

namespace SFP.Presentation
{
    public class BreachVFX : MonoBehaviour
    {
        ParticleSystem _ps;
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
        }
    }
}
