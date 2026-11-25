using UnityEngine;

namespace Monlit
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class WorkshopPiece : MonoBehaviour
    {
        private enum State { Free, Carried, Lowering, Seating, Nailed, Magnetized }

        internal const float CarryCaptureHeight = 0.75f;
        private const float SeatDuration = 0.28f;
        private const float LowerDuration = 0.16f;
        private const float LiftDuration = 0.16f;
        private const float PushAcceleration = 14f;
        private const float MaxFreeSpeed = 4.5f;
        private const float MaxFreeSpin = 2.6f;

        [SerializeField] private Transform target;
        [SerializeField] private Transform alternateTarget;
        [SerializeField] private string pickupPrompt = "Взять деталь";
        [SerializeField, Min(1)] private int matchingOrientations = 1;
        [SerializeField, HideInInspector] private WorkshopRope tether;
        [SerializeField, HideInInspector] private ConfigurableJoint tetherJoint;
        [SerializeField, HideInInspector] private Vector2[] footprint;

        private State state;
        private Rigidbody body;
        private Collider[] shapes;
        private Transform hand;
        private Collider carrier;
        private Collider ignoredRobot;
        private Quaternion gripRotation;
        private float pendingHandTurn;
        private bool turningInHands;
        private bool grippingFromBack;
        private Vector3 liftStart;
        private Quaternion liftRotation;
        private float liftTime;
        private bool lowGrip;
        private Vector3 gripOffset;
        private float lowGripHeight;
        private Vector3 pushVelocity;
        private float pushTime;
        private Vector3 placementStart;
        private Quaternion placementRotation;
        private Quaternion seatingRotation;
        private Vector3 setDownPoint;
        private Vector3 releaseVelocity;
        private float placementTime;
        private WorkshopFigure landingFigure;
        private System.Action releaseHands;
        private Transform castingArt;
        private Transform castingEye;
        private Vector3 unstrainedEye;
        private Bounds castingBounds;
        private WorkshopFigure[] placementFigures;
        private float driftStartedAt = -1f;
        private Vector3 driftDirection;

        public Rigidbody Body => body ? body : body = GetComponent<Rigidbody>();
        public Transform Target => target;
        internal bool IsMagnetized => state == State.Magnetized;
        public float DeckClearance { get; private set; } = 1.05f;
        public string PickupPrompt => IsMagnetized ? "Взять с магнита" : pickupPrompt;
        public bool IsHeld => state == State.Carried;
        public bool CanPickUp => state == State.Free || IsMagnetized;
        public bool RequiresNail { get; private set; }
        public bool IsNailed => state == State.Nailed;
        public bool CanNail => TryFindNailPosition(out _);
        internal Vector2[] Footprint => footprint;
        internal Collider[] CollisionShapes => shapes;
        internal bool IsResting => IsNailed || IsMagnetized || (!RequiresNail && state == State.Free && Body.linearVelocity.sqrMagnitude < 0.12f &&
            Mathf.Abs(Body.angularVelocity.y) < 0.45f);
        public Transform LeftGrip { get; private set; }
        public Transform RightGrip { get; private set; }
        public bool CanSeatHeld => IsHeld && FindMagnet(out _, out _);
        public bool IsDelivered => IsNailed || (IsResting && FitsFigure());

        public void Configure(Transform targetSeat)
        {
            placementFigures = null;
            landingFigure = null;
            RequiresNail = false;
            driftStartedAt = -1f;
            if (IsNailed || IsMagnetized) state = State.Free;
            target = targetSeat;
            ConfigureAlternate(null, null);
            body = GetComponent<Rigidbody>();
            body.mass = 1f;
            body.useGravity = true;
            body.isKinematic = false;
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            SetContactLimits();
            CacheShapes();
        }

        public void Configure(Transform targetSeat, int orientations, string prompt)
        {
            Configure(targetSeat);
            matchingOrientations = Mathf.Max(1, orientations);
            pickupPrompt = prompt;
        }

        public void ConfigureAlternate(Transform alternative, WorkshopPiece peer)
        {
            alternateTarget = alternative;
        }

        internal void SetPlacementFigures(WorkshopFigure[] figures, bool requiresNail)
        {
            placementFigures = figures;
            RequiresNail = requiresNail;
        }

        private bool FitsFigure()
        {
            if (placementFigures == null)
                return FitsSeat(target, .08f, 3f, .09f) || FitsSeat(alternateTarget, .08f, 3f, .09f);
            foreach (var figure in placementFigures)
                if (figure && figure.ContainsRestingFootprint(this)) return true;
            return false;
        }

        private WorkshopFigure FindMagnet(out Vector3 position, out Quaternion rotation)
        {
            if (placementFigures != null)
                foreach (var figure in placementFigures)
                {
                    if (!figure) continue;
                    if (figure.TryGetEdgePose(this, out position, out rotation) && CanReach(position, rotation)) return figure;
                    if (!RequiresNail && figure.TryGetInteriorPose(this, out position, out rotation) &&
                        CanReach(position, rotation)) return figure;
                }
            position = Body.position;
            rotation = Body.rotation;
            return null;
        }

        private bool CanReach(Vector3 position, Quaternion rotation) =>
            !tether || tether.CanReachAt(this, position, rotation);

        private bool TryFindNailPosition(out Vector3 position)
        {
            position = default;
            if (!RequiresNail || !(IsHeld || (CanPickUp && driftStartedAt >= 0f))) return false;
            foreach (var board in placementFigures)
            {
                if (board && board.TryGetNailPose(this, out position) && CanReach(position, Body.rotation))
                    return true;
            }
            return false;
        }

        public bool TryNail()
        {
            if (!TryFindNailPosition(out Vector3 position)) return false;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }
            if (castingArt && castingEye) SetLoadRock(Vector3.forward, 0f);
            body.position = position;
            state = State.Nailed;
            pushTime = 0f;
            ClearHands();
            return true;
        }

        public bool TryRemoveNail()
        {
            if (!IsNailed) return false;
            ReleaseBody(Vector3.zero);
            return true;
        }

        internal void SetFootprint(Vector2Int[] cells, Vector2 pivot)
        {
            footprint = new Vector2[cells.Length];
            for (int i = 0; i < cells.Length; i++) footprint[i] = (Vector2)cells[i] - pivot;
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            // A single casting may be cloned separately from its puzzle and cord.
            if (tether && !tether.ContainsBody(body))
            {
                if (tetherJoint) WorkshopRope.RemoveConstraint(tetherJoint);
                tether = null;
                tetherJoint = null;
            }
            SetContactLimits();
            CacheShapes();
        }

        private void SetContactLimits()
        {
            body.linearDamping = 2.6f;
            body.angularDamping = 5f;
            // Tabletop travel per simulation step is well below the width of a cell.
            // Discrete contacts follow the real faces of the interlocking cutouts.
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.maxLinearVelocity = MaxFreeSpeed;
            body.maxAngularVelocity = MaxFreeSpin;
            body.maxDepenetrationVelocity = 1.2f;
        }

        private void CacheShapes()
        {
            shapes = GetComponentsInChildren<Collider>(true);
            float radiusSquared = 0f;
            foreach (var shape in shapes)
            {
                if (shape.isTrigger) continue;
                Bounds bounds;
                if (shape is BoxCollider box) bounds = new Bounds(box.center, box.size);
                else if (shape is MeshCollider mesh && mesh.sharedMesh) bounds = mesh.sharedMesh.bounds;
                else continue;
                for (int x = -1; x <= 1; x += 2)
                    for (int z = -1; z <= 1; z += 2)
                    {
                        var corner = bounds.center + new Vector3(x * bounds.extents.x, 0f, z * bounds.extents.z);
                        var offset = shape.transform.TransformPoint(corner) - transform.position;
                        radiusSquared = Mathf.Max(radiusSquared, offset.x * offset.x + offset.z * offset.z);
                    }
            }
            if (radiusSquared > 0f) DeckClearance = Mathf.Sqrt(radiusSquared) + 0.04f;
        }

        internal void RequestPush(Vector3 desiredVelocity, float duration)
        {
            if (!CanPickUp || Body.isKinematic) return;
            pushVelocity = desiredVelocity;
            pushTime = Mathf.Clamp(duration, Time.fixedDeltaTime, 0.12f);
        }

        internal void AttachCord(WorkshopRope rope, ConfigurableJoint joint)
        {
            tether = rope;
            tetherJoint = joint;
        }

        internal void DetachCord(WorkshopRope rope)
        {
            if (tether != rope) return;
            tether = null;
            tetherJoint = null;
        }

        internal void SetLoadRock(Vector3 direction, float fraction)
        {
            if (IsNailed || IsMagnetized) return;
            if (!castingArt)
            {
                castingArt = transform.Find("Casting");
                castingEye = transform.Find("Rope Anchor");
                if (!castingArt || !castingEye || !castingArt.TryGetComponent<MeshFilter>(out var mesh)) return;
                unstrainedEye = castingArt.InverseTransformPoint(castingEye.position);
                castingBounds = mesh.sharedMesh.bounds;
            }
            if (!castingEye) return;

            // Tip the nose UP against the obstruction about the far edge on the deck.
            // Every point moves up or away from the blocking face, including the painted ring.
            var local = Vector3.ProjectOnPlane(transform.InverseTransformDirection(direction), Vector3.up).normalized;
            var extents = castingBounds.extents;
            float radius = Mathf.Abs(local.x) * extents.x + Mathf.Abs(local.z) * extents.z;
            var pivot = castingBounds.center - local * radius;
            pivot.y = 0f;
            float angle = Mathf.Min(7f, Mathf.Atan2(.105f, 2f * radius) * Mathf.Rad2Deg);
            var tilt = Quaternion.AngleAxis(-angle * fraction, Vector3.Cross(Vector3.up, local));
            var shift = pivot - tilt * pivot;
            castingArt.SetLocalPositionAndRotation(shift, tilt);
            castingEye.localPosition = shift + tilt * unstrainedEye;
        }

        internal void TurnInHands(float degrees) => pendingHandTurn += degrees;

        private void SelectGripSide(bool fromBack)
        {
            grippingFromBack = fromBack;
            LeftGrip = transform.Find(fromBack ? "Back Grip Left" : "Grip Left");
            RightGrip = transform.Find(fromBack ? "Back Grip Right" : "Grip Right");
        }

        internal Vector3 GripOffset => gripOffset;
        internal Collider IgnoredRobot => ignoredRobot;

        internal Vector3 LimitLoadedStep(Vector3 step) =>
            tether && IsHeld ? tether.LimitGripStep(this, hand.position + hand.rotation * gripOffset - Body.position, step) : step;

        internal Quaternion LimitLoadedTurn(Quaternion requested)
        {
            if (!IsHeld || !tether || !tether.IsTaut) return requested;
            // Let the chassis turn with the casting while the follower rounds the bend.
            // Six degrees of lead keeps the side grips within roughly a palm of the hands.
            var facingWithPiece = Body.rotation * Quaternion.Inverse(gripRotation);
            float currentLead = Quaternion.Angle(facingWithPiece, carrier.transform.rotation);
            return Quaternion.RotateTowards(facingWithPiece, requested, Mathf.Max(6f, currentLead));
        }

        public void PickUp(Transform carryingPoint, Collider robot)
        {
            if (!CanPickUp) return;
            hand = carryingPoint;
            carrier = robot;
            bool fromBack = Vector3.Dot(carrier.transform.position - body.position, body.rotation * Vector3.forward) > 0f;
            SelectGripSide(fromBack);
            gripRotation = fromBack ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
            // Initially keep the casting at its tabletop yaw. Subsequent turns rotate both as one.
            carrier.transform.rotation = body.rotation * Quaternion.Inverse(gripRotation);
            liftStart = body.position;
            liftRotation = body.rotation;
            liftTime = 0f;
            pendingHandTurn = 0f;
            lowGrip = false;
            gripOffset = Vector3.zero;
            if (tether)
            {
                var raised = new Vector3(liftStart.x, hand.position.y, liftStart.z);
                var lift = tether.FitPickupStep(this, liftStart, raised, liftRotation);
                var reach = tether.FitPickupStep(this, lift, hand.position, hand.rotation * gripRotation);
                if ((raised - lift).sqrMagnitude > .000001f || (hand.position - reach).sqrMagnitude > .000001f)
                {
                    lowGrip = true;
                    gripOffset = Quaternion.Inverse(hand.rotation) * Vector3.ProjectOnPlane(liftStart - hand.position, Vector3.up);
                    lowGripHeight = liftStart.y;
                }
            }
            pushTime = 0f;
            landingFigure = null;
            IgnoreRobot(robot);
            var follower = tether ? tether.OtherEnd(this) : null;
            if (follower && !follower.Body.isKinematic) follower.IgnoreRobot(robot);
            body.isKinematic = true;
            state = State.Carried;
            WorkshopAudio.Play(WorkshopAudio.Effect.Clamp, body.position);
        }

        public void SetDown(Vector3 walkingVelocity, float floorHeight, System.Action onHandsReleased = null)
        {
            if (IsMagnetized || (RequiresNail && !IsHeld)) return;
            releaseHands = onHandsReleased;
            releaseVelocity = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(walkingVelocity, Vector3.up) * 0.15f, 0.42f);
            landingFigure = IsHeld ? FindMagnet(out setDownPoint, out seatingRotation) : null;
            if (landingFigure)
            {
                BeginPlacement(State.Seating);
                return;
            }

            // Lower to just above the first solid surface below the part, including the raised beds.
            const float maxDescent = 0.72f;
            float descent = body.SweepTest(Vector3.down, out RaycastHit hit, maxDescent, QueryTriggerInteraction.Ignore)
                ? Mathf.Max(0f, hit.distance - 0.015f) : maxDescent;
            setDownPoint = body.position + Vector3.down * descent;
            setDownPoint.y = Mathf.Max(setDownPoint.y, Mathf.Min(body.position.y, floorHeight + 0.015f));
            BeginPlacement(State.Lowering);
        }

        private void BeginPlacement(State placement)
        {
            placementStart = body.position;
            placementRotation = body.rotation;
            placementTime = 0f;
            body.isKinematic = true;
            state = placement;
        }

        private void FixedUpdate()
        {
            if (state == State.Carried)
            {
                turningInHands = pendingHandTurn != 0f;
                if (turningInHands)
                {
                    gripRotation *= Quaternion.AngleAxis(pendingHandTurn, Vector3.up);
                    pendingHandTurn = 0f;
                }
                if (lowGrip && tether)
                {
                    CarryFromContact();
                    return;
                }
                liftTime = Mathf.Min(liftTime + Time.fixedDeltaTime, LiftDuration);
                // Raise a cutout clear of its neighbours before pulling it across the square.
                float t = liftTime / LiftDuration;
                float reach = Mathf.SmoothStep(0f, 1f, (t - .55f) / .45f);
                Vector3 pose = Vector3.Lerp(liftStart, hand.position, reach);
                pose.y = Mathf.Lerp(liftStart.y, hand.position.y, Mathf.SmoothStep(0f, 1f, t / .55f));
                MoveAttached(pose, Quaternion.Slerp(liftRotation, hand.rotation * gripRotation, reach));
                return;
            }

            if (state == State.Seating || state == State.Lowering)
            {
                AdvancePlacement();
                return;
            }

            if (ignoredRobot)
            {
                var partner = tether ? tether.OtherEnd(this) : null;
                bool towedByRobot = !body.isKinematic && partner && partner.carrier == ignoredRobot && partner.state != State.Free;
                if (!towedByRobot && !TouchesRobot()) RestoreRobotCollision();
            }

            if (IsNailed || IsMagnetized) return;
            if (pushTime > 0f)
            {
                // A compound can generate several controller hits. Apply one centre-of-mass push
                // per physics step so extra colliders and render frames cannot multiply the force.
                Vector3 direction = pushVelocity.normalized;
                float deficit = pushVelocity.magnitude - Vector3.Dot(body.linearVelocity, direction);
                float change = PushAcceleration * Mathf.Min(Time.fixedDeltaTime, pushTime);
                if (deficit > 0f)
                    body.AddForce(direction * Mathf.Min(change, deficit), ForceMode.VelocityChange);
                pushTime -= Time.fixedDeltaTime;
                return;
            }

            if (RequiresNail && driftStartedAt >= 0f) SlideLooseCasting();
        }

        private void SlideLooseCasting()
        {
            // Build up slowly after lowering. Contacts and the hard rope joint still resolve every move.
            float elapsed = Time.fixedTime - driftStartedAt;
            float ramp = Mathf.SmoothStep(0f, 1f, elapsed / .85f);
            Vector3 direction = Quaternion.Euler(0f, 16f * elapsed, 0f) * driftDirection;
            float speedGain = .65f * ramp - Vector3.Dot(body.linearVelocity, direction);
            if (speedGain > 0f)
                body.AddForce(direction * Mathf.Min(speedGain, 8f * Time.fixedDeltaTime), ForceMode.VelocityChange);
            float spinGain = .25f * ramp - body.angularVelocity.y;
            if (spinGain > 0f)
                body.AddTorque(Vector3.up * Mathf.Min(spinGain, 14f * Time.fixedDeltaTime), ForceMode.VelocityChange);
        }

        private void CarryFromContact()
        {
            var rotation = hand.rotation * gripRotation;
            var position = hand.position + hand.rotation * gripOffset;
            position.y = lowGripHeight;
            var raised = position;
            raised.y = Mathf.MoveTowards(position.y, hand.position.y, 5f * Time.fixedDeltaTime);
            position = tether.FitPickupStep(this, position, raised, rotation);
            lowGripHeight = position.y;
            // Keep the original side grips down on the table until the casting clears
            // its neighbours. Recenter in the hands only as the cord permits it.
            if (Mathf.Abs(position.y - hand.position.y) < .001f)
            {
                var centered = Vector3.MoveTowards(position, hand.position, 5f * Time.fixedDeltaTime);
                position = tether.FitPickupStep(this, position, centered, rotation);
                gripOffset = Quaternion.Inverse(hand.rotation) * Vector3.ProjectOnPlane(position - hand.position, Vector3.up);
                if (gripOffset.sqrMagnitude < .000001f)
                {
                    lowGrip = false;
                    gripOffset = Vector3.zero;
                    liftTime = LiftDuration;
                }
            }
            MoveAttached(position, rotation);
        }

        private void OnCollisionEnter() => LimitContactSpeed();
        private void OnCollisionStay() => LimitContactSpeed();

        private void LimitContactSpeed()
        {
            if (state != State.Free || !body || body.isKinematic) return;
            // A moving kinematic casting or the player capsule can impart an impulse in the
            // contact solver after Rigidbody's pre-step limits. Keep that response on the deck.
            Vector3 velocity = body.linearVelocity;
            Vector3 limited = Vector3.ClampMagnitude(velocity, MaxFreeSpeed);
            limited.y = Mathf.Min(limited.y, 0.7f);
            if ((velocity - limited).sqrMagnitude > 0.000001f) body.linearVelocity = limited;
            if (body.angularVelocity.sqrMagnitude > MaxFreeSpin * MaxFreeSpin)
                body.angularVelocity = Vector3.ClampMagnitude(body.angularVelocity, MaxFreeSpin);
        }

        private void AdvancePlacement()
        {
            bool seating = state == State.Seating;
            float duration = seating ? SeatDuration : LowerDuration;
            if (placementTime >= duration)
            {
                bool aligned = seating && landingFigure && (body.position - setDownPoint).sqrMagnitude < .015f * .015f &&
                    Quaternion.Angle(body.rotation, seatingRotation) < 1f &&
                    CanReach(body.position, seatingRotation) &&
                    landingFigure.IsClearOfCastings(this, body.position, seatingRotation);
                bool latch = !RequiresNail && aligned;
                if (latch)
                {
                    // A taut cord can leave a fraction of a degree after lowering. Finish the checked turn before locking.
                    body.rotation = seatingRotation;
                    if (castingArt && castingEye) SetLoadRock(Vector3.forward, 0f);
                    pushTime = 0f;
                    state = State.Magnetized;
                }
                else ReleaseBody(aligned ? Vector3.zero : releaseVelocity);
                landingFigure = null;
                WorkshopAudio.Play(latch ? WorkshopAudio.Effect.Seat : WorkshopAudio.Effect.Drop, body.position);
                ClearHands();
                return;
            }

            placementTime = Mathf.Min(placementTime + Time.fixedDeltaTime, duration);
            float t = placementTime / duration;
            if (!seating)
            {
                MoveAttached(Vector3.Lerp(placementStart, setDownPoint, Mathf.SmoothStep(0f, 1f, t)), body.rotation);
                return;
            }

            // Align above the adjoining castings before lowering into their shared seam.
            float align = Mathf.SmoothStep(0f, 1f, t / 0.58f);
            Vector3 pose = Vector3.Lerp(placementStart, setDownPoint, align);
            float lower = Mathf.SmoothStep(0f, 1f, (t - 0.58f) / 0.42f);
            pose.y = Mathf.Lerp(placementStart.y, setDownPoint.y, lower);
            MoveAttached(pose, Quaternion.Slerp(placementRotation, seatingRotation, align));
        }

        private void MoveAttached(Vector3 position, Quaternion rotation)
        {
            var requestedRotation = rotation;
            if (!tether || tether.TryMove(this, ref position, ref rotation))
            {
                if (IsHeld && turningInHands)
                {
                    // Keep only the hand turn the rope admitted, so slack cannot release a queued spin.
                    if (tether) gripRotation *= Quaternion.Inverse(requestedRotation) * rotation;
                    // Change contact pads as the far face comes around, keeping the wrists from crossing.
                    float side = Vector3.Dot(carrier.transform.position - position, rotation * Vector3.forward);
                    if ((grippingFromBack && side < -.1f) || (!grippingFromBack && side > .1f))
                        SelectGripSide(!grippingFromBack);
                }
                body.MovePosition(position);
                body.MoveRotation(rotation);
                return;
            }

            // Open the grip at its last reachable pose. Contact with the robot is
            // restored by FixedUpdate once the casting has fallen clear of its hands.
            if (carrier && carrier.TryGetComponent<WorkshopRobot>(out var robot)) robot.LoseGrip(this);
            landingFigure = null;
            ReleaseBody(Vector3.zero);
            ClearHands();
        }

        private void ReleaseBody(Vector3 velocity)
        {
            state = State.Free;
            body.isKinematic = false;
            body.linearVelocity = velocity;
            body.angularVelocity = Vector3.zero;
            if (RequiresNail)
            {
                driftStartedAt = Time.fixedTime;
                driftDirection = velocity.sqrMagnitude > .01f ? velocity.normalized : body.rotation * new Vector3(.8f, 0f, .6f);
                body.WakeUp();
            }
        }

        private void ClearHands()
        {
            hand = null;
            carrier = null;
            LeftGrip = null;
            RightGrip = null;
            var onReleased = releaseHands;
            releaseHands = null;
            onReleased?.Invoke();
        }

        private bool FitsSeat(Transform seat, float radius, float angle, float height)
        {
            if (!seat) return false;
            Vector3 offset = Body.position - seat.position;
            return offset.x * offset.x + offset.z * offset.z <= radius * radius && Mathf.Abs(offset.y) <= height
                && Quaternion.Angle(Body.rotation, ClosestSeatRotation(seat, Body.rotation)) <= angle;
        }

        private Quaternion ClosestSeatRotation(Transform seat, Quaternion rotation)
        {
            float step = 360f / Mathf.Max(1, matchingOrientations);
            float yaw = Mathf.DeltaAngle(seat.eulerAngles.y, rotation.eulerAngles.y);
            return seat.rotation * Quaternion.Euler(0f, Mathf.Round(yaw / step) * step, 0f);
        }

        private bool TouchesRobot()
        {
            foreach (var shape in shapes)
            {
                if (shape.bounds.Intersects(ignoredRobot.bounds)) return true;
            }
            return false;
        }

        private void IgnoreRobot(Collider robot)
        {
            if (ignoredRobot == robot) return;
            RestoreRobotCollision();
            ignoredRobot = robot;
            foreach (var shape in shapes) Physics.IgnoreCollision(shape, robot, true);
        }

        private void RestoreRobotCollision()
        {
            if (!ignoredRobot) return;
            foreach (var shape in shapes) if (shape) Physics.IgnoreCollision(shape, ignoredRobot, false);
            ignoredRobot = null;
        }

        private void OnDisable() => RestoreRobotCollision();
    }
}
