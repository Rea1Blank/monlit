using System;
using UnityEngine;

namespace Monlit
{
    public sealed class WorkshopNailVisual : MonoBehaviour
    {
        [Serializable]
        public struct Finishes
        {
            public Mesh Block, Round;
            public Material Steel, Dark, Orange, Brass;
        }

        private const float ImpactTime = .115f;
        private const float StrikeDuration = .195f;
        private Transform nail, hammer;
        private float elapsed;
        public Transform Handle => hammer;

        public static WorkshopNailVisual Strike(WorkshopPiece piece, Transform robot, Finishes kit)
        {
            if (!kit.Block || !kit.Round || !kit.Steel || !kit.Dark || !kit.Orange || !kit.Brass) return null;
            var root = Child(piece.transform, "Nail", StrikePoint(piece, robot));
            var forward = Vector3.ProjectOnPlane(root.position - robot.position, Vector3.up);
            root.rotation = Quaternion.LookRotation(forward.sqrMagnitude > .001f ? forward : robot.forward);
            var visual = root.gameObject.AddComponent<WorkshopNailVisual>();

            visual.nail = Child(root, "Fastener", Vector3.up * .13f);
            Part(visual.nail, "Shank", kit.Round, kit.Brass, new Vector3(0f,-.055f,0f), new Vector3(.07f,.19f,.07f));
            Part(visual.nail, "Head", kit.Round, kit.Dark, new Vector3(0f,.029f,0f), new Vector3(.235f,.048f,.235f));
            Part(visual.nail, "Struck face", kit.Round, kit.Steel, new Vector3(0f,.053f,0f), new Vector3(.205f,.021f,.205f));
            Part(visual.nail, "Hammer mark", kit.Round, kit.Dark, new Vector3(.035f,.064f,-.019f), new Vector3(.046f,.004f,.035f));

            visual.hammer = Child(root, "Hammer", new Vector3(0f,.225f,-.48f));
            Part(visual.hammer, "Handle", kit.Block, kit.Orange, new Vector3(0f,0f,.23f), new Vector3(.11f,.11f,.50f));
            Part(visual.hammer, "Grip", kit.Block, kit.Dark, Vector3.zero, new Vector3(.135f,.135f,.21f));
            Part(visual.hammer, "Head", kit.Block, kit.Dark, new Vector3(0f,0f,.48f), new Vector3(.28f,.245f,.19f));
            Part(visual.hammer, "Striking face", kit.Block, kit.Steel, new Vector3(0f,-.13f,.48f), new Vector3(.28f,.065f,.19f));
            Part(visual.hammer, "Peen", kit.Block, kit.Steel, new Vector3(0f,.13f,.48f), new Vector3(.17f,.065f,.16f));
            visual.hammer.localRotation = Quaternion.Euler(-65f,0f,0f);
            return visual;
        }

        // The centroid of a concave casting may be outside its lid. Place the
        // shank above solid metal on the side the robot can actually reach.
        private static Vector3 StrikePoint(WorkshopPiece piece, Transform robot)
        {
            Vector3 hand = piece.transform.InverseTransformPoint(robot.TransformPoint(new Vector3(.42f,.5f,.35f)));
            Transform eye = piece.transform.Find("Rope Anchor");
            Vector3 eyePoint = eye ? piece.transform.InverseTransformPoint(eye.position) : Vector3.zero;
            Vector3 spot = Vector3.zero;
            float best = float.PositiveInfinity;
            bool foundClear = false;
            foreach (var box in piece.GetComponents<BoxCollider>())
            {
                if (box.isTrigger) continue;
                var half = box.size * .5f;
                float x = Mathf.Max(0f,half.x - .14f), z = Mathf.Max(0f,half.z - .14f);
                bool Consider(Vector3 aim)
                {
                    var point = box.center + new Vector3(Mathf.Clamp(aim.x - box.center.x,-x,x),half.y,
                        Mathf.Clamp(aim.z - box.center.z,-z,z));
                    float dx = point.x - eyePoint.x, dz = point.z - eyePoint.z;
                    bool clear = !eye || dx*dx + dz*dz >= .32f*.32f;
                    float distance = (point - hand).sqrMagnitude;
                    if ((clear && !foundClear) || (clear == foundClear && distance < best))
                    {
                        foundClear = clear;
                        best = distance;
                        spot = point;
                    }
                    return clear;
                }
                if (Consider(hand)) continue;
                // If the nearest reachable patch is the eyelet, try the lid just outside it.
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI * .25f;
                    Consider(eyePoint + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * .35f);
                }
            }
            return spot;
        }

        private static Transform Child(Transform parent, string name, Vector3 position)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent,false);
            child.localPosition = position;
            return child;
        }

        private static void Part(Transform parent, string name, Mesh mesh, Material finish, Vector3 center, Vector3 size)
        {
            var part = Child(parent,name,center);
            var bounds = mesh.bounds.size;
            part.localScale = new Vector3(size.x/bounds.x,size.y/bounds.y,size.z/bounds.z);
            part.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            part.gameObject.AddComponent<MeshRenderer>().sharedMaterial = finish;
        }

        private void Update()
        {
            float previous = elapsed;
            elapsed += Time.deltaTime;
            if (previous < ImpactTime && elapsed >= ImpactTime)
                WorkshopAudio.Play(WorkshopAudio.Effect.Hammer, nail.position);
            if (elapsed >= StrikeDuration)
            {
                nail.localPosition = Vector3.zero;
                Destroy(hammer.gameObject);
                hammer = null;
                enabled = false;
                return;
            }
            float down = Mathf.Clamp01(elapsed / ImpactTime);
            float recoil = Mathf.InverseLerp(ImpactTime, StrikeDuration, elapsed);
            hammer.localRotation = Quaternion.Euler(-65f * (1f - down * down) - 25f * recoil,0f,0f);
            nail.localPosition = Vector3.up * (.13f * (1f - Mathf.InverseLerp(.78f,1f,down)));
        }
    }
}
