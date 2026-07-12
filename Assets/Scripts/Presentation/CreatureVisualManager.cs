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
        // Bioluminescence: cyan glow that intensifies with depth (design doc §5.3)
        static readonly Color BioLumColor = new Color(0.05f, 0.4f, 0.5f, 1f);
        const float BioLumStartDepth = 200f;
        const float BioLumFullDepth = 800f;
        const float BaseIntensity = 0.5f;
        const float AttackIntensity = 3.5f;
        const float BioLumIntensity = 2f;
        const float EmissionLerpSpeed = 4f;

        sealed class Visual
        {
            public GameObject Root;
            public Material BodyMat;
            public float EmissionT;
        }

        readonly Dictionary<int, Visual> _visualDict = new();

        void Start()
        {
            // Materials are created per-creature in CreateVisual
        }

        void Update()
        {
            var bridge = SimulationBridge.Instance;
            var system = bridge != null ? bridge.Creatures : null;
            if (system == null) return;

            var creatures = system.Creatures;

            // Track which Ids are still present
            var currentIds = new HashSet<int>();

            for (int i = 0; i < creatures.Count; i++)
            {
                var creature = creatures[i];
                currentIds.Add(creature.Id);

                // Create visual for new creatures
                if (!_visualDict.ContainsKey(creature.Id))
                {
                    var visual = CreateVisual(creature);
                    _visualDict[creature.Id] = visual;
                }

                // Update existing visual
                var vis = _visualDict[creature.Id];
                if (vis.Root == null) continue;

                if (creature.IsDead)
                {
                    if (vis.Root.activeSelf)
                        vis.Root.SetActive(false);
                    continue;
                }

                vis.Root.transform.position = new Vector3(creature.X, -creature.Depth, creature.Z);

                var vel = new Vector3(creature.VelX, -creature.VelDepth, creature.VelZ);
                if (vel.sqrMagnitude > 0.01f)
                    vis.Root.transform.rotation = Quaternion.LookRotation(vel.normalized);

                bool attacking = creature.Behavior == CreatureBehavior.Attack;
                vis.EmissionT = Mathf.MoveTowards(vis.EmissionT, attacking ? 1f : 0f,
                    Time.deltaTime * EmissionLerpSpeed);
                Color emission = Color.Lerp(BaseEmission * BaseIntensity, AttackEmission * AttackIntensity,
                    vis.EmissionT);

                // Bioluminescence overlay: adds cyan glow at depth
                float bioFactor = Mathf.InverseLerp(BioLumStartDepth, BioLumFullDepth, creature.Depth);
                emission += BioLumColor * (bioFactor * BioLumIntensity);

                vis.BodyMat.SetColor("_EmissionColor", emission);
            }

            // Destroy visuals for creatures no longer present
            var toRemove = new List<int>();
            foreach (var kvp in _visualDict)
            {
                if (!currentIds.Contains(kvp.Key))
                {
                    if (kvp.Value.Root != null)
                        Destroy(kvp.Value.Root);
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var id in toRemove)
                _visualDict.Remove(id);
        }

        Visual CreateVisual(CreatureState creature)
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

            return new Visual { Root = root, BodyMat = bodyMat };
        }
    }
}
