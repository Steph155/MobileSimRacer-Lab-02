using UnityEngine;

[ExecuteAlways]
public class CarVisualRig : MonoBehaviour
{
    [Header("Chassis Geometry")]
    public Vector3 chassisSize = new Vector3(1.4f, 0.5f, 3.8f);
    public Vector3 centerOfMassOffset = new Vector3(0f, -0.22f, 0f);

    [Header("Inspector Symmetry Control")]
    public bool symmetricEditing = true;

    [Header("16 Chassis Mounts - Front Axle (Left)")]
    public Vector3 fUpperLeftFrontMount = new Vector3(-0.3f, 0.15f, 0.6f);
    public Vector3 fUpperLeftRearMount = new Vector3(-0.3f, 0.15f, 0.2f);
    public Vector3 fLowerLeftFrontMount = new Vector3(-0.4f, -0.2f, 0.7f);
    public Vector3 fLowerLeftRearMount = new Vector3(-0.4f, -0.2f, 0.1f);

    [Header("16 Chassis Mounts - Front Axle (Right)")]
    public Vector3 fUpperRightFrontMount = new Vector3(0.3f, 0.15f, 0.6f);
    public Vector3 fUpperRightRearMount = new Vector3(0.3f, 0.15f, 0.2f);
    public Vector3 fLowerRightFrontMount = new Vector3(0.4f, -0.2f, 0.7f);
    public Vector3 fLowerRightRearMount = new Vector3(0.4f, -0.2f, 0.1f);

    [Header("16 Chassis Mounts - Rear Axle (Left)")]
    public Vector3 rUpperLeftFrontMount = new Vector3(-0.3f, 0.15f, -0.2f);
    public Vector3 rUpperLeftRearMount = new Vector3(-0.3f, 0.15f, -0.6f);
    public Vector3 rLowerLeftFrontMount = new Vector3(-0.4f, -0.2f, -0.1f);
    public Vector3 rLowerLeftRearMount = new Vector3(-0.4f, -0.2f, -0.7f);

    [Header("16 Chassis Mounts - Rear Axle (Right)")]
    public Vector3 rUpperRightFrontMount = new Vector3(0.3f, 0.15f, -0.2f);
    public Vector3 rUpperRightRearMount = new Vector3(0.3f, 0.15f, -0.6f);
    public Vector3 rLowerRightFrontMount = new Vector3(0.4f, -0.2f, -0.1f);
    public Vector3 rLowerRightRearMount = new Vector3(0.4f, -0.2f, -0.7f);

    [Header("Tyre & Wheel Configuration")]
    public float frontWheelRadius = 0.3525f;
    public float frontWheelWidth = 0.28f;
    public float rearWheelRadius = 0.355f;
    public float rearWheelWidth = 0.375f;
    [Range(0.05f, 1f)] public float wheelOpacity = 0.4f;

    [Header("Ride Heights & Positioning")]
    [Tooltip("Reference resting offset. Real ride height is solved by the suspension.")]
    public float frontRideHeight = -0.2f;
    public float rearRideHeight = -0.2f;
    public float frontTrackWidth = 1.9f;
    public float rearTrackWidth = 1.85f;
    public float frontWheelBaseZ = 1.8f;
    public float rearWheelBaseZ = -1.8f;

    [Header("Alignment (Camber & Toe in Degrees)")]
    [Range(-10f, 10f)] public float frontCamber = -2.5f;
    [Range(-5f, 5f)] public float frontToe = 0.1f;
    [Range(-10f, 10f)] public float rearCamber = -1.5f;
    [Range(-5f, 5f)] public float rearToe = 0.2f;

    [Header("Editor")]
    [Tooltip("When a CarController is present it drives the wheels; otherwise this rig is static.")]
    public bool drivenByController = false;
    [Range(0f, 1f)] public float suspensionPreview = 0.5f; // editor arc preview position

    public readonly string[] wheelNames = { "W_FL", "W_FR", "W_RL", "W_RR" };

    // Live compression (0=droop, 1=bump) fed by the controller for arc visualization.
    public float[] liveCompression = new float[4];

    public struct CornerGeometry
    {
        public Vector3 innerUpperFront, innerUpperRear;
        public Vector3 innerLowerFront, innerLowerRear;
        public Vector3 outerUpper, outerLower;
        public Vector3 upperPivotAxis; // unit, innerUpperFront->innerUpperRear
        public Vector3 lowerPivotAxis; // unit, innerLowerFront->innerLowerRear
    }

    void Awake()
    {
        InitializeWheels();
    }

    void Update()
    {
        if (!drivenByController) UpdateWheelTransforms();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (symmetricEditing)
        {
            fUpperRightFrontMount = new Vector3(-fUpperLeftFrontMount.x, fUpperLeftFrontMount.y, fUpperLeftFrontMount.z);
            fUpperRightRearMount = new Vector3(-fUpperLeftRearMount.x, fUpperLeftRearMount.y, fUpperLeftRearMount.z);
            fLowerRightFrontMount = new Vector3(-fLowerLeftFrontMount.x, fLowerLeftFrontMount.y, fLowerLeftFrontMount.z);
            fLowerRightRearMount = new Vector3(-fLowerLeftRearMount.x, fLowerLeftRearMount.y, fLowerLeftRearMount.z);
            rUpperRightFrontMount = new Vector3(-rUpperLeftFrontMount.x, rUpperLeftFrontMount.y, rUpperLeftFrontMount.z);
            rUpperRightRearMount = new Vector3(-rUpperLeftRearMount.x, rUpperLeftRearMount.y, rUpperLeftRearMount.z);
            rLowerRightFrontMount = new Vector3(-rLowerLeftFrontMount.x, rLowerLeftFrontMount.y, rLowerLeftFrontMount.z);
            rLowerRightRearMount = new Vector3(-rLowerLeftRearMount.x, rLowerLeftRearMount.y, rLowerLeftRearMount.z);
        }
        UpdateWheelTransparency();
    }
#endif

    void InitializeWheels()
    {
        foreach (string n in wheelNames)
        {
            Transform t = transform.Find(n);
            if (t == null)
            {
                GameObject cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                cyl.name = n;
                cyl.transform.SetParent(transform);
                t = cyl.transform;
            }
            Collider col = t.GetComponent<Collider>();
            if (col != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying) DestroyImmediate(col);
                else Destroy(col);
#else
                Destroy(col);
#endif
            }
        }
        UpdateWheelTransparency();
    }

    void UpdateWheelTransparency()
    {
        foreach (string n in wheelNames)
        {
            Transform t = transform.Find(n);
            if (t == null) continue;
            Renderer rend = t.GetComponent<Renderer>();
            if (rend == null) continue;
            Material mat = rend.sharedMaterial;
            if (mat == null) continue;
            mat.SetFloat("_Mode", 3);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
            Color c = mat.color;
            c.a = wheelOpacity;
            mat.color = c;
        }
    }

    public void UpdateWheelTransforms()
    {
        for (int i = 0; i < 4; i++)
        {
            Transform wheelT = transform.Find(wheelNames[i]);
            if (wheelT == null) continue;
            bool isFront = i < 2;
            bool isLeft = (i % 2 == 0);
            float track = isFront ? frontTrackWidth : rearTrackWidth;
            float radius = isFront ? frontWheelRadius : rearWheelRadius;
            float width = isFront ? frontWheelWidth : rearWheelWidth;
            float camber = isFront ? frontCamber : rearCamber;
            float toe = isFront ? frontToe : rearToe;
            float rh = isFront ? frontRideHeight : rearRideHeight;
            float halfTrack = track * 0.5f;
            float xPos = isLeft ? -halfTrack : halfTrack;
            float zPos = isFront ? frontWheelBaseZ : rearWheelBaseZ;
            Vector3 localPos = new Vector3(xPos, rh, zPos);
            wheelT.position = transform.TransformPoint(localPos);
            Quaternion baseRotation = transform.rotation;
            Quaternion alignmentRot = Quaternion.Euler(0f, toe * (isLeft ? 1f : -1f), camber * (isLeft ? 1f : -1f));
            Quaternion cylinderFix = Quaternion.Euler(0f, 0f, 90f);
            wheelT.rotation = baseRotation * alignmentRot * cylinderFix;
            wheelT.localScale = new Vector3(radius * 2f, width * 0.5f, radius * 2f);
        }
    }

    /// <summary>World-space wishbone geometry for one corner (used by physics + gizmos).</summary>
    public CornerGeometry GetCornerGeometry(int corner)
    {
        bool isFront = corner < 2;
        bool isLeft = (corner % 2 == 0);
        Vector3 uF, uR, lF, lR;
        if (isFront)
        {
            uF = isLeft ? fUpperLeftFrontMount : fUpperRightFrontMount;
            uR = isLeft ? fUpperLeftRearMount : fUpperRightRearMount;
            lF = isLeft ? fLowerLeftFrontMount : fLowerRightFrontMount;
            lR = isLeft ? fLowerLeftRearMount : fLowerRightRearMount;
        }
        else
        {
            uF = isLeft ? rUpperLeftFrontMount : rUpperRightFrontMount;
            uR = isLeft ? rUpperLeftRearMount : rUpperRightRearMount;
            lF = isLeft ? rLowerLeftFrontMount : rLowerRightFrontMount;
            lR = isLeft ? rLowerLeftRearMount : rLowerRightRearMount;
        }
        Vector3 wUF = transform.TransformPoint(uF);
        Vector3 wUR = transform.TransformPoint(uR);
        Vector3 wLF = transform.TransformPoint(lF);
        Vector3 wLR = transform.TransformPoint(lR);
        CornerGeometry g;
        g.innerUpperFront = wUF; g.innerUpperRear = wUR;
        g.innerLowerFront = wLF; g.innerLowerRear = wLR;
        // Outer ball joint approximated as midpoint of the two arms' free ends.
        g.outerUpper = (wUF + wUR) * 0.5f;
        g.outerLower = (wLF + wLR) * 0.5f;
        g.upperPivotAxis = (wUR - wUF).normalized;
        g.lowerPivotAxis = (wLR - wLF).normalized;
        return g;
    }

    private void OnDrawGizmos()
    {
        Matrix4x4 defaultMatrix = Gizmos.matrix;
        Matrix4x4 chassisMatrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        Gizmos.matrix = chassisMatrix;
        Gizmos.color = new Color(0.1f, 0.4f, 0.8f, 0.25f);
        Gizmos.DrawCube(Vector3.zero, chassisSize);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(Vector3.zero, chassisSize);
        Gizmos.matrix = defaultMatrix;

        Gizmos.color = Color.magenta;
        Gizmos.DrawSphere(transform.TransformPoint(centerOfMassOffset), 0.05f);

        for (int i = 0; i < 4; i++)
        {
            CornerGeometry g = GetCornerGeometry(i);

            // 16 inner mount points (red) - 4 per corner.
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(g.innerUpperFront, 0.02f);
            Gizmos.DrawSphere(g.innerUpperRear, 0.02f);
            Gizmos.DrawSphere(g.innerLowerFront, 0.02f);
            Gizmos.DrawSphere(g.innerLowerRear, 0.02f);

            // Wishbone arms (green) from inner mounts to the outer ball joint.
            Gizmos.color = Color.green;
            Gizmos.DrawLine(g.innerUpperFront, g.outerUpper);
            Gizmos.DrawLine(g.innerUpperRear, g.outerUpper);
            Gizmos.DrawLine(g.innerUpperFront, g.innerUpperRear);
            Gizmos.DrawLine(g.innerLowerFront, g.outerLower);
            Gizmos.DrawLine(g.innerLowerRear, g.outerLower);
            Gizmos.DrawLine(g.innerLowerFront, g.innerLowerRear);

            // 8 outer mount points (yellow) - 2 per corner.
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(g.outerUpper, 0.02f);
            Gizmos.DrawSphere(g.outerLower, 0.02f);

            // Upright link between the two outer joints.
            Gizmos.DrawLine(g.outerUpper, g.outerLower);

            // Arc motion of each double-wishbone arm as the suspension travels.
            float comp = drivenByController ? liveCompression[i] : suspensionPreview;
            DrawWishboneArc(g.innerUpperFront, g.innerUpperRear, g.outerUpper, comp);
            DrawWishboneArc(g.innerLowerFront, g.innerLowerRear, g.outerLower, comp);
        }
    }

    // Draws the circular arc the outer ball joint traces about its inner pivot axis,
    // across the full suspension travel (sweep derived from arm length vs travel).
    private void DrawWishboneArc(Vector3 innerF, Vector3 innerR, Vector3 outer, float comp)
    {
        Vector3 axis = (innerR - innerF).normalized;
        Vector3 toOuter = outer - innerF;
        float armLen = Vector3.Dot(toOuter, axis);
        Vector3 center = innerF + axis * armLen;
        Vector3 radial = (outer - center);
        float radius = radial.magnitude;
        if (radius < 1e-4f) return;
        Vector3 aA = radial.normalized;
        Vector3 aB = Vector3.Cross(axis, aA).normalized;
        float sweep = Mathf.Asin(Mathf.Clamp(0.10f / Mathf.Max(radius, 1e-3f), 0f, 1f)); // ~10cm travel
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.8f);
        int steps = 24;
        Vector3 prev = center + radius * (Mathf.Cos(-sweep) * aA + Mathf.Sin(-sweep) * aB);
        for (int s = 1; s <= steps; s++)
        {
            float t = -sweep + (2f * sweep) * (s / (float)steps);
            Vector3 p = center + radius * (Mathf.Cos(t) * aA + Mathf.Sin(t) * aB);
            Gizmos.DrawLine(prev, p);
            prev = p;
        }
    }
}
