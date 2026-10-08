using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using FloatingOffset.Runtime;
using UnityEngine;
using FishNet.Connection;

/// <summary>
/// A simple AI NPC car controller that was AI-generated with Gemini 3.6-Flash for this techdemo.
/// Not thoroughly tested. No guarantees of proper function. Use at your own risk.
/// </summary>
public class VibeCodedAICarController : NetworkBehaviour
{
    private enum AiState
    {
        Chasing,
        UnstuckReversing,
        UnstuckForward
    }

    [Header("AI Settings")]
    [SerializeField] private float targetSearchInterval = 0.2f;
    [SerializeField] private float reverseDistanceThreshold = 3f;

    [Header("Unstuck Settings")]
    [SerializeField] private float stuckSpeedThreshold = 0.5f; // Speed below which car is considered stationary (m/s)
    [SerializeField] private float stuckTimeThreshold = 2.0f;  // Seconds stationary before trigger
    [SerializeField] private float unstuckReverseDuration = 1.5f; // Seconds spent reversing & turning ~60 deg
    [SerializeField] private float unstuckForwardDuration = 2.0f; // Seconds spent driving forward out of obstacle

    [Header("Car Settings")]
    [SerializeField] private GameObject visual;
    [SerializeField] private float maxSteerAngle = 30f;
    [SerializeField] private float motorForce = 1500f;
    [SerializeField] private float brakeForce = 3000f;
    [SerializeField] private float topSpeed = 150f;
    [SerializeField] private SpeedType speedType;
    [SerializeField] private float antiRoll = 1000f;
    [SerializeField] private bool tractionControl = true;
    [SerializeField] private float slipLimit = 0.3f;
    [SerializeField] private bool steeringAssist = true;
    [SerializeField] private float steeringAssistRatio = 0.5f;
    [SerializeField] private int numberOfGears = 5;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private float minimumPitch = 0.5f;
    [SerializeField] private float maximumPitch = 2.0f;
    [SerializeField] private float boostZoneMultiplier = 1.5f;
    [SerializeField] private WheelCollider[] wheelColliders = new WheelCollider[4];
    [SerializeField] private Transform[] wheelMeshes = new Transform[4];

    private Rigidbody rb;
    private OffsetView view;

    private float horizontalInput;
    private float verticalInput;
    private bool isReversing = false;
    private float rotationInPreviousFrame;
    private int currentGear = 0;
    private float currentSpeed;
    private float gearFactor;
    private float engineRpm;
    private float motorForceWithoutBoost;

    // AI Tracking & State Variables
    private Transform targetPlayer;
    private float searchTimer;
    private AiState currentState = AiState.Chasing;
    private float stuckTimer = 0f;
    private float stateTimer = 0f;
    private float unstuckSteerDirection = 1f;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        view = GetComponent<OffsetView>();
    }

    private void Start()
    {
        motorForceWithoutBoost = motorForce;

        // Disable local physics simulation on non-server clients so NetworkTransform moves the vehicle
        if (!base.IsServerInitialized)
        {
            rb.isKinematic = true;
        }
    }

    private void Update()
    {
        // Visual wheel mesh alignment & engine pitch audio update on all clients
        HandleWheelTransform();
        CalculateEngineRevs();
        HandleAudio();
    }

    private void FixedUpdate()
    {
        // Only the server runs AI decision making and vehicle physics
        if (!base.IsServerInitialized) return;

        float fixedDelta = Time.fixedDeltaTime;

        // 1. Target Acquisition
        searchTimer += fixedDelta;
        if (searchTimer >= targetSearchInterval || targetPlayer == null)
        {
            searchTimer = 0f;
            FindNearestPlayer();
        }

        // 2. Process AI State Machine & Inputs
        UpdateAiDecision(fixedDelta);

        // 3. Execute Server Physics Loop
        UpdateCurrentSpeed();
        HandleSteering();
        HandleDrive();
        AntiRollForce();
        DetectReverse();
        TractionControl();
        SteeringAssist();
        HandleGearChange();
    }

    #region AI Logic

    private void UpdateAiDecision(float delta)
    {
        // Execute Unstuck State Machine
        if (currentState == AiState.UnstuckReversing)
        {
            stateTimer -= delta;
            if (stateTimer <= 0f)
            {
                currentState = AiState.UnstuckForward;
                stateTimer = unstuckForwardDuration;
            }
            horizontalInput = unstuckSteerDirection;
            verticalInput = -1f;
            return;
        }

        if (currentState == AiState.UnstuckForward)
        {
            stateTimer -= delta;
            if (stateTimer <= 0f)
            {
                currentState = AiState.Chasing;
                stuckTimer = 0f;
            }
            horizontalInput = 0f;
            verticalInput = 1f;
            return;
        }

        if (targetPlayer == null)
        {
            horizontalInput = 0f;
            verticalInput = 0f;
            return;
        }

        // Floating Origin Vector & Angle Calculation
        Vector3d realTargetPos = OffsetUtils.GetRealPosition(targetPlayer);
        Vector3d realSelfPos = OffsetUtils.GetRealPosition(transform);
        Vector3 worldDelta = OffsetUtils.ToVector3(realTargetPos - realSelfPos);
        Vector3 localTarget = transform.InverseTransformDirection(worldDelta);

        // Calculate angle (-180 to +180 deg) relative to forward
        float targetAngle = Vector3.SignedAngle(Vector3.forward, localTarget, Vector3.up);

        // Trigger reverse ONLY when the player is significantly behind (>120 deg)
        if (Mathf.Abs(targetAngle) > 120f)
        {
            verticalInput = -1f;
            // In reverse, steering opposite to target direction swings front nose toward target
            horizontalInput = (targetAngle > 0f) ? -1f : 1f;
        }
        else
        {
            verticalInput = 1f;
            horizontalInput = Mathf.Clamp(targetAngle / maxSteerAngle, -1f, 1f);
        }

        // Stuck Detection Logic
        bool isStationary = rb.velocity.sqrMagnitude < (stuckSpeedThreshold * stuckSpeedThreshold);
        if (isStationary && Mathf.Abs(verticalInput) > 0.1f)
        {
            stuckTimer += delta;
            if (stuckTimer >= stuckTimeThreshold)
            {
                currentState = AiState.UnstuckReversing;
                stateTimer = unstuckReverseDuration;
                unstuckSteerDirection = (targetAngle > 0f) ? -1f : 1f;
                stuckTimer = 0f;
            }
        }
        else
        {
            stuckTimer = Mathf.Max(0f, stuckTimer - delta);
        }
    }

    private void FindNearestPlayer()
    {
        SimpleCarController[] players = Object.FindObjectsByType<SimpleCarController>(FindObjectsSortMode.None);
        float minSqrDistance = float.MaxValue;
        Transform nearest = null;
        Vector3d currentPos = OffsetUtils.GetRealPosition(transform);

        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] == null || !players[i].gameObject.activeInHierarchy)
                continue;

            float sqrDist = (float)Vector3d.SquaredMagnitude(OffsetUtils.GetRealPosition(players[i].transform) - currentPos);
            if (sqrDist < minSqrDistance)
            {
                minSqrDistance = sqrDist;
                nearest = players[i].transform;
            }
        }

        targetPlayer = nearest;
    }

    #endregion

    #region Vehicle Physics Logic

    private void UpdateCurrentSpeed()
    {
        currentSpeed = (speedType == SpeedType.KPH)
            ? rb.velocity.magnitude * 3.6f
            : rb.velocity.magnitude * 2.23693629f;
    }

    private void HandleSteering()
    {
        wheelColliders[0].steerAngle = maxSteerAngle * horizontalInput;
        wheelColliders[1].steerAngle = maxSteerAngle * horizontalInput;
    }

    private void HandleDrive()
    {
        float targetTorque = motorForce * verticalInput / 2f;
        wheelColliders[0].motorTorque = targetTorque;
        wheelColliders[1].motorTorque = targetTorque;

        if (!isReversing && verticalInput < 0 && rb.velocity.magnitude > 1f)
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
            if (wheelColliders[i] == null || wheelMeshes[i] == null) continue;

            Vector3 pos;
            Quaternion quat;
            wheelColliders[i].GetWorldPose(out pos, out quat);

            wheelMeshes[i].position = pos;
            wheelMeshes[i].rotation = quat;
        }
    }

    private void AntiRollForce()
    {
        ApplyAntiRoll(wheelColliders[0], wheelColliders[1]);
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
        if (!tractionControl) return;

        WheelHit hit;
        if (wheelColliders[0].GetGroundHit(out hit) && hit.forwardSlip >= slipLimit && wheelColliders[0].motorTorque > 0)
        {
            wheelColliders[0].motorTorque *= 0.9f;
        }
        if (wheelColliders[1].GetGroundHit(out hit) && hit.forwardSlip >= slipLimit && wheelColliders[1].motorTorque > 0)
        {
            wheelColliders[1].motorTorque *= 0.9f;
        }
    }

    private void SteeringAssist()
    {
        float currentY = transform.eulerAngles.y;
        float deltaY = Mathf.DeltaAngle(rotationInPreviousFrame, currentY);

        if (Mathf.Abs(deltaY) < 10f && steeringAssist && rb.velocity.sqrMagnitude > 0.1f)
        {
            float turnadjust = deltaY * steeringAssistRatio;
            Quaternion velocityRotation = Quaternion.AngleAxis(turnadjust, Vector3.up);
            rb.velocity = velocityRotation * rb.velocity;
        }

        rotationInPreviousFrame = currentY;
    }

    private void HandleGearChange()
    {
        float speedRatio = Mathf.Abs(currentSpeed / topSpeed);
        float upshiftLimit = 1f / numberOfGears * (currentGear + 1);
        float downshiftLimit = 1f / numberOfGears * currentGear;

        if (currentGear > 0 && speedRatio < downshiftLimit)
        {
            currentGear--;
        }

        if (speedRatio > upshiftLimit && (currentGear < (numberOfGears - 1)))
        {
            currentGear++;
        }
    }

    private void CalculateEngineRevs()
    {
        float f = 1f / numberOfGears;
        var targetGearFactor = Mathf.InverseLerp(f * currentGear, f * (currentGear + 1), Mathf.Abs(currentSpeed / topSpeed));
        gearFactor = Mathf.Lerp(gearFactor, targetGearFactor, Time.deltaTime * 5f);

        var gearNumFactor = currentGear / (float)numberOfGears;
        var revsRangeMin = Mathf.Lerp(0f, 1f, 1f - (1f - gearNumFactor) * (1f - gearNumFactor));
        var revsRangeMax = Mathf.Lerp(1f, 1f, gearNumFactor);
        engineRpm = Mathf.Lerp(revsRangeMin, revsRangeMax, gearFactor);
    }

    private void HandleAudio()
    {
        if (audioSource == null) return;
        float pitch = Mathf.Lerp(minimumPitch, maximumPitch, engineRpm);
        audioSource.pitch = Mathf.Max(pitch, minimumPitch);
    }

    public float GetCurrentSpeed() => Mathf.Floor(currentSpeed);
    public void MuteAudio() { if (audioSource != null) audioSource.volume = 0; }
    public void ActivateBoost() => motorForce = motorForceWithoutBoost * boostZoneMultiplier;
    public void DeactivateBoost() => motorForce = motorForceWithoutBoost;

    #endregion
}