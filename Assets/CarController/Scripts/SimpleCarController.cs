using FishNet.Object;
using FishNet.Transporting;
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
    [SerializeField] private float minimumPitch = 1f;
    [SerializeField] private float maximumPitch = 3f;
    [SerializeField] private float boostZoneMultiplier = 1.5f;
    [SerializeField] private WheelCollider[] wheelColliders = new WheelCollider[4];
    [SerializeField] private Transform[] wheelMeshes = new Transform[4];
    [SerializeField] private Camera cam;
    [SerializeField] private float lookBackOffset = 10f;

    private Rigidbody rb;
    private OffsetView view;

    // Synchronized inputs processed by Server
    private float horizontalInput;
    private float verticalInput;

    // Input tracking to reduce RPC spam
    private float lastSentHorizontal;
    private float lastSentVertical;

    private bool look_back = false;
    private bool isReversing = false;
    private float rotationInPreviousFrame;
    private int currentGear = 0;
    private float currentSpeed;
    private float gearFactor;
    private float engineRpm;
    private float motorForceWithoutBoost;

    private Vector3 cam_initial_pos;
    private Vector3 cam_lookback_pos;
    private Quaternion cam_initial;
    private Quaternion cam_inverted;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        view = GetComponent<OffsetView>();
        
        if (cam != null)
        {
            cam.enabled = false;
            cam_initial = cam.transform.localRotation;
            cam_inverted = cam_initial * Quaternion.AngleAxis(180, Vector3.up);
            cam_initial_pos = cam.transform.localPosition;
            cam_lookback_pos = cam_initial_pos + Vector3.forward * lookBackOffset;
        }
    }

    private void Start()
    {
        motorForceWithoutBoost = motorForce;

        // Turn off Rigidbody physics on non-server clients so NetworkTransform has full control
        if (!base.IsServerInitialized)
        {
            rb.isKinematic = true;
        }
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (base.IsOwner)
        {
            if (cam != null) cam.enabled = true;
        }
        else if (cam != null)
        {
            AudioListener listener = cam.gameObject.GetComponent<AudioListener>();
            if (listener != null) Destroy(listener);
        }

        Cursor.lockState = CursorLockMode.Locked;
    }

    private void Update()
    {
        // 1. Owner collects inputs and sends to server when changed
        if (base.IsOwner)
        {
            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");

            if (Mathf.Abs(h - lastSentHorizontal) > 0.01f || Mathf.Abs(v - lastSentVertical) > 0.01f)
            {
                lastSentHorizontal = h;
                lastSentVertical = v;
                ServerSubmitInput(h, v);
            }

            // Camera controls
            if (cam != null)
            {
                if (look_back != Input.GetKey(KeyCode.C))
                {
                    cam.transform.localRotation = !look_back ? cam_inverted : cam_initial;
                    cam.transform.localPosition = !look_back ? cam_lookback_pos : cam_initial_pos;
                }
                look_back = Input.GetKey(KeyCode.C);
            }
        }

        // 2. Visual wheel mesh alignment & audio update on all clients
        HandleWheelTransform();
        CalculateEngineRevs();
        HandleAudio();
    }

    private void FixedUpdate()
    {
        // Only the server runs the vehicle physics loop
        if (!base.IsServerInitialized) return;

        UpdateCurrentSpeed();
        HandleSteering();
        HandleDrive();
        AntiRollForce();
        DetectReverse();
        TractionControl();
        SteeringAssist();
        HandleGearChange();
    }

    [ServerRpc]
    private void ServerSubmitInput(float horizontal, float vertical, Channel channel = Channel.Unreliable)
    {
        horizontalInput = Mathf.Clamp(horizontal, -1f, 1f);
        verticalInput = Mathf.Clamp(vertical, -1f, 1f);
    }

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
        float targetTorque = (verticalInput != 0) ? (motorForce * verticalInput / 2f) : 0f;
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