using System;
using UnityEngine;

namespace Monlit
{
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class WorkshopGrips : MonoBehaviour
    {
        [Serializable]
        private sealed class Arm
        {
            [SerializeField] private Transform shoulder;
            [SerializeField] private Transform sleeve;
            [SerializeField] private Transform elbow;
            [SerializeField] private Transform forearm;
            [SerializeField] private Transform wrist;

            public Arm(Transform armRoot)
            {
                shoulder = armRoot;
                sleeve = armRoot.Find("Sleeve");
                elbow = armRoot.Find("Elbow");
                forearm = armRoot.Find("Forearm");
                wrist = armRoot.Find("Wrist");
            }

            public void Pose(Transform robot, Transform contact, float side, bool immediate)
            {
                if (contact)
                {
                    // The socket faces into the casting, so the palm stays outside its edge.
                    wrist.SetPositionAndRotation(contact.position, contact.rotation);
                }
                else
                {
                    Vector3 rest = robot.TransformPoint(new Vector3(side * 0.46f, 0.50f, 0.37f));
                    if (immediate)
                        wrist.SetPositionAndRotation(rest, robot.rotation);
                    else
                        wrist.SetPositionAndRotation(Vector3.MoveTowards(wrist.position, rest, 7f * Time.deltaTime),
                            Quaternion.RotateTowards(wrist.rotation, robot.rotation, 1000f * Time.deltaTime));
                }

                Vector3 palmBase = wrist.position - wrist.forward * 0.17f;
                Vector3 reach = palmBase - shoulder.position;
                // Bend toward the outside of the chassis, leaving the front clear for the piece.
                Vector3 bend = Vector3.ProjectOnPlane(robot.right * side - robot.up * 0.25f, reach.normalized);
                elbow.position = Vector3.Lerp(shoulder.position, palmBase, 0.45f) + bend * 0.16f;
                FitLink(sleeve, shoulder.position, elbow.position, robot.up);
                FitLink(forearm, elbow.position, palmBase, robot.up);
            }

            private static void FitLink(Transform link, Vector3 start, Vector3 end, Vector3 up)
            {
                Vector3 span = end - start;
                link.position = (start + end) * 0.5f;
                if (span.sqrMagnitude > 0.00001f)
                    link.rotation = Quaternion.LookRotation(span, up);
                Vector3 scale = link.localScale;
                scale.z = span.magnitude + 0.055f;
                link.localScale = scale;
            }
        }

        [SerializeField] private WorkshopRobot robot;
        [SerializeField] private Arm left;
        [SerializeField] private Arm right;

        public void Configure(WorkshopRobot controller)
        {
            robot = controller;
            left = new Arm(transform.Find("Visuals/Left Arm"));
            right = new Arm(transform.Find("Visuals/Right Arm"));
            left.Pose(transform, null, -1f, true);
            right.Pose(transform, null, 1f, true);
        }

        private void LateUpdate()
        {
            left.Pose(transform, robot.LeftGrip, -1f, false);
            right.Pose(transform, robot.RightGrip, 1f, false);
        }
    }
}
