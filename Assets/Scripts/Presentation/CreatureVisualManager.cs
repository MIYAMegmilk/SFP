using System.Collections.Generic;
using UnityEngine;
using SFP.Simulation;

namespace SFP.Presentation
{
    public class CreatureVisualManager : MonoBehaviour
    {
        static readonly Color BaseColor = new Color(0.22f, 0.08f, 0.06f, 1f);
        static readonly Color BaseEmission = new Color(0.35f, 0.05f, 0.03f, 1f);
        static readonly Color AttackEmission = new Color(1f, 0.05f, 0.02f, 1f);
        const float BaseIntensity = 0.5f;
        const float AttackIntensity = 3.5f;
        const float EmissionLerpSpeed = 4f;

        sealed class Visual
        {
            public GameObject Root;
            public Material BodyMat;
            public float EmissionT;
        }

        readonly List<CreatureState> _creatures = new();
        readonly List<Visual> _visuals = new();
        readonly List<bool> _hidden = new();

        void Start()
        {
            var bridge = SimulationBridge.Instance;
            var system = bridge != null ? bridge.Creatures : null;
            if (system == null) return;

            foreach (var creature in system.Creatures)
            {
                var root = new GameObject("Creature");
                root.transform.SetParent(transform, false);
                root.transform.position = new Vector3(creature.X, -creature.Depth, creature.Z);

                var bodyMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                bodyMat.color = BaseColor;
                bodyMat.EnableKeyword("_EMISSION");
                bodyMat.SetColor("_EmissionColor", BaseEmission * BaseIntensity);
                bodyMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;

                var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                body.name = "Body";
                var bodyCol = body.GetComponent<Collider>();
                if (bodyCol != null) Destroy(bodyCol);
                body.transform.SetParent(root.transform, false);
                body.transform.localScale = new Vector3(2.2f, 1.4f, 5f);
                body.GetComponent<MeshRenderer>().sharedMaterial = bodyMat;

                var tail = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tail.name = "Tail";
                var tailCol = tail.GetComponent<Collider>();
                if (tailCol != null) Destroy(tailCol);
                tail.transform.SetParent(root.transform, false);
                tail.transform.localPosition = new Vector3(0f, 0f, -3.2f);
                tail.transform.localScale = new Vector3(0.5f, 0.4f, 1.4f);
                tail.GetComponent<MeshRenderer>().sharedMaterial = bodyMat;

                _creatures.Add(creature);
                _visuals.Add(new Visual { Root = root, BodyMat = bodyMat });
                _hidden.Add(false);
            }
        }

        void Update()
        {
            for (int i = 0; i < _creatures.Count; i++)
            {
                var creature = _creatures[i];
                var visual = _visuals[i];
                if (visual.Root == null) continue;

                if (creature.IsDead)
                {
                    if (!_hidden[i])
                    {
                        visual.Root.SetActive(false);
                        _hidden[i] = true;
                    }
                    continue;
                }

                visual.Root.transform.position = new Vector3(creature.X, -creature.Depth, creature.Z);

                var vel = new Vector3(creature.VelX, -creature.VelDepth, creature.VelZ);
                if (vel.sqrMagnitude > 0.01f)
                    visual.Root.transform.rotation = Quaternion.LookRotation(vel.normalized);

                bool attacking = creature.Behavior == CreatureBehavior.Attack;
                visual.EmissionT = Mathf.MoveTowards(visual.EmissionT, attacking ? 1f : 0f,
                    Time.deltaTime * EmissionLerpSpeed);
                Color emission = Color.Lerp(BaseEmission * BaseIntensity, AttackEmission * AttackIntensity,
                    visual.EmissionT);
                visual.BodyMat.SetColor("_EmissionColor", emission);
            }
        }
    }
}
