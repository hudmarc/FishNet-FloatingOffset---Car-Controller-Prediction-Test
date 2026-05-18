using Cinemachine;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting; // UPDATE: Required for Channel enum
using FloatingOffset.Runtime;
using UnityEngine;

enum SpeedType
{
    KPH,
    MPH
}

public class SimpleCarController : NetworkBehaviour
{
    [SerializeField] private GameObject visual;
    [SerializeField] private float maxSteerAngle;
    [SerializeField] private float motorForce;
    [SerializeField] private float brakeForce;
    [SerializeField] private float topSpeed;
    [SerializeField] private SpeedType speedType;
    [SerializeField] private float antiRoll = 1000f;
    [SerializeField] private bool tractionControl = true;
    [SerializeField] private float slipLimit = 0.3f;
    [SerializeField] private bool steeringAssist = true;
    [SerializeField] private float steeringAssistRatio = 0.5f;
    [SerializeField] private int numberOfGears;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private float minimumPitch;
    [SerializeField] private float maximumPitch;
    [SerializeField] private float boostZoneMultiplier;
    [SerializeField] private WheelCollider[] wheelColliders = new WheelCollider[4];
    [SerializeField] private Transform[] wheelMeshes = new Transform[4];
    [SerializeField] private Camera cam;
    [SerializeField] private OffsetUniverse universe;
    private OffsetTransform offsetTransform;

    #region Types.

    // UPDATE: Structs must now implement IReplicateData
    public struct MoveData : IReplicateData
    {
        public float Horizontal;
        public float Vertical;

        // UPDATE: Interface requirements
        private uint _tick;
        public void Dispose() { }
        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;

        // UPDATE: Struct constructors require ': this()'
        public MoveData(float horizontal, float vertical) : this()
        {
            Horizontal = horizontal;
            Vertical = vertical;
        }
    }

    // UPDATE: Structs must now implement IReconcileData
    public struct ReconcileData : IReconcileData
    {
        public double PositionX;
        public double PositionY;
        public double PositionZ;
        public Quaternion Rotation;
        public Vector3 Velocity;
        public Vector3 AngularVelocity;
        public float RotationInPreviousFrame;
        public int CurrentGear;
        public float FrontLeftSteerAngle;
        public float FrontRightSteerAngle;
        public float FrontLeftMotorTorque;
        public float FrontRightMotorTorque;

        public float FrontLeftBrakeTorque;
        public float FrontRightBrakeTorque;
        public float BackLeftBrakeTorque;
        public float BackRightBrakeTorque;

        // UPDATE: Interface requirements
        private uint _tick;
        public void Dispose() { }
        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;

        public ReconcileData(Vector3d position, Quaternion rotation, Vector3 velocity, Vector3 angularVelocity, float rotationInPreviousFrame, int currentGear,
               float frontLeftSteerAngle, float frontRightSteerAngle,
               float frontLeftMotorTorque, float frontRightMotorTorque,
               float frontLeftBrakeTorque, float frontRightBrakeTorque,
               float backLeftBrakeTorque, float backRightBrakeTorque) : this() // UPDATE: Struct constructors require ': this()'
        {
            PositionX = position.x;
            PositionY = position.y;
            PositionZ = position.z;
            Rotation = rotation;
            Velocity = velocity;
            AngularVelocity = angularVelocity;
            RotationInPreviousFrame = rotationInPreviousFrame;
            CurrentGear = currentGear;

            FrontLeftSteerAngle = frontLeftSteerAngle;
            FrontRightSteerAngle = frontRightSteerAngle;
            FrontLeftMotorTorque = frontLeftMotorTorque;
            FrontRightMotorTorque = frontRightMotorTorque;

            FrontLeftBrakeTorque = frontLeftBrakeTorque;
            FrontRightBrakeTorque = frontRightBrakeTorque;
            BackLeftBrakeTorque = backLeftBrakeTorque;
            BackRightBrakeTorque = backRightBrakeTorque;
        }
    }

    #endregion

    private Rigidbody rb;
    private float horizontalInput;
    private float verticalInput;
    private bool isReversing = false;
    private float rotationInPreviousFrame;
    private int currentGear = 0;
    private float currentSpeed;
    private float gearFactor;
    private float engineRpm;
    private float motorForceWithoutBoost;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        offsetTransform = GetComponent<OffsetTransform>();
        cam.enabled = false;
        // Subscriptions moved to OnStartNetwork
    }

    // UPDATE: Use OnStartNetwork/OnStopNetwork to ensure TimeManager is correctly referenced
    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        if (base.TimeManager != null)
        {
            base.TimeManager.OnTick += TimeManager_OnTick;
            base.TimeManager.OnPostTick += TimeManager_OnPostTick;
        }
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        if (base.TimeManager != null)
        {
            base.TimeManager.OnTick -= TimeManager_OnTick;
            base.TimeManager.OnPostTick -= TimeManager_OnPostTick;
        }
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (base.IsOwner)
        {
            cam.enabled = true;
        }
        else
        {
            Destroy(cam.gameObject.GetComponent<AudioListener>());
        }

        Cursor.lockState = CursorLockMode.Locked;
    }

    private void TimeManager_OnTick()
    {
        // UPDATE: Prediction manages IsServer, IsOwner checks, and automatic replays natively.
        // You just need to pass the data into your replicate method.
        Move(BuildMoveData());
        HandleWheelTransform();

    }

    private void TimeManager_OnPostTick()
    {
        // UPDATE: Call CreateReconcile on every post-tick. The framework handles whether it needs to be sent or not.
        CreateReconcile();

    }

    [Replicate]
    // UPDATE: Method signature updated to support (replaced bools with ReplicateState and Channel).
    private void Move(MoveData md, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
    {
        horizontalInput = md.Horizontal;
        verticalInput = md.Vertical;

        UpdateCurrentSpeed();
        HandleSteering();
        HandleDrive();

        AntiRoll();
        DetectReverse();
        TractionControl();
        SteeringAssist();
        HandleGearChange();
        CalculateEngineRevs();
        HandleAudio();
    }

    // UPDATE: Replaces CheckInput to return a struct and handle ownership checks explicitly.
    private MoveData BuildMoveData()
    {
        if (!base.IsOwner)
            return default;

        var horizontal = Input.GetAxis("Horizontal");
        var vertical = Input.GetAxis("Vertical");

        return new MoveData(horizontal, vertical);
    }

    private void Start()
    {
        motorForceWithoutBoost = motorForce;
    }

    private void UpdateCurrentSpeed()
    {
        if (speedType == SpeedType.KPH)
        {
            currentSpeed = rb.velocity.magnitude * 3.6f;
        }
        else
        {
            currentSpeed = rb.velocity.magnitude * 2.23693629f;
        }
    }

    private void HandleSteering()
    {
        wheelColliders[0].steerAngle = maxSteerAngle * horizontalInput;
        wheelColliders[1].steerAngle = maxSteerAngle * horizontalInput;
    }

    private void HandleDrive()
    {
        wheelColliders[0].motorTorque = motorForce * verticalInput / 2;
        wheelColliders[1].motorTorque = motorForce * verticalInput / 2;

        if (!isReversing && verticalInput < 0 && rb.velocity.magnitude > 1)
        {
            ApplyBrakes();
        }
        else
        {
            ResetBrakes();
        }
    }

    private void ApplyBrakes()
    {
        for (int i = 0; i < wheelColliders.Length; i++)
        {
            wheelColliders[i].brakeTorque = -brakeForce * verticalInput;
        }
    }

    private void ResetBrakes()
    {
        for (int i = 0; i < wheelColliders.Length; i++)
        {
            wheelColliders[i].brakeTorque = 0f;
        }
    }

    private void HandleWheelTransform()
    {
        for (int i = 0; i < wheelMeshes.Length; i++)
        {
            Vector3 pos = wheelMeshes[i].position;
            Quaternion quat = wheelMeshes[i].rotation;

            wheelColliders[i].GetWorldPose(out pos, out quat);

            // adjust because visuals are on different parent
            pos = pos - wheelColliders[i].transform.parent.position + wheelMeshes[i].parent.position;

            wheelMeshes[i].position = pos;
            wheelMeshes[i].rotation = quat;
        }
    }

    private void AntiRoll()
    {
        // Front axle
        ApplyAntiRoll(wheelColliders[0], wheelColliders[1]);
        // Back axle
        ApplyAntiRoll(wheelColliders[2], wheelColliders[3]);
    }

    private void ApplyAntiRoll(WheelCollider left, WheelCollider right)
    {
        WheelHit hit;
        float travelLeft = 1f;
        float travelRight = 1f;

        bool isGroundedLeft = left.GetGroundHit(out hit);
        if (isGroundedLeft)
        {
            travelLeft = (-left.transform.InverseTransformPoint(hit.point).y - left.radius) / left.suspensionDistance;
        }
        bool isGroundedRight = right.GetGroundHit(out hit);
        if (isGroundedRight)
        {
            travelRight = (-right.transform.InverseTransformPoint(hit.point).y - right.radius) / right.suspensionDistance;
        }

        float antirollForce = (travelLeft - travelRight) * antiRoll;

        if (isGroundedLeft)
        {
            rb.AddForceAtPosition(left.transform.up * -antirollForce, left.transform.position);
        }
        if (isGroundedRight)
        {
            rb.AddForceAtPosition(right.transform.up * antirollForce, right.transform.position);
        }
    }

    private void DetectReverse()
    {
        float rpmSum = 0f;
        for (int i = 0; i < wheelColliders.Length; i++)
        {
            rpmSum += wheelColliders[i].rpm;
        }
        isReversing = rpmSum / wheelColliders.Length < 0;
    }

    private void TractionControl()
    {
        if (tractionControl)
        {
            WheelHit hit;
            wheelColliders[0].GetGroundHit(out hit);
            if (hit.forwardSlip >= slipLimit && wheelColliders[0].motorTorque > 0)
            {
                wheelColliders[0].motorTorque *= 0.9f;
            }
            wheelColliders[1].GetGroundHit(out hit);
            if (hit.forwardSlip >= slipLimit && wheelColliders[1].motorTorque > 0)
            {
                wheelColliders[1].motorTorque *= 0.9f;
            }
        }
    }

    private void SteeringAssist()
    {
        if (Mathf.Abs(rotationInPreviousFrame - transform.eulerAngles.y) < 10f && steeringAssist)
        {
            var turnadjust = (transform.eulerAngles.y - rotationInPreviousFrame) * steeringAssistRatio;
            Quaternion velocityRotation = Quaternion.AngleAxis(turnadjust, Vector3.up);
            rb.velocity = velocityRotation * rb.velocity;
        }
        rotationInPreviousFrame = transform.eulerAngles.y;
    }

    private void HandleGearChange()
    {
        float speedRatio = Mathf.Abs(currentSpeed / topSpeed);
        float upshiftLimit = 1 / (float)numberOfGears * (currentGear + 1);
        float downshiftLimit = 1 / (float)numberOfGears * currentGear;

        if (currentGear > 0 && speedRatio < downshiftLimit)
        {
            currentGear--;
        }

        if (speedRatio > upshiftLimit && (currentGear < (numberOfGears - 1)))
        {
            currentGear++;
        }
    }

    private static float ULerp(float from, float to, float value)
    {
        return (1.0f - value) * from + value * to;
    }

    private static float CurveFactor(float factor)
    {
        return 1 - (1 - factor) * (1 - factor);
    }

    private void CalculateGearFactor()
    {
        float f = (1 / (float)numberOfGears);
        var targetGearFactor = Mathf.InverseLerp(f * currentGear, f * (currentGear + 1), Mathf.Abs(currentSpeed / topSpeed));
        gearFactor = Mathf.Lerp(gearFactor, targetGearFactor, (float)(TimeManager.TickDelta * 5f));
    }

    private void CalculateEngineRevs()
    {
        CalculateGearFactor();
        var gearNumFactor = currentGear / (float)numberOfGears;
        var revsRangeMin = ULerp(0f, 1f, CurveFactor(gearNumFactor));
        var revsRangeMax = ULerp(1f, 1f, gearNumFactor);
        engineRpm = ULerp(revsRangeMin, revsRangeMax, gearFactor);
    }

    private void HandleAudio()
    {
        float pitch = ULerp(minimumPitch, maximumPitch, engineRpm);

        if (pitch < minimumPitch)
        {
            pitch = minimumPitch;
        }

        audioSource.pitch = pitch;
    }

    public float GetCurrentSpeed()
    {
        return Mathf.Floor(currentSpeed);
    }

    public void MuteAudio()
    {
        audioSource.volume = 0;
    }

    public void ActivateBoost()
    {
        motorForce = motorForceWithoutBoost * boostZoneMultiplier;
    }

    public void DeactivateBoost()
    {
        motorForce = motorForceWithoutBoost;
    }

    // UPDATE: Build reconcile data here and invoke your Reconcile method.
    public override void CreateReconcile()
    {
        ReconcileData rd = new ReconcileData(offsetTransform.GetRealPosition(), transform.rotation, rb.velocity, rb.angularVelocity, rotationInPreviousFrame, currentGear,
            wheelColliders[0].steerAngle, wheelColliders[1].steerAngle,
            wheelColliders[0].motorTorque, wheelColliders[1].motorTorque,
            wheelColliders[0].brakeTorque, wheelColliders[1].brakeTorque, wheelColliders[2].brakeTorque, wheelColliders[3].brakeTorque);

        Reconciliation(rd);
    }


    [Reconcile]
    // UPDATE: Method signature updated to support (replaced bool asServer with Channel).
    private void Reconciliation(ReconcileData rd, Channel channel = Channel.Unreliable)
    {
        var position = new Vector3d(rd.PositionX, rd.PositionY, rd.PositionZ);

        Vector3d difference = position - offsetTransform.GetRealPosition();
        offsetTransform.transform.position += Mathd.toVector3(difference);


        transform.rotation = rd.Rotation;
        Debug.Log($"PREV: {rb.velocity.magnitude} INCOMING: {rd.Velocity.magnitude}");
        rb.velocity = rd.Velocity;
        rb.angularVelocity = rd.AngularVelocity;
        rotationInPreviousFrame = rd.RotationInPreviousFrame;
        currentGear = rd.CurrentGear;
        wheelColliders[0].steerAngle = rd.FrontLeftSteerAngle;
        wheelColliders[1].steerAngle = rd.FrontRightSteerAngle;
        wheelColliders[0].motorTorque = rd.FrontLeftMotorTorque;
        wheelColliders[1].motorTorque = rd.FrontRightMotorTorque;
        wheelColliders[0].brakeTorque = rd.FrontLeftBrakeTorque;
        wheelColliders[1].brakeTorque = rd.FrontRightBrakeTorque;
        wheelColliders[2].brakeTorque = rd.BackLeftBrakeTorque;
        wheelColliders[3].brakeTorque = rd.BackRightBrakeTorque;

        bool was_offset = difference.magnitude > 0.1;

        float targetStiffness = was_offset ? 0f : 1f;

        // Temporarily disable wheel stiffness to prevent bugs from scene changing
        for (int i = 0; i < wheelColliders.Length; i++)
        {
            // Extract, modify, and reassign Forward Friction
            WheelFrictionCurve fFriction = wheelColliders[i].forwardFriction;
            fFriction.stiffness = targetStiffness;
            wheelColliders[i].forwardFriction = fFriction;

            // Extract, modify, and reassign Sideways Friction
            WheelFrictionCurve sFriction = wheelColliders[i].sidewaysFriction;
            sFriction.stiffness = targetStiffness;
            wheelColliders[i].sidewaysFriction = sFriction;
        }
    }
}