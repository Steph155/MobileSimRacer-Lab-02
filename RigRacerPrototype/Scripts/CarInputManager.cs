using UnityEngine;

/// <summary>
/// Reads raw keyboard input. Supports both QWERTY (WASD) and AZERTY (ZQSD).
/// The controller decides throttle/brake remapping for reverse.
/// </summary>
public class CarInputManager : MonoBehaviour
{
    [Header("Keys")]
    public KeyCode resetKey = KeyCode.R;

    public bool Accelerate { get { return Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.Z); } }
    public bool BrakeKey { get { return Input.GetKey(KeyCode.S); } }
    public bool SteerLeft { get { return Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.Q); } }
    public bool SteerRight { get { return Input.GetKey(KeyCode.D); } }
    public bool ResetPressed { get { return Input.GetKeyDown(resetKey); } }

    public CarInput Sample()
    {
        CarInput i = new CarInput();
        i.throttle = Accelerate ? 1f : 0f;
        i.brake = BrakeKey ? 1f : 0f;
        i.steer = (SteerLeft ? 1f : 0f) - (SteerRight ? 1f : 0f);
        i.reset = ResetPressed;
        return i;
    }
}
