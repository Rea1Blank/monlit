using UnityEngine;
using UnityEngine.InputSystem;

namespace Monlit
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class WorkshopRobot : MonoBehaviour
    {
        internal const float GripHeight = 0.58f;
        private const float PrecisionMoveSpeed = 0.9f;
        private const float PrecisionTurnSpeed = 90f;

        [SerializeField] private WorkshopPiece[] pieces;
        [SerializeField, HideInInspector] private WorkshopFigure[] figures;
        [SerializeField] private Transform holdPoint;
        [SerializeField] private BoxCollider floor;
        [SerializeField, Min(0.1f)] private float moveSpeed = 4f;
        [SerializeField, Min(0.1f)] private float interactionRange = 1.55f;
        [SerializeField, Min(1f)] private float turnSpeed = 720f;
        [SerializeField] private WorkshopNailVisual.Finishes hammerArt;

        private CharacterController controller;
        private Transform cameraTransform;
        private InputAction moveAction;
        private InputAction turnAction;
        private InputAction interactAction;
        private InputAction fastenAction;
        private Vector3 walkingVelocity;
        private Vector3 driveVelocity;
        private float driveTime;
        private Quaternion facing;
        private int menuInputFrame = -1;
        private WorkshopNailVisual lastStrike;

        internal float InteractionRange => interactionRange;
        internal bool PrecisionSteering => Keyboard.current?.shiftKey.isPressed == true;
        internal bool CarryAngleLocked { get; private set; }
        public WorkshopPiece HeldPiece { get; private set; }
        public float GripLostAt { get; private set; } = float.NegativeInfinity;
        public bool IsCarrying => HeldPiece != null;
        public bool IsSettingDown => IsCarrying && !HeldPiece.IsHeld;
        public Transform LeftGrip => HeldPiece ? HeldPiece.LeftGrip : null;
        public Transform RightGrip
        {
            get
            {
                if (HeldPiece) return HeldPiece.RightGrip;
                return lastStrike ? lastStrike.Handle : null;
            }
        }
        public bool CanSeatHeld => IsCarrying && HeldPiece.CanSeatHeld;
        public int PieceCount => pieces?.Length ?? 0;
        public bool CanPickUp => PickupCandidate != null;
        public WorkshopPiece NailCandidate
        {
            get
            {
                if (!HeldPiece) return NearbyFastener(false);
                return HeldPiece.CanNail ? HeldPiece : null;
            }
        }
        public WorkshopPiece NailedCandidate => HeldPiece ? null : NearbyFastener(true);
        public bool IsDelivered
        {
            get
            {
                if (figures == null || figures.Length == 0) return PieceCount > 0 && DeliveredCount == PieceCount;
                foreach (var figure in figures) if (!figure.IsComplete) return false;
                return true;
            }
        }

        public int DeliveredCount
        {
            get
            {
                int count = 0;
                if (figures != null && figures.Length > 0)
                {
                    foreach (var figure in figures) count += figure.FilledCount;
                    return count;
                }
                for (int i = 0; i < PieceCount; i++)
                {
                    if (pieces[i].IsDelivered)
                        count++;
                }
                return count;
            }
        }

        public WorkshopPiece PickupCandidate
        {
            get
            {
                if (IsCarrying)
                    return null;

                WorkshopPiece nearest = null;
                float nearestSquared = interactionRange * interactionRange;
                for (int i = 0; i < PieceCount; i++)
                {
                    var candidate = pieces[i];
                    Vector3 offset = candidate.transform.position - transform.position;
                    float squared = offset.x * offset.x + offset.z * offset.z;
                    if (candidate.CanPickUp && squared <= nearestSquared)
                    {
                        nearest = candidate;
                        nearestSquared = squared;
                    }
                }
                return nearest;
            }
        }

        private WorkshopPiece NearbyFastener(bool remove)
        {
            WorkshopPiece closest = null;
            float limit = interactionRange * interactionRange;
            for (int i = 0; i < PieceCount; i++)
            {
                var piece = pieces[i];
                if (!piece || !piece.RequiresNail || piece.IsNailed != remove) continue;
                Vector3 delta = piece.transform.position - transform.position;
                float distance = delta.x * delta.x + delta.z * delta.z;
                if (distance > limit || (!remove && !piece.CanNail)) continue;
                closest = piece;
                limit = distance;
            }
            return closest;
        }

        public void Configure(WorkshopPiece[] movablePieces, Transform carryingPoint, BoxCollider workshopFloor,
            WorkshopFigure[] goals = null)
        {
            pieces = movablePieces;
            figures = goals;
            facing = transform.rotation;
            holdPoint = carryingPoint;
            holdPoint.localPosition = new Vector3(0f, GripHeight, 0.85f);
            floor = workshopFloor;
            controller = GetComponent<CharacterController>();
            controller.height = 1.25f;
            controller.radius = 0.36f;
            controller.center = Vector3.up * (controller.height * 0.5f);
            controller.stepOffset = 0.12f;
            controller.skinWidth = 0.04f;
            controller.minMoveDistance = 0f;
        }

        internal void ResetRound()
        {
            pieces = System.Array.Empty<WorkshopPiece>();
            figures = null;
            ReleaseHands();
            lastStrike = null;
            GripLostAt = float.NegativeInfinity;
            walkingVelocity = driveVelocity = Vector3.zero;
            driveTime = 0f;
        }

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            var workshopCamera = Camera.main;
            cameraTransform = workshopCamera ? workshopCamera.transform : null;
            facing = transform.rotation;
            moveAction = InputSystem.actions.FindAction("Player/Move", true);
            turnAction = InputSystem.actions.FindAction("Player/Turn", true);
            interactAction = InputSystem.actions.FindAction("Player/Interact", true);
            fastenAction = InputSystem.actions.FindAction("Player/Fasten", true);
        }

        private void Update()
        {
            if (Time.frameCount == menuInputFrame) return;
            if (IsSettingDown)
            {
                walkingVelocity = Vector3.zero;
                return;
            }
            if (HeldPiece && HeldPiece.IsHeld)
                HeldPiece.TurnInHands(turnAction.ReadValue<float>() * PrecisionTurnSpeed * Time.deltaTime);
            Move(moveAction.ReadValue<Vector2>(), Time.deltaTime);
            if (fastenAction.WasPressedThisFrame())
            {
                if (!TryNail()) TryRemoveNail();
            }
            else if (interactAction.WasPressedThisFrame())
                Interact();
        }

        internal void SkipMenuInputFrame() => menuInputFrame = Time.frameCount;

        internal void SetCarryAngleLocked(bool value)
        {
            CarryAngleLocked = value;
            if (IsCarrying) facing = transform.rotation;
        }

        private void Move(Vector2 input, float deltaTime)
        {
            walkingVelocity = Vector3.zero;
            if (deltaTime <= 0f)
                return;

            Vector3 direction;
            float speed = moveSpeed;
            if (PrecisionSteering)
            {
                TurnManually(input.x, deltaTime);
                direction = transform.forward * input.y;
                speed = Mathf.Min(PrecisionMoveSpeed, moveSpeed);
            }
            else
            {
                Vector3 right = cameraTransform ? Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized : Vector3.right;
                Vector3 forward = Vector3.Cross(right, Vector3.up);
                direction = Vector3.ClampMagnitude(right * input.x + forward * input.y, 1f);
                if (!CarryAngleLocked || !IsCarrying)
                {
                    if (direction.sqrMagnitude > .0001f) facing = Quaternion.LookRotation(direction);
                    TurnTo(Quaternion.RotateTowards(transform.rotation, facing, turnSpeed * deltaTime));
                }
            }
            Vector3 start = transform.position;
            Vector3 step = direction * (speed * deltaTime);
            if (HeldPiece) step = HeldPiece.LimitLoadedStep(step);
            Vector3 next = ClampToDeck(start + step, transform.rotation);

            // Keep feet on the deck when walking back down from a socket ramp.
            driveVelocity = (next - start) / deltaTime;
            driveTime = deltaTime;
            controller.Move(next - start + Vector3.down * (4f * deltaTime));
            driveVelocity = Vector3.zero;
            walkingVelocity = Vector3.ProjectOnPlane(transform.position - start, Vector3.up) / deltaTime;
        }

        private void TurnManually(float input, float deltaTime)
        {
            float yaw = input * PrecisionTurnSpeed * deltaTime;
            TurnTo(Quaternion.AngleAxis(yaw, Vector3.up) * transform.rotation);
            facing = transform.rotation;
        }

        private void TurnTo(Quaternion rotation)
        {
            transform.rotation = HeldPiece ? HeldPiece.LimitLoadedTurn(rotation) : rotation;
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (driveVelocity.sqrMagnitude < 0.0001f || Mathf.Abs(hit.normal.y) > 0.65f)
                return;
            var other = hit.collider.attachedRigidbody;
            if (!other || other.isKinematic || !other.TryGetComponent<WorkshopPiece>(out var piece) ||
                piece.IgnoredRobot == controller)
                return;

            Vector3 inward = Vector3.ProjectOnPlane(-hit.normal, Vector3.up).normalized;
            float speed = Vector3.Dot(driveVelocity, inward);
            if (speed > 0.05f)
                piece.RequestPush(inward * Mathf.Min(speed, 2.4f), driveTime);
        }

        private Vector3 ClampToDeck(Vector3 position, Quaternion rotation)
        {
            Bounds bounds = floor.bounds;
            Vector3 minimum = bounds.min + Vector3.one * controller.radius;
            Vector3 maximum = bounds.max - Vector3.one * controller.radius;
            if (IsCarrying)
            {
                Vector3 handOffset = rotation * (holdPoint.localPosition + holdPoint.localRotation * HeldPiece.GripOffset);
                float pieceClearance = HeldPiece.DeckClearance;
                // A grab at the rim can start outside the casting's conservative clearance.
                // It limits future outward steps without displacing an idle robot.
                var start = transform.position;
                minimum = Vector3.Max(minimum, Vector3.Min(start, bounds.min + Vector3.one * pieceClearance - handOffset));
                maximum = Vector3.Min(maximum, Vector3.Max(start, bounds.max - Vector3.one * pieceClearance - handOffset));
            }
            return new Vector3(Mathf.Clamp(position.x, minimum.x, maximum.x), position.y,
                Mathf.Clamp(position.z, minimum.z, maximum.z));
        }

        public bool Interact()
        {
            if (IsSettingDown)
                return false;
            if (IsCarrying)
            {
                HeldPiece.SetDown(walkingVelocity, floor.bounds.max.y, ReleaseHands);
                return true;
            }

            var piece = PickupCandidate;
            if (piece == null)
                return false;

            piece.PickUp(holdPoint, controller);
            facing = transform.rotation;
            HeldPiece = piece;
            return true;
        }

        private void ReleaseHands() => HeldPiece = null;

        public bool TryNail()
        {
            if (!isActiveAndEnabled || Time.timeScale <= 0f) return false;
            var piece = NailCandidate;
            if (!piece || !piece.TryNail()) return false;
            if (HeldPiece == piece) ReleaseHands();
            lastStrike = WorkshopNailVisual.Strike(piece, transform, hammerArt);
            return true;
        }

        public bool TryRemoveNail()
        {
            if (!isActiveAndEnabled || Time.timeScale <= 0f) return false;
            var piece = NailedCandidate;
            if (!piece || !piece.TryRemoveNail()) return false;
            WorkshopAudio.Play(WorkshopAudio.Effect.Clamp, piece.Body.position, .52f);
            var nail = piece.GetComponentInChildren<WorkshopNailVisual>();
            if (nail)
            {
                if (lastStrike == nail) lastStrike = null;
                nail.gameObject.SetActive(false);
                Destroy(nail.gameObject);
            }
            return true;
        }

        internal void LoseGrip(WorkshopPiece piece)
        {
            if (HeldPiece != piece) return;
            GripLostAt = Time.time;
            ReleaseHands();
        }
    }
}
