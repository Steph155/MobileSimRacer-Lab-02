using UnityEngine;

[RequireComponent(typeof(CarVisualRig))]
[DefaultExecutionOrder(-100)]
public class CarController : MonoBehaviour
{
    [Header("References")]
    public CarVisualRig rig;
    public CarInputManager input;
    public TelemetryHUD hud;

    [Header("Powertrain")]
    public VehicleEngine engine = new VehicleEngine();
    public TyreParams frontTyre = new TyreParams();
    public TyreParams rearTyre = new TyreParams();
    public SuspensionParams frontSusp = new SuspensionParams();
    public SuspensionParams rearSusp = new SuspensionParams();
    public bool rearWheelDrive = true;

    [Header("Mass / Inertia")]
    public float mass = 1450f;

    [Header("Determinism")]
    [Tooltip("Fixed physics timestep. The sim only ever advances in these increments.")]
    public float fixedStep = 1f / 240f;
    public int maxSubSteps = 12;

    [Header("Steering / Alignment")]
    public float maxSteerAngle = 32f;     // degrees
    [Range(-1f, 1f)] public float ackermann = -0.35f; // negative = anti-ackermann
    public float camberGainPerMeter = -1.5f; // deg of camber gained per metre of compression

    [Header("World")]
    public LayerMask groundMask = -1;
    public Vector3 spawnPosition = new Vector3(0f, 1.0f, 0f); // ~0.35m drop: gentle enough for the 10cm travel

    [Header("Brakes")]
    public float maxBrakeTorque = 1600f;

    [Header("Determinism replay")]
    public bool recordInputs = false;
    public bool playbackInputs = false;

    // ---- runtime body state (world frame) ----
    Vector3 position;
    Vector3 velocity;
    Quaternion orientation;
    Vector3 angularVelocity; // world frame

    float[] wheelOmega = new float[4];      // spin (rad/s)
    float[] compression = new float[4];     // current suspension compression (m)
    float[] compressionVel = new float[4];  // d(compression)/dt

    int gearIndex = 1;       // 1..N forward
    bool inReverse = false;
    float reverseTimer = 0f;
    float rpm = 0f;
    float speedKph = 0f;

    float steerAngleCurrent; // rad, smoothed

    // inertia (body diagonal)
    Vector3 inertia;
    Vector3 invInertia;

    float accumulator = 0f;
    System.Collections.Generic.List<CarInput> recorded = new System.Collections.Generic.List<CarInput>();
    int playbackIndex = 0;

    // Tight double-wishbone coilover: the chassis perch sits close to the lower
    // ball joint (a fraction of the chassis height), not a long 0.5m raycast spring.
    public float springTopLocalY = -0.05f;
    float springFreeLength = 0.18f;

    // per-step scratch
    struct WheelStep
    {
        public Vector3 contact;
        public Vector3 center;   // kinematic wheel centre (wishbone arc)
        public Vector3 anchor;
        public float currentLength;
        public float radius;
        public bool grounded;
        public float fz;
        public float steerRad;
        public Quaternion align;
    }

    void Awake()
    {
        if (rig == null) rig = GetComponent<CarVisualRig>();
        rig.drivenByController = true;
        engine.InitDefaults();

        // Box inertia about body axes (x=lateral/pitch, y=vertical/yaw, z=longitudinal/roll).
        Vector3 s = rig.chassisSize;
        float ix = mass / 12f * (s.y * s.y + s.z * s.z);
        float iy = mass / 12f * (s.x * s.x + s.z * s.z);
        float iz = mass / 12f * (s.x * s.x + s.y * s.y);
        inertia = new Vector3(ix, iy, iz);
        invInertia = new Vector3(1f / ix, 1f / iy, 1f / iz);

        // Size the tight coilover so ride height is automatic and the spawn drop is
        // within the 10 cm travel (no slam -> no bounce/launch).
        float compEq = mass * VehicleMath.Gravity / (4f * frontSusp.cornerSpringK);
        springFreeLength = compEq + 0.15f; // tight free length (~0.20 m)
        float uprightHalf = (rig.fUpperLeftFrontMount.y - rig.fLowerLeftFrontMount.y) * 0.5f;
        // Equilibrium body height so the wheel rests exactly on the ground.
        float bodyEq = -springTopLocalY + (springFreeLength - compEq) - uprightHalf + frontWheelRadius;
        if (spawnPosition.y < bodyEq + 0.02f) spawnPosition.y = bodyEq + 0.06f;

        ResetToSpawn();
    }

    void ResetToSpawn()
    {
        position = spawnPosition;
        velocity = Vector3.zero;
        orientation = Quaternion.identity;
        angularVelocity = Vector3.zero;
        for (int i = 0; i < 4; i++) { wheelOmega[i] = 0f; compression[i] = 0f; compressionVel[i] = 0f; }
        gearIndex = 1; inReverse = false; reverseTimer = 0f; rpm = engine.idleRpm;
        playbackIndex = 0;
    }

    void FixedUpdate()
    {
        if (recordInputs && playbackInputs) playbackInputs = false;

        float frameDt = Mathf.Min(Time.deltaTime, 0.1f);
        accumulator += frameDt;
        int steps = 0;
        while (accumulator >= fixedStep && steps < maxSubSteps)
        {
            CarInput raw = playbackInputs
                ? (playbackIndex < recorded.Count ? recorded[playbackIndex++] : new CarInput())
                : (input != null ? input.Sample() : new CarInput());
            if (recordInputs) recorded.Add(raw);
            StepPhysics(fixedStep, raw);
            accumulator -= fixedStep;
            steps++;
        }
        if (steps == maxSubSteps) accumulator = 0f; // avoid spiral of death

        SyncVisuals();
    }

    // ---------------- core deterministic step ----------------
    void StepPhysics(float dt, CarInput raw)
    {
        if (raw.reset) { ResetToSpawn(); return; }

        Vector3 forward = orientation * Vector3.forward;
        Vector3 up = orientation * Vector3.up;
        float vForward = Vector3.Dot(velocity, forward);

        // remap throttle/brake for reverse, update gear state
        CarInput cmd = Remap(raw, vForward, dt);
        lastThrottle = cmd.throttle; lastBrake = cmd.brake;

        // transmission (forward gears)
        if (!inReverse) gearIndex = ChooseGear(vForward);
        float gearRatio = inReverse ? engine.gearRatios[0] : engine.gearRatios[gearIndex];
        rpm = engine.RpmFromWheelSpeed(vForward, gearRatio);
        float engineTorque = engine.TorqueAt(rpm) * cmd.throttle;

        // smoothed steering
        float targetSteer = cmd.steer * maxSteerAngle * Mathf.Deg2Rad;
        steerAngleCurrent = Mathf.Lerp(steerAngleCurrent, targetSteer, Mathf.Clamp01(dt * 8f));

        // ---- suspension base forces (spring + damper + bump stop) ----
        float[] baseForce = new float[4];
        WheelStep[] ws = new WheelStep[4];
        Vector3 comWorld = position + (orientation * rig.centerOfMassOffset);

        for (int c = 0; c < 4; c++)
        {
            bool isFront = c < 2;
            bool isLeft = (c % 2 == 0);
            int side = isLeft ? -1 : 1;
            SuspensionParams sp = isFront ? frontSusp : rearSusp;
            float trackW = isFront ? rig.frontTrackWidth : rig.rearTrackWidth;
            float axleZ = isFront ? rig.frontWheelBaseZ : rig.rearWheelBaseZ;
            float radius = isFront ? rig.frontWheelRadius : rig.rearWheelRadius;

            // Inboard inner mounts (local) for this corner.
            Vector3 iLF = isLeft ? (isFront ? rig.fLowerLeftFrontMount : rig.rLowerLeftFrontMount)
                                  : (isFront ? rig.fLowerRightFrontMount : rig.rLowerRightFrontMount);
            Vector3 iLR = isLeft ? (isFront ? rig.fLowerLeftRearMount : rig.rLowerLeftRearMount)
                                  : (isFront ? rig.fLowerRightRearMount : rig.rLowerRightRearMount);

            // Outer ball joints sit OUTBOARD at the wheel (track width). The A-arm
            // links them to the inboard inner mounts; the spring perch is tight & inboard.
            Vector3 lowerBallLocal = new Vector3(side * trackW * 0.5f, iLF.y, axleZ);
            Vector3 springTopLocal = new Vector3(side * trackW * 0.5f, springTopLocalY, axleZ);

            Vector3 pivotFrontW = position + orientation * iLF;
            Vector3 pivotRearW  = position + orientation * iLR;
            Vector3 lowerBallDesignW = position + orientation * lowerBallLocal;

            // Short raycast straight down only to locate the ground surface.
            Vector3 aboveWheel = lowerBallDesignW + up * 0.6f;
            Ray ray = new Ray(aboveWheel, -up);
            float maxDist = radius + sp.maxTravel + 0.8f;
            bool hit = Physics.Raycast(ray, out RaycastHit hitInfo, maxDist, groundMask);

            WheelStep w = new WheelStep();
            w.radius = radius; w.grounded = false;

            if (hit)
            {
                Vector3 contact = hitInfo.point;
                Vector3 wheelCenterGround = contact + up * radius;
                Vector3 springTopW = position + orientation * springTopLocal;
                Vector3 attachW = wheelCenterGround - up * 0.175f; // lower spring attach ~ lower ball
                float springLen = Vector3.Distance(springTopW, attachW);

                // Tight coilover compression, clamped to +/- maxTravel (10 cm).
                float comp = Mathf.Clamp(springFreeLength - springLen, -sp.maxTravel, sp.maxTravel);
                float compV = (comp - compression[c]) / dt;
                float fSpring = sp.cornerSpringK * comp;
                float fBump = comp > sp.bumpStopStart ? sp.bumpStopK * (comp - sp.bumpStopStart) : 0f;
                float spd = Mathf.Abs(compV);
                float tt = Mathf.Clamp01(spd / Mathf.Max(sp.highSpeedThreshold, 1e-3f));
                float damp = compV > 0f ? Mathf.Lerp(sp.bumpLow, sp.bumpHigh, tt)
                                        : Mathf.Lerp(sp.reboundLow, sp.reboundHigh, tt);
                float fDamp = -damp * compV;
                baseForce[c] = fSpring + fBump + fDamp;
                compression[c] = comp; compressionVel[c] = compV;

                // Rigid wishbone arc: rotate the lower ball joint about the inner
                // pivot axis (A-arm sweep) so the upright swings in an arc.
                Vector3 axis = (pivotRearW - pivotFrontW).normalized;
                Vector3 toBall = lowerBallDesignW - pivotFrontW;
                float along = Vector3.Dot(toBall, axis);
                Vector3 perp = toBall - axis * along;
                float armR = perp.magnitude;
                float theta = Mathf.Asin(Mathf.Clamp(comp / (side * armR), -1f, 1f));
                Vector3 rotated = Quaternion.AngleAxis(theta, axis) * perp;
                Vector3 lowerBallW = pivotFrontW + axis * along + rotated;
                w.center = lowerBallW + up * 0.175f;
                w.contact = contact;
                w.grounded = true;
            }
            else
            {
                // Airborne: full droop, no spring force.
                float comp = Mathf.MoveTowards(compression[c], -sp.maxTravel, 0.5f * dt);
                compression[c] = comp; compressionVel[c] = 0f;
                baseForce[c] = 0f;
                w.center = lowerBallDesignW + up * 0.175f;
                w.contact = lowerBallDesignW + up * (0.175f - radius);
            }
            ws[c] = w;
        }

        // ---- ARB + heave (per axle, needs both sides) ----
        float[] fz = new float[4];
        for (int axle = 0; axle < 2; axle++)
        {
            int l = axle * 2;     // FL / RL
            int r = axle * 2 + 1; // FR / RR
            SuspensionParams sp = axle == 0 ? frontSusp : rearSusp;
            bool isFront = axle == 0;
            float arb = sp.arbStiffness * (compression[l] - compression[r]);
            float avg = (compression[l] + compression[r]) * 0.5f;
            float avgV = (compressionVel[l] + compressionVel[r]) * 0.5f;
            float heave = sp.heaveK * avg + sp.heaveDamping * avgV;
            baseForce[l] += arb + heave;
            baseForce[r] += -arb + heave;

            fz[l] = Mathf.Max(baseForce[l], 0f);
            fz[r] = Mathf.Max(baseForce[r], 0f);
        }

        // ---- accumulate body forces / torque + wheel spin ----
        Vector3 force = Vector3.down * mass * VehicleMath.Gravity;
        Vector3 torque = Vector3.zero;
        Vector3 omegaWorld = angularVelocity;

        for (int c = 0; c < 4; c++)
        {
            bool isFront = c < 2;
            bool isLeft = (c % 2 == 0);
            TyreParams tp = isFront ? frontTyre : rearTyre;
            SuspensionParams sp = isFront ? frontSusp : rearSusp;
            WheelStep w = ws[c];

            // wheel alignment (steer / camber / toe)
            float steerRad = isFront ? SteerForWheel(c) : 0f;
            float camberDeg = (isFront ? rig.frontCamber : rig.rearCamber) + compression[c] * camberGainPerMeter;
            float toeDeg = isFront ? rig.frontToe : rig.rearToe;
            float camberSign = isLeft ? 1f : -1f;
            float toeSign = isLeft ? 1f : -1f;
            Quaternion align = Quaternion.Euler(camberDeg * camberSign, (steerRad * Mathf.Rad2Deg) + toeDeg * toeSign, 0f);
            w.steerRad = steerRad; w.align = align;

            Vector3 wheelFwd = (orientation * align) * Vector3.forward;
            Vector3 wheelRight = (orientation * align) * Vector3.right;
            Vector3 wheelUp = (orientation * align) * Vector3.up;

            Vector3 r = w.contact - comWorld;
            Vector3 vContact = velocity + Vector3.Cross(omegaWorld, r);

            float vLong = Vector3.Dot(vContact, wheelFwd);
            float vLat = Vector3.Dot(vContact, wheelRight);

            float Fx = 0f, Fy = 0f;
            if (w.grounded && fz[c] > 1f)
            {
                // Clamp slip: a wheel that free-spun in the air would otherwise produce a
                // massive instantaneous force on touchdown and launch the car.
                float slipRatio = Mathf.Clamp((wheelOmega[c] * w.radius - vLong) / Mathf.Max(Mathf.Abs(vLong), 1f), -1.5f, 1.5f);
                float slipAngle = Mathf.Atan2(-vLat, Mathf.Abs(vLong) + 1e-3f);
                Fx = VehicleMath.MagicFormula(slipRatio, tp.longB, tp.longC, tp.muX * fz[c], tp.longE);
                Fy = VehicleMath.MagicFormula(slipAngle, tp.latB, tp.latC, tp.muY * fz[c], tp.latE);
                float camberRad = camberDeg * camberSign * Mathf.Deg2Rad;
                Fy += -camberRad * fz[c] * tp.camberThrust;
                // friction circle
                float mag = Mathf.Sqrt(Fx * Fx + Fy * Fy);
                float limit = tp.muY * fz[c];
                if (mag > limit && mag > 1e-3f) { float s = limit / mag; Fx *= s; Fy *= s; }
            }

            // suspension vertical + tyre forces at contact
            Vector3 wheelForce = up * fz[c] + wheelFwd * Fx + wheelRight * Fy;
            force += wheelForce;
            torque += Vector3.Cross(r, wheelForce);

            // wheel spin (driven wheels get engine torque)
            bool driven = rearWheelDrive ? !isFront : isFront;
            float driveTorque = 0f;
            if (driven) driveTorque = engineTorque * gearRatio * engine.finalDrive * engine.drivetrainEfficiency;
            float brakeT = cmd.brake * maxBrakeTorque * Mathf.Sign(wheelOmega[c] + 1e-4f);
            float dOmega = (driveTorque - Fx * w.radius - brakeT) / tp.wheelInertia;
            wheelOmega[c] += dOmega * dt;
            // simple free-wheel bearing drag when airborne; clamp spin so touchdown slip is bounded
            if (!w.grounded)
            {
                wheelOmega[c] *= (1f - Mathf.Min(1f, dt * 0.5f));
                wheelOmega[c] = Mathf.Clamp(wheelOmega[c], -250f, 250f);
            }

            ws[c] = w;
        }

        // aero drag + rolling resistance (caps top speed)
        float vMag = velocity.magnitude;
        if (vMag > 0.1f)
        {
            float drag = 0.5f * engine.airDensity * engine.dragCoefficient * engine.frontalArea * vMag * vMag;
            float roll = engine.rollingResistance * mass * VehicleMath.Gravity;
            force += -velocity.normalized * (drag + roll);
        }

        // ---- integrate body ----
        Vector3 accel = force / mass;
        velocity += accel * dt;
        position += velocity * dt;

        // torque -> angular accel in body frame
        Quaternion invOri = Quaternion.Inverse(orientation);
        Vector3 torqueBody = invOri * torque;
        Vector3 alphaBody = new Vector3(torqueBody.x * invInertia.x, torqueBody.y * invInertia.y, torqueBody.z * invInertia.z);
        Vector3 alphaWorld = orientation * alphaBody;
        angularVelocity += alphaWorld * dt;

        Vector3 bodyOmega = invOri * angularVelocity;
        orientation = VehicleMath.Integrate(orientation, bodyOmega, dt);

        speedKph = Mathf.Abs(Vector3.Dot(velocity, orientation * Vector3.forward)) * 3.6f;

        // Safety net: if the car ever ends up far below the ground (e.g. raycast
        // missed), recover instead of falling forever.
        if (position.y < -5f) { ResetToSpawn(); return; } // true fall-through only

        // store compression for gizmos
        for (int c = 0; c < 4; c++)
            rig.liveCompression[c] = Mathf.Clamp01((compression[c] + frontSusp.maxTravel) / (2f * frontSusp.maxTravel));

        lastWs = ws;
    }

    WheelStep[] lastWs;

    CarInput Remap(CarInput raw, float vForward, float dt)
    {
        CarInput cmd = raw;
        bool accel = raw.throttle > 0.5f;
        bool brake = raw.brake > 0.5f;
        if (!inReverse)
        {
            cmd.throttle = accel ? 1f : 0f;
            cmd.brake = brake ? 1f : 0f;
            if (brake && !accel && Mathf.Abs(vForward) < 0.5f) reverseTimer += dt; else reverseTimer = 0f;
            if (reverseTimer > 0.2f) { inReverse = true; reverseTimer = 0f; }
        }
        else
        {
            // reverse: S = throttle (backward), W = brake
            cmd.throttle = brake ? 1f : 0f;
            cmd.brake = accel ? 1f : 0f;
            if (accel && Mathf.Abs(vForward) < 0.5f) { inReverse = false; reverseTimer = 0f; }
        }
        return cmd;
    }

    int ChooseGear(float vForward)
    {
        if (Mathf.Abs(vForward) < 1f) return 1;
        int best = 1;
        for (int g = engine.gearRatios.Length - 1; g >= 1; g--)
        {
            if (engine.RpmFromWheelSpeed(vForward, engine.gearRatios[g]) >= 1200f) { best = g; break; }
        }
        return best;
    }

    // Anti/ackermann per front wheel.
    float SteerForWheel(int corner)
    {
        if (corner >= 2) return 0f;
        bool isLeft = (corner % 2 == 0);
        float baseS = steerAngleCurrent;
        if (Mathf.Abs(baseS) < 1e-4f) return baseS;
        float L = Mathf.Abs(rig.frontWheelBaseZ - rig.rearWheelBaseZ);
        float T = rig.frontTrackWidth;
        float R = L / Mathf.Tan(Mathf.Abs(baseS));
        float aInner = Mathf.Atan(L / Mathf.Max(R - 0.5f * T, 0.2f));
        float aOuter = Mathf.Atan(L / (R + 0.5f * T));
        float aL = baseS > 0f ? aInner : aOuter;
        float aR = baseS > 0f ? aOuter : aInner;
        float sL = Mathf.Lerp(baseS, aL, ackermann);
        float sR = Mathf.Lerp(baseS, aR, ackermann);
        return isLeft ? sL : sR;
    }

    // ---------------- visuals ----------------
    void SyncVisuals()
    {
        transform.position = position;
        transform.rotation = orientation;
        if (lastWs == null) return;
        for (int c = 0; c < 4; c++)
        {
            Transform wt = rig.transform.Find(rig.wheelNames[c]);
            if (wt == null) continue;
            bool isFront = c < 2;
            float radius = isFront ? rig.frontWheelRadius : rig.rearWheelRadius;
            float width = isFront ? rig.frontWheelWidth : rig.rearWheelWidth;
            Vector3 center = lastWs[c].center;
            wt.position = center;
            Quaternion cylinderFix = Quaternion.Euler(0f, 0f, 90f);
            wt.rotation = orientation * lastWs[c].align * cylinderFix;
            wt.localScale = new Vector3(radius * 2f, width * 0.5f, radius * 2f);
        }
    }

    // ---------------- HUD accessors ----------------
    public float ThrottleDisplay { get { return lastThrottle; } }
    public float BrakeDisplay { get { return lastBrake; } }
    public float SpeedKph { get { return speedKph; } }
    public float Rpm { get { return rpm; } }
    public float MaxRpm { get { return engine.maxRpm; } }
    public float Redline { get { return engine.redlineRpm; } }
    public float TopSpeedKph { get { return engine.TopSpeedKph; } }
    public float Horsepower { get { return engine.maxPowerHp; } }
    public string GearLabel { get { return inReverse ? "R" : gearIndex.ToString(); } }
    public float EngineTorqueNm { get { return engine.TorqueAt(rpm); } }

    float lastThrottle = 0f, lastBrake = 0f;
}
