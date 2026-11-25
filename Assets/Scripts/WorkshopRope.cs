using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Monlit
{
    [ExecuteAlways, DefaultExecutionOrder(100), DisallowMultipleComponent, RequireComponent(typeof(LineRenderer))]
    public sealed class WorkshopRope : MonoBehaviour
    {
        private const float CordWidth = 0.105f;
        private const float SampleSpacing = .20f;
        private const float GripTravel = .11f;
        private const float GripHoldTime = .12f;
        private const float GiveTime = .20f;
        private const float YieldSpeed = .48f;
        // Allow for the trim above the collision hull.
        private const float CoverClearance = .005f;

        [SerializeField] private Transform firstAnchor;
        [SerializeField] private Transform secondAnchor;
        [SerializeField, Min(0.25f)] private float restLength = 3.2f;
        [SerializeField, HideInInspector] private ConfigurableJoint constraint;

        private int segments;
        private Vector3[] points;
        private float[] runs;
        private int[] supports;
        private float[] tightHeights;
        private readonly RaycastHit[] supportHits = new RaycastHit[8];
        private readonly Rigidbody[] contactRow = new Rigidbody[5];
        private LineRenderer cord;
        private Rigidbody firstBody;
        private Rigidbody secondBody;
        private WorkshopPiece firstPiece;
        private WorkshopPiece secondPiece;
        private Vector3 firstLocalOffset;
        private Vector3 secondLocalOffset;
        private Vector3? nextFirstEye;
        private Vector3? nextSecondEye;
        private float blockedTime;
        private Vector3 bendDirection;
        private WorkshopPiece reactingPiece;
        private Vector3 pullDirection;
        private float rock;
        private bool loadedThisStep;
        private bool braceYaw;
        private Rigidbody trackedFollower;
        private bool trackedThisStep;
        private Vector3 lastFollowerPosition;
        private Vector3 catchUpDirection;
        private float stalledTime;
        private bool tensionHeard;
        private float nextSlideSound;
        private SurfaceMotion surfaceMotion;
        private bool hasDrawn;
        private Vector3 drawnStart, drawnEnd;
        private Rect drawnArea;

        // All cords in a round share one movement scan. Sleeping endpoints can still
        // have a different casting slide under their line or be lifted out of it.
        internal sealed class SurfaceMotion
        {
            private readonly WorkshopPiece[] pieces;
            private readonly Pose[] poses;
            private readonly bool[] present;
            private readonly float[] radii;
            private readonly Rect[] changes;
            private int frame = -1;
            private int count;
            internal readonly Rect Deck;

            internal SurfaceMotion(WorkshopPiece[] pieces)
            {
                this.pieces = pieces;
                poses = new Pose[pieces.Length];
                present = new bool[pieces.Length];
                changes = new Rect[pieces.Length];
                radii = new float[pieces.Length];
                for (int i = 0; i < pieces.Length; i++)
                    if (pieces[i]) radii[i] = pieces[i].DeckClearance;
            }

            internal SurfaceMotion(WorkshopPiece[] pieces, BoxCollider floor) : this(pieces)
            {
                // The board may have just been resized, before the round's first physics sync.
                Vector3 center = floor.transform.TransformPoint(floor.center);
                Vector3 half = Vector3.Scale(floor.size * .5f, floor.transform.lossyScale);
                const float rim = .31f;
                Deck = Rect.MinMaxRect(center.x - half.x + rim, center.z - half.z + rim,
                    center.x + half.x - rim, center.z + half.z - rim);
            }

            internal bool HasChange(Rect footprint)
            {
                if (frame != Time.frameCount) Sample();
                for (int i = 0; i < count; i++)
                    if (changes[i].Overlaps(footprint)) return true;
                return false;
            }

            private void Sample()
            {
                frame = Time.frameCount;
                count = 0;
                for (int i = 0; i < pieces.Length; i++)
                {
                    var piece = pieces[i];
                    bool active = piece && piece.gameObject.activeInHierarchy;
                    if (!active && !present[i]) continue;
                    var previous = poses[i];
                    var current = previous;
                    if (active)
                        piece.transform.GetPositionAndRotation(out current.position, out current.rotation);
                    if (active && present[i] &&
                        (current.position - previous.position).sqrMagnitude < .000004f &&
                        Mathf.Abs(Quaternion.Dot(current.rotation, previous.rotation)) > .9999996f) continue;

                    // Keep the previous bounds as well when a support leaves the scene.
                    float radius = radii[i];
                    var a = present[i] ? previous.position : current.position;
                    var b = current.position;
                    changes[count++] = Rect.MinMaxRect(Mathf.Min(a.x, b.x) - radius, Mathf.Min(a.z, b.z) - radius,
                        Mathf.Max(a.x, b.x) + radius, Mathf.Max(a.z, b.z) + radius);
                    present[i] = active;
                    poses[i] = current;
                }
            }
        }

        public float SlackLength => restLength;
        public float DeployedLength => restLength;
        public float Separation => firstBody && secondBody
            ? Vector3.Distance(Eye(firstBody, firstLocalOffset), Eye(secondBody, secondLocalOffset)) : 0f;
        public bool IsTaut => Separation >= restLength - .015f;

        public void Configure(Transform first, Transform second, Material material, float length = 3.2f)
            => Configure(first, second, material, length, null);

        internal void Configure(Transform first, Transform second, Material material, float length, SurfaceMotion motion)
        {
            if (!first || !second || !material)
                throw new ArgumentException("The rope requires two anchor transforms and a cord material.");

            ClearConstraint();
            firstAnchor = first;
            secondAnchor = second;
            restLength = Mathf.Max(0.25f, length);
            surfaceMotion = motion;
            hasDrawn = false;
            CacheAttachments();
            if (!firstBody || !secondBody || firstBody == secondBody)
                throw new ArgumentException("Attach each end of the rope to a different rigidbody.");

            cord = GetComponent<LineRenderer>();
            cord.useWorldSpace = true;
            cord.loop = false;
            cord.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
            cord.widthMultiplier = CordWidth;
            cord.numCornerVertices = 2;
            cord.numCapVertices = 3;
            cord.textureMode = LineTextureMode.Tile;
            cord.generateLightingData = true;
            cord.sharedMaterial = material;
            AttachConstraint();
            if (isActiveAndEnabled) DrawCord();
        }

        private void OnEnable()
        {
            hasDrawn = false;
            cord = GetComponent<LineRenderer>();
            CacheAttachments();
            AttachConstraint();
            DrawCord();
        }

        private void CacheAttachments()
        {
            firstBody = firstAnchor ? firstAnchor.GetComponentInParent<Rigidbody>(true) : null;
            secondBody = secondAnchor ? secondAnchor.GetComponentInParent<Rigidbody>(true) : null;
            firstPiece = firstBody ? firstBody.GetComponent<WorkshopPiece>() : null;
            secondPiece = secondBody ? secondBody.GetComponent<WorkshopPiece>() : null;
            if (firstBody)
                firstLocalOffset = LocalOffset(firstBody, firstAnchor);
            if (secondBody)
                secondLocalOffset = LocalOffset(secondBody, secondAnchor);
        }

        private static Vector3 LocalOffset(Rigidbody body, Transform anchor)
        {
            return Quaternion.Inverse(body.transform.rotation) * (anchor.position - body.transform.position);
        }

        internal bool ContainsBody(Rigidbody body) =>
            (firstAnchor && firstAnchor.IsChildOf(body.transform)) ||
            (secondAnchor && secondAnchor.IsChildOf(body.transform));

        internal WorkshopPiece OtherEnd(WorkshopPiece piece) => piece == firstPiece ? secondPiece : firstPiece;

        private static Vector3 Eye(Rigidbody body, Vector3 offset) => body.position + body.rotation * offset;

        internal bool CanReachAt(WorkshopPiece lead, Vector3 position, Quaternion rotation)
        {
            bool first = lead == firstPiece;
            Rigidbody other = first ? secondBody : firstBody;
            if (!other) return true;
            Vector3 otherEye = Eye(other, first ? secondLocalOffset : firstLocalOffset);
            Vector3 eye = position + rotation * (first ? firstLocalOffset : secondLocalOffset);
            float limit = restLength + .002f;
            return (eye - otherEye).sqrMagnitude <= limit * limit;
        }

        // Fit only the automatic lift or reach to the slack between the two eyelets.
        // The carrier's own motion still goes through TryMove to tow or release.
        internal Vector3 FitPickupStep(WorkshopPiece lead, Vector3 from, Vector3 to, Quaternion rotation)
        {
            bool first = lead == firstPiece;
            var otherBody = first ? secondBody : firstBody;
            if (!otherBody) return to;
            var otherEye = (first ? nextSecondEye : nextFirstEye) ??
                Eye(otherBody, first ? secondLocalOffset : firstLocalOffset);
            var start = from + rotation * (first ? firstLocalOffset : secondLocalOffset) - otherEye;
            var step = to - from;
            // Leave a few millimetres between automatic hand travel and the hard stop.
            float limit = restLength - .005f;
            if ((start + step).sqrMagnitude <= limit * limit) return to;
            float room = limit * limit - start.sqrMagnitude;
            if (room <= 0f || step.sqrMagnitude < .00000001f) return from;
            float dot = Vector3.Dot(start, step);
            float t = (Mathf.Sqrt(dot * dot + step.sqrMagnitude * room) - dot) / step.sqrMagnitude;
            return from + step * Mathf.Clamp01(t);
        }

        private void AttachConstraint()
        {
            if (!isActiveAndEnabled || !Application.IsPlaying(gameObject) || !firstBody || !secondBody)
                return;

            // Three limited linear axes form a spherical stop. Inside the sphere the
            // joint exerts no force. At its surface both eyes participate in the contact solve.
            if (!constraint) constraint = firstBody.gameObject.AddComponent<ConfigurableJoint>();
            constraint.autoConfigureConnectedAnchor = false;
            constraint.connectedBody = secondBody;
            constraint.anchor = firstBody.transform.InverseTransformPoint(firstAnchor.position);
            constraint.connectedAnchor = secondBody.transform.InverseTransformPoint(secondAnchor.position);
            constraint.linearLimit = new SoftJointLimit { limit = restLength, contactDistance = .035f };
            constraint.xMotion = constraint.yMotion = constraint.zMotion = ConfigurableJointMotion.Limited;
            constraint.angularXMotion = constraint.angularYMotion = constraint.angularZMotion = ConfigurableJointMotion.Free;
            constraint.enableCollision = true;
            constraint.projectionMode = JointProjectionMode.PositionAndRotation;
            constraint.projectionDistance = .003f;
            constraint.projectionAngle = 180f;
            firstBody.solverIterations = Mathf.Max(firstBody.solverIterations, 12);
            secondBody.solverIterations = Mathf.Max(secondBody.solverIterations, 12);
            if (firstPiece) firstPiece.AttachCord(this, constraint);
            if (secondPiece) secondPiece.AttachCord(this, null);
        }

        internal static void RemoveConstraint(ConfigurableJoint joint)
        {
            joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Free;
            joint.projectionMode = JointProjectionMode.None;
            joint.connectedBody = null;
            Destroy(joint);
        }

        private void ClearConstraint()
        {
            if (reactingPiece && rock > 0f) reactingPiece.SetLoadRock(pullDirection, 0f);
            ReleaseYaw();
            reactingPiece = null;
            rock = 0f;
            loadedThisStep = false;
            trackedFollower = null;
            trackedThisStep = false;
            catchUpDirection = Vector3.zero;
            stalledTime = 0f;
            if (firstPiece) firstPiece.DetachCord(this);
            if (secondPiece) secondPiece.DetachCord(this);
            if (constraint) RemoveConstraint(constraint);
            constraint = null;
            nextFirstEye = nextSecondEye = null;
            blockedTime = 0f;
            tensionHeard = false;
            nextSlideSound = 0f;
        }

        // Kinematic hand and landing poses have infinite mass in PhysX. Admit only
        // the part of the requested move that a contact-safe motion of the follower can support.
        internal bool TryMove(WorkshopPiece lead, ref Vector3 position, ref Quaternion rotation)
        {
            bool first = lead == firstPiece;
            var follower = first ? secondBody : firstBody;
            var otherPiece = first ? secondPiece : firstPiece;
            if (!follower) return true;
            Vector3 offset = first ? firstLocalOffset : secondLocalOffset;
            Vector3 otherEye = (first ? nextSecondEye : nextFirstEye) ??
                Eye(follower, first ? secondLocalOffset : firstLocalOffset);
            Vector3 delta = position + rotation * offset - otherEye;
            Vector3 tow = Vector3.zero;
            bool resisting = false;
            bool advancingRow = false;
            bool catchingUp = false;
            catchUpDirection = Vector3.zero;
            if (delta.sqrMagnitude > restLength * restLength)
            {
                // Keep the casting on the table. A partly frozen body can only follow
                // along its available axes. Sweep the actual casting before applying traction.
                Vector3 axis = new Vector3(delta.x, 0f, delta.z);
                if ((follower.constraints & RigidbodyConstraints.FreezePositionX) != 0) axis.x = 0f;
                if ((follower.constraints & RigidbodyConstraints.FreezePositionZ) != 0) axis.z = 0f;
                axis.Normalize();
                float parallel = Vector3.Dot(delta, axis);
                float perpendicularSq = Mathf.Max(0f, delta.sqrMagnitude - parallel * parallel);
                float required = parallel - Mathf.Sqrt(Mathf.Max(0f, restLength * restLength - perpendicularSq));
                float travel = follower.isKinematic ? 0f : Mathf.Clamp(required, 0f,
                    Mathf.Min(follower.maxLinearVelocity, 4.5f) * Time.fixedDeltaTime);
                bool immobile = follower.isKinematic || axis == Vector3.zero;
                float clearance = float.PositiveInfinity;
                if (travel > 0f && SweepPastRobot(follower, axis, travel + .11f, otherPiece ? otherPiece.IgnoredRobot : null, out var hit)
                    && Vector3.Dot(axis, hit.normal) < -.15f)
                {
                    clearance = Mathf.Max(0f, hit.distance - .005f);
                    if (lead.IsHeld && clearance < .11f && IsMovableNeighbour(hit.rigidbody, axis))
                    {
                        float step = YieldSpeed * Time.fixedDeltaTime;
                        float space = clearance;
                        // Apply pressure only after the leading faces actually meet.
                        if (clearance < step)
                            space += PushContactRow(hit.rigidbody, follower, axis, step - clearance);
                        advancingRow = TrackFollower(follower, axis) && space > .001f;
                        clearance = space;
                    }
                }
                if (clearance < .11f)
                    travel = Mathf.Min(travel, YieldSpeed * Time.fixedDeltaTime, clearance);
                tow = axis * travel;
                // A turning hand describes a longer arc than a free casting can cover
                // in one tick. Keep towing at its actual speed while it is making progress.
                if (lead.IsHeld && travel > 0f && clearance >= .11f &&
                    required > travel + .003f && perpendicularSq <= restLength * restLength)
                {
                    catchingUp = TrackFollower(follower, axis);
                    if (catchingUp) catchUpDirection = axis;
                }
                bool loaded = lead.IsHeld && otherPiece && delta.magnitude > restLength + .008f &&
                    (immobile || clearance < .11f);
                resisting = loaded && !advancingRow;
                if (loaded)
                {
                    Vector3 direction = Vector3.ProjectOnPlane(delta, Vector3.up).normalized;
                    float targetRock = advancingRow ? 0f : immobile ? 1f : Mathf.Clamp01(1f - clearance / .11f);
                    LoadFollower(otherPiece, direction, targetRock);
                    otherEye = Eye(follower, first ? secondLocalOffset : firstLocalOffset);
                    delta = position + rotation * offset - otherEye;
                }
            }

            float unreachable = (delta - tow).magnitude - restLength;
            bool following = advancingRow || catchingUp;
            if (lead.IsHeld)
                blockedTime = !following && (resisting || unreachable > .012f) ? blockedTime + Time.fixedDeltaTime : 0f;
            bool release = following ? false : resisting ? blockedTime + .0001f >= GiveTime :
                (unreachable > GripTravel || (lead.IsHeld && blockedTime >= GripHoldTime));
            if (release || (!lead.IsHeld && unreachable > .012f))
            {
                if (lead.IsHeld) WorkshopAudio.PlaySlip(lead.Body.position);
                tensionHeard = false;
                blockedTime = 0f;
                return false;
            }

            // A sweep can miss a face already touching the follower. A requested tow
            // is an impulse, not proof that the follower has cleared that face. Grant
            // hand travel only from motion already under way, and none in advance
            // while the follower is braced. Recheck its actual eye next physics tick.
            Vector3 otherNext = otherEye;
            float towStep = tow.magnitude;
            if (towStep > 0f && (!reactingPiece || reactingPiece != otherPiece || advancingRow))
            {
                Vector3 axis = tow / towStep;
                float advance = Vector3.Dot(follower.GetPointVelocity(otherEye), axis) * Time.fixedDeltaTime;
                otherNext += axis * Mathf.Clamp(advance, 0f, towStep);
            }
            if ((position + rotation * offset - otherNext).sqrMagnitude > restLength * restLength)
            {
                // Truncate the original move, including its yaw, along the same path.
                float low = 0f, high = 1f;
                var body = lead.Body;
                for (int i = 0; i < 10; i++)
                {
                    float t = (low + high) * .5f;
                    Vector3 eye = Vector3.Lerp(body.position, position, t) +
                        Quaternion.Slerp(body.rotation, rotation, t) * offset;
                    if ((eye - otherNext).sqrMagnitude <= restLength * restLength) low = t;
                    else high = t;
                }
                position = Vector3.Lerp(body.position, position, low);
                rotation = Quaternion.Slerp(body.rotation, rotation, low);
                // Rocking a contact eye can put even the previous hand pose just
                // beyond reach. Let the held part give back that last few millimetres.
                Vector3 remaining = position + rotation * offset - otherNext;
                float correction = remaining.magnitude - restLength;
                if (correction > .0001f)
                {
                    Vector3 back = -remaining.normalized;
                    if (SweepPastRobot(body, back, correction + .002f, lead.IgnoredRobot, out var contact))
                        correction = Mathf.Clamp(contact.distance - .002f, 0f, correction);
                    position += back * correction;
                }
            }

            if (lead.IsHeld) UpdatePullSounds(lead, follower, delta.magnitude, tow);
            if (tow.sqrMagnitude > .00000001f)
            {
                Vector3 axis = tow.normalized;
                float speed = tow.magnitude / Time.fixedDeltaTime;
                float increase = Mathf.Max(0f, speed - Vector3.Dot(follower.linearVelocity, axis));
                follower.AddForce(axis * increase, ForceMode.VelocityChange);
            }
            if (first) nextFirstEye = position + rotation * offset;
            else nextSecondEye = position + rotation * offset;
            return true;
        }

        private void UpdatePullSounds(WorkshopPiece lead, Rigidbody follower, float span, Vector3 tow)
        {
            // Re-arm only after visible slack, so a vibrating eyelet cannot creak every tick.
            if (span < restLength - .08f) tensionHeard = false;
            if (!tensionHeard && span > restLength + .002f)
            {
                tensionHeard = true;
                WorkshopAudio.PlayTension(lead.Body.position);
            }

            if (tow.sqrMagnitude < .00000001f || Time.fixedTime < nextSlideSound) return;
            // Use the follower's solved motion. A casting pushed against a wall stays quiet.
            float slideSpeed = Vector3.Dot(follower.linearVelocity, tow.normalized);
            if (slideSpeed < .15f) return;
            WorkshopAudio.PlaySlide(follower.position, Mathf.Clamp01(slideSpeed / 3f));
            nextSlideSound = Time.fixedTime + .38f;
        }

        private static bool SweepPastRobot(Rigidbody body, Vector3 direction, float distance, Collider robot, out RaycastHit hit)
        {
            if (!body.SweepTest(direction, out hit, distance, QueryTriggerInteraction.Ignore)) return false;
            if (hit.collider != robot && Vector3.Dot(hit.normal, direction) < -.15f) return true;
            // The chassis or a tangential floor contact can mask a wall. Find the
            // nearest face actually opposing this move.
            float nearest = float.PositiveInfinity;
            foreach (var candidate in body.SweepTestAll(direction, distance, QueryTriggerInteraction.Ignore))
            {
                if (candidate.collider == robot || candidate.distance >= nearest ||
                    Vector3.Dot(candidate.normal, direction) >= -.15f) continue;
                nearest = candidate.distance;
                hit = candidate;
            }
            return !float.IsPositiveInfinity(nearest);
        }

        private static bool IsMovableNeighbour(Rigidbody body, Vector3 axis)
        {
            if (!body || body.isKinematic || !body.TryGetComponent<WorkshopPiece>(out var piece) ||
                !piece.CanPickUp) return false;
            return (Mathf.Abs(axis.x) < .01f || (body.constraints & RigidbodyConstraints.FreezePositionX) == 0) &&
                   (Mathf.Abs(axis.z) < .01f || (body.constraints & RigidbodyConstraints.FreezePositionZ) == 0);
        }

        private bool TrackFollower(Rigidbody follower, Vector3 axis)
        {
            if (trackedFollower == follower &&
                Vector3.Dot(follower.position - lastFollowerPosition, axis) < .025f * Time.fixedDeltaTime)
                stalledTime += Time.fixedDeltaTime;
            else stalledTime = 0f;
            trackedFollower = follower;
            lastFollowerPosition = follower.position;
            trackedThisStep = true;
            return stalledTime < .10f;
        }

        private float PushContactRow(Rigidbody neighbour, Rigidbody follower, Vector3 axis, float step)
        {
            // Look along the short row of contacting faces. Only a clear gap at the
            // front lets the follower advance. Wall contact never grants extra rope.
            int count = 0;
            float room = 0f;
            while (count < contactRow.Length)
            {
                contactRow[count++] = neighbour;
                Rigidbody next = null;
                float nearest = float.PositiveInfinity;
                foreach (var hit in neighbour.SweepTestAll(axis, step + .006f, QueryTriggerInteraction.Ignore))
                {
                    if (hit.distance >= nearest || Vector3.Dot(axis, hit.normal) >= -.15f ||
                        (hit.rigidbody && (hit.rigidbody == follower || Array.IndexOf(contactRow, hit.rigidbody, 0, count) >= 0))) continue;
                    nearest = hit.distance;
                    next = hit.rigidbody;
                }
                room += Mathf.Max(0f, nearest - .005f);
                if (room >= step || !IsMovableNeighbour(next, axis)) break;
                neighbour = next;
            }
            float advance = Mathf.Min(room, step);
            for (int i = 0; i < count && advance > 0f; i++)
            {
                var body = contactRow[i];
                float increase = advance / Time.fixedDeltaTime - Vector3.Dot(body.linearVelocity, axis);
                body.AddForce(axis * Mathf.Clamp(increase, 0f, 8f * Time.fixedDeltaTime), ForceMode.VelocityChange);
            }
            return advance;
        }

        private void LoadFollower(WorkshopPiece piece, Vector3 direction, float targetRock)
        {
            if (reactingPiece && reactingPiece != piece) FinishReaction();
            reactingPiece = piece;
            loadedThisStep = true;
            if (direction.sqrMagnitude > .0001f) pullDirection = direction;
            if (targetRock > .01f && !piece.Body.isKinematic &&
                (piece.Body.constraints & RigidbodyConstraints.FreezeRotationY) == 0)
            {
                // An offset eye must not swivel a long interlocking nose into a wall.
                // Both the root's translation and its neighbours remain contact-solved.
                piece.Body.angularVelocity = Vector3.zero;
                piece.Body.constraints |= RigidbodyConstraints.FreezeRotationY;
                braceYaw = true;
            }
            SetRock(Mathf.MoveTowards(rock, targetRock, Time.fixedDeltaTime / .15f));
        }

        private void SetRock(float value)
        {
            if (value == rock) return;
            rock = value;
            reactingPiece.SetLoadRock(pullDirection, rock);
            if (reactingPiece == firstPiece)
            {
                firstLocalOffset = LocalOffset(firstBody, firstAnchor);
                if (constraint) constraint.anchor = firstBody.transform.InverseTransformPoint(firstAnchor.position);
            }
            else
            {
                secondLocalOffset = LocalOffset(secondBody, secondAnchor);
                if (constraint) constraint.connectedAnchor = secondBody.transform.InverseTransformPoint(secondAnchor.position);
            }
        }

        private void FinishReaction()
        {
            SetRock(0f);
            ReleaseYaw();
            reactingPiece = null;
        }

        private void ReleaseYaw()
        {
            if (braceYaw && reactingPiece) reactingPiece.Body.constraints &= ~RigidbodyConstraints.FreezeRotationY;
            braceYaw = false;
        }

        internal Vector3 LimitGripStep(WorkshopPiece lead, Vector3 handOffset, Vector3 step)
        {
            Vector3 axis = reactingPiece && reactingPiece != lead ? pullDirection : catchUpDirection;
            if (axis == Vector3.zero) return step;
            // The robot braces for a moment. It can still step sideways or back out
            // of the pull without letting its arms move far beyond the side grips.
            float outward = Vector3.Dot(step, axis);
            float reach = Mathf.Max(0f, GripTravel - Vector3.Dot(handOffset, axis));
            return step - axis * Mathf.Max(0f, outward - reach);
        }

        private void FixedUpdate()
        {
            // All kinematic requests have now been submitted for this simulation step.
            nextFirstEye = nextSecondEye = null;
            if (!loadedThisStep && reactingPiece)
            {
                SetRock(Mathf.MoveTowards(rock, 0f, Time.fixedDeltaTime / .20f));
                if (rock == 0f) FinishReaction();
            }
            if (!trackedThisStep)
            {
                trackedFollower = null;
                catchUpDirection = Vector3.zero;
            }
            trackedThisStep = false;
            loadedThisStep = false;
            LimitFreeSeparation();
        }

        private void LimitFreeSeparation()
        {
            if (!constraint || firstBody.isKinematic || secondBody.isKinematic ||
                (firstBody.IsSleeping() && secondBody.IsSleeping())) return;
            var a = Eye(firstBody, firstLocalOffset);
            var b = Eye(secondBody, secondLocalOffset);
            float dt = Time.fixedDeltaTime;
            // Controller pushes are queued as forces earlier in this same fixed step.
            // Predict both eye velocities (including yaw) before PhysX consumes them.
            var travel = (FreeEyeVelocity(secondBody, b, dt) - FreeEyeVelocity(firstBody, a, dt)) * dt;
            var predicted = b - a + travel;
            float length = predicted.magnitude;
            float excess = length - (restLength - .003f);
            if (excess <= 0f) return;
            var radial = predicted / length;
            var aResponse = TranslationResponse(firstBody, radial);
            var bResponse = TranslationResponse(secondBody, radial);
            float response = Vector3.Dot(aResponse + bResponse, radial);
            if (response < .0001f) return;
            // Cancel only the separating motion that would exceed the stop; retain
            // tangential travel and let the joint and contacts solve the remaining load.
            float impulse = excess / (response * dt);
            firstBody.AddForce(aResponse * impulse, ForceMode.VelocityChange);
            secondBody.AddForce(-bResponse * impulse, ForceMode.VelocityChange);
        }

        private static Vector3 FreeEyeVelocity(Rigidbody body, Vector3 eye, float dt) =>
            body.GetPointVelocity(eye) + body.GetAccumulatedForce(dt) * (dt / body.mass);

        private static Vector3 TranslationResponse(Rigidbody body, Vector3 direction)
        {
            var constraints = body.constraints;
            if ((constraints & RigidbodyConstraints.FreezePositionX) != 0) direction.x = 0f;
            if ((constraints & RigidbodyConstraints.FreezePositionY) != 0) direction.y = 0f;
            if ((constraints & RigidbodyConstraints.FreezePositionZ) != 0) direction.z = 0f;
            return direction / body.mass;
        }

        private void LateUpdate() => DrawCord();

        private void DrawCord()
        {
            if (!firstAnchor || !secondAnchor)
            {
                cord.enabled = false;
                hasDrawn = false;
                return;
            }

            // A flat bow can traverse almost twice its mean segment spacing at its ends.
            // Sample even a long cord more finely than a single 0.46 m casting cell.
            int required = Mathf.Clamp(Mathf.CeilToInt(restLength / SampleSpacing), 28, 512);
            if (segments != required)
            {
                segments = required;
                points = new Vector3[segments + 1];
                runs = new float[segments + 1];
                supports = new int[segments + 1];
                tightHeights = new float[segments + 1];
                hasDrawn = false;
            }

            Vector3 start = firstAnchor.position;
            Vector3 end = secondAnchor.position;
            bool cacheSurfaces = surfaceMotion != null && Application.IsPlaying(gameObject);
            if (cacheSurfaces && hasDrawn && !surfaceMotion.HasChange(drawnArea) &&
                (start - drawnStart).sqrMagnitude < .000004f && (end - drawnEnd).sqrMagnitude < .000004f) return;
            cord.enabled = true;
            Vector3 chord = end - start;
            Vector3 sideways = Vector3.Cross(chord, Vector3.up);
            if (sideways.sqrMagnitude > .0001f)
            {
                sideways.Normalize();
                bendDirection = Vector3.Dot(sideways, bendDirection) < 0f ? -sideways : sideways;
            }
            else if (bendDirection == Vector3.zero) bendDirection = Vector3.right;

            // Excess material lies in a sideways bow on the deck. First estimate the
            // bow, then fit it to the fixed length using the sampled surface heights.
            float slackBend = .46f * Mathf.Sqrt(Mathf.Max(0f, restLength * restLength - chord.sqrMagnitude));
            var deck = surfaceMotion?.Deck ?? default;
            if (deck.width > 0f)
            {
                // A held eye can be above the rim. Preserve the real attachments at either end.
                deck = Rect.MinMaxRect(Mathf.Min(deck.xMin, Mathf.Min(start.x, end.x)), Mathf.Min(deck.yMin, Mathf.Min(start.z, end.z)),
                    Mathf.Max(deck.xMax, Mathf.Max(start.x, end.x)), Mathf.Max(deck.yMax, Mathf.Max(start.z, end.z)));
                if (!hasDrawn)
                {
                    Vector3 middle = (start + end) * .5f;
                    Vector3 bow = bendDirection * slackBend;
                    if (RimOverflow(middle - bow, deck) < RimOverflow(middle + bow, deck)) bendDirection = -bendDirection;
                }
            }
            float floorHeight = CordWidth * 0.5f;
            float drop = Mathf.Clamp((start.y + end.y) * 0.5f - floorHeight, 0f, slackBend);
            float sideBend = slackBend;
            float step = Vector3.ProjectOnPlane(chord, Vector3.up).magnitude / segments;
            float lowerBow = 0f, upperBow = restLength * .5f + (deck.width > 0f ? SampleSpacing : 0f);
            // Moving the bow across a lid changes its supports. Bracket subsequent
            // fits so the path converges even as it slips across a tabletop edge.
            for (int pass = 0; pass < 5; pass++)
            {
                bool folded = LayOnSurfaces(start, end, bendDirection * sideBend - Vector3.up * drop, floorHeight, deck);
                float length = FitSupportedPath();
                if (slackBend == 0f || Mathf.Abs(length - restLength) <= .012f) break;
                if (length < restLength) lowerBow = sideBend;
                else upperBow = sideBend;
                float low = lowerBow, high = upperBow;
                for (int j = 0; j < 10; j++)
                {
                    float bow = (low + high) * .5f;
                    float predicted = 0f;
                    Vector3 previous = start;
                    for (int i = 1; i <= segments; i++)
                    {
                        float dy = points[i].y - points[i - 1].y;
                        if (folded)
                        {
                            float t = (float)i / segments;
                            Vector3 next = start + chord * t + bendDirection * (bow * 4f * t * (1f - t));
                            TurnAtRim(ref next, deck);
                            float dx = next.x - previous.x, dz = next.z - previous.z;
                            predicted += Mathf.Sqrt(dx * dx + dz * dz + dy * dy);
                            previous = next;
                        }
                        else
                        {
                            float across = bow * (4f / segments) * (1f - (2f * i - 1f) / segments);
                            predicted += Mathf.Sqrt(step * step + across * across + dy * dy);
                        }
                    }
                    if (predicted < restLength) low = bow;
                    else high = bow;
                }
                float fitted = (low + high) * .5f;
                sideBend = Mathf.Abs(fitted - sideBend) > .005f ? fitted : (lowerBow + upperBow) * .5f;
            }
            if (cord.positionCount != points.Length)
                cord.positionCount = points.Length;
            cord.SetPositions(points);
            if (cacheSurfaces)
            {
                drawnStart = start;
                drawnEnd = end;
                hasDrawn = true;
                // Include the whole bend, including any return run beside the rim.
                Vector3 min = points[0], max = points[0];
                for (int i = 1; i < points.Length; i++)
                {
                    min = Vector3.Min(min, points[i]);
                    max = Vector3.Max(max, points[i]);
                }
                float padding = CordWidth * .55f;
                drawnArea = Rect.MinMaxRect(min.x - padding, min.z - padding, max.x + padding, max.z + padding);
            }
        }

        private static float RimOverflow(Vector3 p, Rect deck)
        {
            float x = p.x - Mathf.Clamp(p.x, deck.xMin, deck.xMax);
            float z = p.z - Mathf.Clamp(p.z, deck.yMin, deck.yMax);
            return x * x + z * z;
        }

        // Turn surplus material back into the tray when it reaches a raised edge.
        // Mirroring preserves the distance travelled on either side of the turn.
        private static bool TurnAtRim(ref Vector3 p, Rect deck)
        {
            bool outsideX = p.x < deck.xMin || p.x > deck.xMax;
            bool outsideZ = p.z < deck.yMin || p.z > deck.yMax;
            if (outsideX) p.x = deck.xMin + Mathf.PingPong(p.x - deck.xMin, deck.width);
            if (outsideZ) p.z = deck.yMin + Mathf.PingPong(p.z - deck.yMin, deck.height);
            return outsideX || outsideZ;
        }

        private bool LayOnSurfaces(Vector3 start, Vector3 end, Vector3 bend, float floorHeight, Rect deck)
        {
            bool folded = false;
            float minimumHeight = Mathf.Min(floorHeight, Mathf.Min(start.y, end.y));
            var physics = gameObject.scene.GetPhysicsScene();
            float rayHeight = Mathf.Max(start.y, end.y) + 1.5f;
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                Vector3 point = Vector3.Lerp(start, end, t) + bend * (4f * t * (1f - t));
                if (deck.width > 0f) folded |= TurnAtRim(ref point, deck);
                point.y = Mathf.Max(minimumHeight, point.y);
                if (i > 0 && i < segments)
                {
                    Vector3 origin = new Vector3(point.x, rayHeight, point.z);
                    // The cord bridges a narrow seam between closely packed lids.
                    int hits = physics.SphereCast(origin, CordWidth * .55f, Vector3.down, supportHits, rayHeight - minimumHeight + 0.5f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    for (int h = 0; h < hits; h++)
                    {
                        var hit = supportHits[h];
                        if (hit.collider is CharacterController) continue;
                        float clearance = floorHeight + (hit.rigidbody ? CoverClearance : 0f);
                        if (hit.rigidbody == firstBody || hit.rigidbody == secondBody)
                        {
                            Vector3 eye = hit.rigidbody == firstBody ? start : end;
                            float fromEye = Vector3.ProjectOnPlane(point - eye, Vector3.up).magnitude;
                            clearance = Mathf.Lerp(.016f, clearance, fromEye / .30f);
                        }
                        point.y = Mathf.Max(point.y, hit.point.y + clearance);
                    }
                }
                points[i] = point;
            }

            // A cord bridging a seam or sliding off a lid bends over a short horizontal run.
            // Propagate each raised support onto its neighbours while leaving both eyelets fixed.
            for (int i = 1; i < segments; i++) SupportFrom(i, i - 1);
            for (int i = segments - 1; i > 0; i--) SupportFrom(i, i + 1);
            return folded;
        }

        private float FitSupportedPath()
        {
            float length = 0f;
            for (int i = 1; i <= segments; i++)
            {
                var delta = points[i] - points[i - 1];
                length += delta.magnitude;
                runs[i] = runs[i - 1] + Mathf.Sqrt(delta.x * delta.x + delta.z * delta.z);
            }
            if (length <= restLength + .003f) return length;

            // The shortest route over a row of supports is a set of straight spans
            // across their upper hull. Keep both eyelets and remove only excess drape.
            int count = 0;
            for (int i = 0; i <= segments; i++)
            {
                while (count >= 2)
                {
                    int a = supports[count - 2], b = supports[count - 1];
                    float previousRise = (points[b].y - points[a].y) * (runs[i] - runs[b]);
                    float nextRise = (points[i].y - points[b].y) * (runs[b] - runs[a]);
                    if (previousRise > nextRise) break;
                    count--;
                }
                supports[count++] = i;
            }
            for (int k = 1; k < count; k++)
            {
                int a = supports[k - 1], b = supports[k];
                for (int i = a; i <= b; i++)
                {
                    float t = Mathf.InverseLerp(runs[a], runs[b], runs[i]);
                    tightHeights[i] = Mathf.Lerp(points[a].y, points[b].y, t);
                }
            }
            float low = 0f, high = 1f;
            for (int j = 0; j < 9; j++)
            {
                float tension = (low + high) * .5f;
                float path = 0f;
                for (int i = 1; i <= segments; i++)
                {
                    float dy = Mathf.Lerp(points[i].y - points[i - 1].y, tightHeights[i] - tightHeights[i - 1], tension);
                    float dx = runs[i] - runs[i - 1];
                    path += Mathf.Sqrt(dx * dx + dy * dy);
                }
                if (path > restLength) low = tension;
                else high = tension;
            }
            for (int i = 1; i < segments; i++)
                points[i].y = Mathf.Lerp(points[i].y, tightHeights[i], high);
            length = 0f;
            for (int i = 1; i <= segments; i++) length += Vector3.Distance(points[i - 1], points[i]);
            return length;
        }

        private void SupportFrom(int index, int neighbour)
        {
            var span = points[index] - points[neighbour];
            float run = Mathf.Sqrt(span.x * span.x + span.z * span.z);
            points[index].y = Mathf.Max(points[index].y, points[neighbour].y - run);
        }

        private void OnDisable()
        {
            ClearConstraint();
            if (cord) cord.enabled = false;
        }
    }
}
