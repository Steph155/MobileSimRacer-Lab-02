using UnityEngine;

// ---------------------------------------------------------------------------
// Shared deterministic types for the RigRacer Level-0 prototype.
// Everything here is pure data / math so the simulation is frame-rate
// independent: given the same state + the same input list, the outcome is
// identical regardless of how many render frames occurred.
// ---------------------------------------------------------------------------

/// <summary>One sampled control frame. Held constant across a fixed physics step.</summary>
public struct CarInput
{
    public float throttle; // 0..1
    public float brake;    // 0..1
    public float steer;    // -1..1 (left positive)
    public bool reset;     // R key
}

public static class VehicleMath
{
    public const float Gravity = 9.81f;

    /// <summary>Simplified Magic Formula (Pacejka) tyre curve.</summary>
    public static float MagicFormula(float x, float B, float C, float D, float E)
    {
        float bx = B * x;
        return D * Mathf.Sin(C * Mathf.Atan(bx - E * (bx - Mathf.Atan(bx))));
    }

    /// <summary>Integrate a quaternion by a body-frame angular velocity (explicit, normalized).</summary>
    public static Quaternion Integrate(Quaternion q, Vector3 omegaBody, float dt)
    {
        Quaternion w = new Quaternion(omegaBody.x, omegaBody.y, omegaBody.z, 0f);
        Quaternion qDot = w * q; // q * (0, omega)
        q.x += 0.5f * qDot.x * dt;
        q.y += 0.5f * qDot.y * dt;
        q.z += 0.5f * qDot.z * dt;
        q.w += 0.5f * qDot.w * dt;
        return q.normalized;
    }
}

// ---------------------------------------------------------------------------
// ICE engine: torque curve, RPM, gearbox, horsepower / top speed.
// ---------------------------------------------------------------------------
[System.Serializable]
public class VehicleEngine
{
    [Header("ICE")]
    public float maxPowerHp = 300f;
    public float idleRpm = 900f;
    public float maxRpm = 7200f;
    public float redlineRpm = 7000f;

    [Tooltip("rpm -> torque (Nm). Built from defaults if empty.")]
    public AnimationCurve torqueCurve = new AnimationCurve();

    [Tooltip("index 0 = reverse, then 1st..Nth")]
    public float[] gearRatios = { -3.2f, 3.6f, 2.5f, 1.9f, 1.5f, 1.2f, 0.95f };
    public float finalDrive = 3.65f;
    public float drivetrainEfficiency = 0.92f;
    public float wheelRadius = 0.3525f;

    [Header("Aero / Drag (for top speed estimate)")]
    public float dragCoefficient = 0.32f;
    public float frontalArea = 2.0f;
    public float airDensity = 1.225f;
    public float rollingResistance = 0.015f;

    public float TopSpeedKph
    {
        get
        {
            // v = sqrt( (2 * P) / (rho * Cd * A) )
            float p = maxPowerHp * 745.7f;
            float v = Mathf.Sqrt((2f * p) / (airDensity * dragCoefficient * frontalArea));
            return v * 3.6f;
        }
    }

    public void InitDefaults()
    {
        if (torqueCurve == null || torqueCurve.length == 0)
        {
            torqueCurve = new AnimationCurve();
            torqueCurve.AddKey(800f, 130f);
            torqueCurve.AddKey(2500f, 235f);
            torqueCurve.AddKey(4500f, 265f);
            torqueCurve.AddKey(6000f, 255f);
            torqueCurve.AddKey(7000f, 210f);
            torqueCurve.AddKey(7200f, 170f);
        }
        // Derive displayed horsepower from the curve peak (P = T * w).
        float peak = 0f;
        for (float r = idleRpm; r <= maxRpm; r += 100f)
        {
            float t = torqueCurve.Evaluate(r);
            float w = r * 2f * Mathf.PI / 60f;
            peak = Mathf.Max(peak, t * w);
        }
        maxPowerHp = peak / 745.7f;
    }

    public float TorqueAt(float rpm)
    {
        rpm = Mathf.Clamp(rpm, 0f, maxRpm);
        return torqueCurve.Evaluate(rpm);
    }

    /// <summary>Engine RPM from wheel surface speed (m/s, signed for reverse).</summary>
    public float RpmFromWheelSpeed(float wheelSpeed, float gearRatio)
    {
        if (Mathf.Abs(gearRatio) < 1e-4f) return idleRpm;
        float engineOmega = (wheelSpeed / wheelRadius) * gearRatio * finalDrive;
        float rpm = engineOmega * 60f / (2f * Mathf.PI);
        return Mathf.Clamp(rpm, idleRpm, maxRpm);
    }
}

// ---------------------------------------------------------------------------
// Tyre slip model parameters (per axle, front/rear can differ).
// ---------------------------------------------------------------------------
[System.Serializable]
public class TyreParams
{
    [Header("Friction")]
    public float muX = 1.6f;   // longitudinal peak friction
    public float muY = 1.7f;   // lateral peak friction
    public float camberThrust = 0.12f; // lateral N per N of load per radian of camber

    [Header("Magic Formula shape")]
    public float longB = 11f; public float longC = 1.55f; public float longE = 0.97f;
    public float latB = 9f;   public float latC = 1.4f;  public float latE = 0.95f;

    [Header("Wheel inertia (spin)")]
    public float wheelInertia = 0.9f;
}

// ---------------------------------------------------------------------------
// Suspension: corner spring, 4-way damper, bump stops, ARB and heave spring.
// All per axle (front / rear).
// ---------------------------------------------------------------------------
[System.Serializable]
public class SuspensionParams
{
    [Header("Geometry")]
    public float restLength = 0.50f;     // anchor -> contact at design ride height
    public float maxTravel = 0.10f;      // ~10 cm total vertical travel

    [Header("Corner spring")]
    public float cornerSpringK = 70000f; // N/m

    [Header("4-way damper (N per m/s) - near-critical for stability")]
    public float bumpLow = 7000f;
    public float bumpHigh = 12000f;
    public float reboundLow = 7000f;
    public float reboundHigh = 12000f;
    public float highSpeedThreshold = 0.12f; // m/s separates low/high speed

    [Header("Bump stops (progressive, soft enough not to kick")]
    public float bumpStopStart = 0.07f;   // compression (m) where stop engages
    public float bumpStopK = 150000f;     // extra N/m once engaged

    [Header("Anti-roll bar")]
    public float arbStiffness = 22000f;   // N/m of roll-induced deflection

    [Header("Heave spring (3rd spring on axle)")]
    public float heaveK = 18000f;
    public float heaveDamping = 5000f;
}
