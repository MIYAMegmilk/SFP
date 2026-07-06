using UnityEngine;
using UnityEngine.InputSystem;
using SFP.Presentation;

namespace SFP.Gameplay
{
    public class PumpPlacer : MonoBehaviour
    {
        public float MaxDistance = 50f;
        public float PumpRate = 0.5f;

        static Material s_pumpMat;

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;
            if (!kb.rKey.isPressed) return;
            if (!mouse.leftButton.wasPressedThisFrame) return;

            var ray = Camera.main.ScreenPointToRay(mouse.position.ReadValue());
            if (!Physics.Raycast(ray, out var hit, MaxDistance)) return;

            var compDef = hit.collider.GetComponentInParent<CompartmentDefinition>();
            if (compDef == null) return;

            PlacePump(compDef, hit.point);
        }

        void PlacePump(CompartmentDefinition compDef, Vector3 worldPos)
        {
            var go = new GameObject("PlacedPump");
            go.transform.position = new Vector3(worldPos.x, compDef.FloorY + 0.25f, worldPos.z);

            var pump = go.AddComponent<Pump>();
            pump.TargetCompartment = compDef;
            pump.PumpRate = PumpRate;
            pump.IsActive = true;

            BuildVisual(go);
        }

        void BuildVisual(GameObject parent)
        {
            if (s_pumpMat == null)
            {
                s_pumpMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                s_pumpMat.color = new Color(0.9f, 0.3f, 0.1f, 1f);
            }

            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "PumpBody";
            body.transform.SetParent(parent.transform, false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localScale = new Vector3(0.4f, 0.25f, 0.4f);
            body.GetComponent<MeshRenderer>().sharedMaterial = s_pumpMat;
            body.GetComponent<Collider>().enabled = false;

            var pipe = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pipe.name = "PumpPipe";
            pipe.transform.SetParent(parent.transform, false);
            pipe.transform.localPosition = new Vector3(0f, 0.35f, 0f);
            pipe.transform.localScale = new Vector3(0.1f, 0.15f, 0.1f);
            pipe.GetComponent<MeshRenderer>().sharedMaterial = s_pumpMat;
            pipe.GetComponent<Collider>().enabled = false;
        }
    }
}
