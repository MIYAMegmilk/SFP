using System.Collections.Generic;
using UnityEngine;
using SFP.Simulation;

namespace SFP.Presentation
{
    public class CrewVisualManager : MonoBehaviour
    {
        static CrewVisualManager _instance;
        public static CrewVisualManager Instance => _instance;

        int _selectedCrewId = -1;

        sealed class Visual
        {
            public GameObject Root;
            public MeshRenderer StatusRenderer;
            public Material StatusMat;
            public GameObject SelectionRing;
            public TextMesh Label;
            public float SmoothedX, SmoothedY, SmoothedZ;
            public bool Initialized;
        }

        readonly Dictionary<int, Visual> _visuals = new();
        Material _bodyMat;

        static readonly Color IdleColor = new Color(0.2f, 0.8f, 0.2f);
        static readonly Color FireColor = new Color(1f, 0.15f, 0.05f);
        static readonly Color RepairColor = new Color(1f, 0.6f, 0.1f);
        static readonly Color DeviceColor = new Color(0.1f, 0.8f, 0.9f);
        static readonly Color MoveColor = Color.white;
        static readonly Color FleeColor = new Color(0.9f, 0.2f, 0.9f);
        static readonly Color DeadColor = new Color(0.4f, 0.4f, 0.4f);

        static readonly Color CaptainColor = new Color(0.3f, 0.5f, 0.7f);
        static readonly Color EngineerColor = new Color(0.8f, 0.6f, 0.2f);
        static readonly Color MechanicColor = new Color(0.2f, 0.7f, 0.3f);
        static readonly Color DamageControlColor = new Color(0.7f, 0.25f, 0.25f);

        const float LerpSpeed = 10f;

        void Awake()
        {
            _instance = this;
            _bodyMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _bodyMat.color = new Color(0.3f, 0.5f, 0.7f);
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        public void SetSelected(int crewId)
        {
            _selectedCrewId = crewId;
        }

        public int SelectedCrewId => _selectedCrewId;

        void Update()
        {
            var bridge = SimulationBridge.Instance;
            var system = bridge?.CrewSystem;
            if (system == null) return;

            var crew = system.Crew;
            var active = new HashSet<int>();

            for (int i = 0; i < crew.Count; i++)
            {
                var c = crew[i];
                active.Add(c.Id);

                if (!_visuals.TryGetValue(c.Id, out var vis))
                {
                    vis = CreateVisual(c);
                    _visuals[c.Id] = vis;
                }

                if (vis.Root == null) continue;

                // smooth position
                if (!vis.Initialized)
                {
                    vis.SmoothedX = c.X;
                    vis.SmoothedY = c.Y;
                    vis.SmoothedZ = c.Z;
                    vis.Initialized = true;
                }
                else
                {
                    float t = Time.deltaTime * LerpSpeed;
                    vis.SmoothedX = Mathf.Lerp(vis.SmoothedX, c.X, t);
                    vis.SmoothedY = Mathf.Lerp(vis.SmoothedY, c.Y, t);
                    vis.SmoothedZ = Mathf.Lerp(vis.SmoothedZ, c.Z, t);
                }

                vis.Root.transform.localPosition = new Vector3(vis.SmoothedX, vis.SmoothedY, vis.SmoothedZ);

                // face movement direction
                if (c.VelX * c.VelX + c.VelZ * c.VelZ > 0.01f)
                {
                    var dir = new Vector3(c.VelX, 0f, c.VelZ).normalized;
                    vis.Root.transform.localRotation = Quaternion.LookRotation(dir);
                }

                // dead
                if (c.IsDead)
                {
                    vis.StatusMat.SetColor("_EmissionColor", DeadColor * 0.5f);
                    vis.Root.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    vis.SelectionRing.SetActive(false);
                    continue;
                }

                // status color
                Color statusColor = GetTaskColor(c.Task);
                vis.StatusMat.SetColor("_EmissionColor", statusColor * 2f);

                // selection ring
                vis.SelectionRing.SetActive(c.Id == _selectedCrewId);

                // billboard label
                if (vis.Label != null && Camera.main != null)
                {
                    var labelT = vis.Label.transform;
                    labelT.rotation = Camera.main.transform.rotation;
                }
            }

            // cleanup removed
            var toRemove = new List<int>();
            foreach (var kvp in _visuals)
            {
                if (!active.Contains(kvp.Key))
                {
                    if (kvp.Value.Root != null) Destroy(kvp.Value.Root);
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var id in toRemove) _visuals.Remove(id);

            // drain events
            while (system.TryDequeueEvent(out var evt))
            {
                if (evt.Kind == CrewEventKind.BreachSealed)
                    HandleBreachSealed(evt.TargetId);
            }
        }

        Visual CreateVisual(CrewMemberState c)
        {
            var root = new GameObject($"Crew_{c.Id}");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(c.X, c.Y, c.Z);

            // body capsule — color by job
            var bodyMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            bodyMat.color = GetJobColor(c.Job);

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
            body.GetComponent<MeshRenderer>().sharedMaterial = bodyMat;

            // keep collider for raycast selection
            var refComp = root.AddComponent<CrewMemberRef>();
            refComp.CrewId = c.Id;

            // add collider on root too for easier raycast
            var col = root.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 0.9f, 0f);
            col.radius = 0.3f;
            col.height = 1.8f;

            // remove body's default collider
            var bodyCol = body.GetComponent<Collider>();
            if (bodyCol != null) Destroy(bodyCol);

            // status indicator sphere
            var statusMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            statusMat.EnableKeyword("_EMISSION");
            statusMat.SetColor("_EmissionColor", IdleColor * 2f);
            statusMat.color = Color.white;

            var status = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            status.name = "Status";
            var statusCol = status.GetComponent<Collider>();
            if (statusCol != null) Destroy(statusCol);
            status.transform.SetParent(root.transform, false);
            status.transform.localPosition = new Vector3(0f, 2.1f, 0f);
            status.transform.localScale = Vector3.one * 0.25f;
            var statusRenderer = status.GetComponent<MeshRenderer>();
            statusRenderer.sharedMaterial = statusMat;

            // selection ring
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "SelectionRing";
            var ringCol = ring.GetComponent<Collider>();
            if (ringCol != null) Destroy(ringCol);
            ring.transform.SetParent(root.transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            ring.transform.localScale = new Vector3(1f, 0.02f, 1f);
            var ringMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            ringMat.color = new Color(0f, 1f, 0.5f, 0.8f);
            ringMat.EnableKeyword("_EMISSION");
            ringMat.SetColor("_EmissionColor", new Color(0f, 1f, 0.5f) * 1.5f);
            ring.GetComponent<MeshRenderer>().sharedMaterial = ringMat;
            ring.SetActive(false);

            // name label
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(root.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 2.5f, 0f);
            var label = labelGo.AddComponent<TextMesh>();
            label.text = $"{CrewJob.GetLabel(c.Job)}{c.Id + 1}";
            label.characterSize = 0.15f;
            label.fontSize = 48;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.color = Color.white;

            return new Visual
            {
                Root = root,
                StatusRenderer = statusRenderer,
                StatusMat = statusMat,
                SelectionRing = ring,
                Label = label,
            };
        }

        static Color GetTaskColor(CrewTaskKind task)
        {
            switch (task)
            {
                case CrewTaskKind.FightFire: return FireColor;
                case CrewTaskKind.RepairBreach: return RepairColor;
                case CrewTaskKind.OperatePump: return DeviceColor;
                case CrewTaskKind.OperateReactor: return DeviceColor;
                case CrewTaskKind.MoveTo: return MoveColor;
                case CrewTaskKind.Flee: return FleeColor;
                default: return IdleColor;
            }
        }

        static Color GetJobColor(CrewJobKind job)
        {
            switch (job)
            {
                case CrewJobKind.Captain:       return CaptainColor;
                case CrewJobKind.Engineer:      return EngineerColor;
                case CrewJobKind.Mechanic:      return MechanicColor;
                case CrewJobKind.DamageControl: return DamageControlColor;
                default: return CaptainColor;
            }
        }

        void HandleBreachSealed(int openingId)
        {
            var breachVisuals = FindObjectsByType<BreachVisual>(FindObjectsSortMode.None);
            for (int i = 0; i < breachVisuals.Length; i++)
            {
                var bv = breachVisuals[i];
                if (bv.Opening != null && bv.Opening.Id == openingId)
                {
                    Destroy(bv.gameObject);
                    return;
                }
            }
        }
    }
}
