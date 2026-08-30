using UnityEngine;

/// <summary>
/// On-screen telemetry HUD: throttle gauge, brake gauge, speed (kph),
/// RPM and current gear. Pure IMGUI, no assets required.
/// </summary>
public class TelemetryHUD : MonoBehaviour
{
    public CarController car;

    GUIStyle labelStyle;
    GUIStyle bigStyle;
    Texture2D fillTex;

    void Awake()
    {
        if (car == null) car = FindObjectOfType<CarController>();
        labelStyle = new GUIStyle() { normal = { textColor = Color.white }, fontSize = 16 };
        bigStyle = new GUIStyle() { normal = { textColor = Color.white }, fontSize = 28, fontStyle = FontStyle.Bold };
        fillTex = new Texture2D(1, 1);
        fillTex.SetPixel(0, 0, Color.white);
        fillTex.Apply();
    }

    void OnGUI()
    {
        if (car == null) return;
        float pad = 16f;
        float w = 240f, h = 200f;
        Rect box = new Rect(pad, Screen.height - h - pad, w, h);
        GUI.Box(box, "TELEMETRY");

        float x = box.x + 12f;
        float y = box.y + 26f;
        float rowH = 26f;

        GUI.Label(new Rect(x, y, w - 24, rowH), $"SPEED   {car.SpeedKph,6:F1} kph", bigStyle); y += rowH + 4;
        GUI.Label(new Rect(x, y, w - 24, rowH), $"RPM     {car.Rpm,7:F0}", labelStyle); y += rowH;
        GUI.Label(new Rect(x, y, w - 24, rowH), $"GEAR    {car.GearLabel}", labelStyle); y += rowH;
        GUI.Label(new Rect(x, y, w - 24, rowH), $"POWER   {car.Horsepower,5:F0} hp   TOP {car.TopSpeedKph,5:F0} kph", labelStyle); y += rowH + 4;

        // Throttle gauge
        DrawBar(new Rect(x, y, w - 24, 14), car.ThrottleDisplay, Color.green, "THROTTLE"); y += 22;
        // Brake gauge
        DrawBar(new Rect(x, y, w - 24, 14), car.BrakeDisplay, Color.red, "BRAKE");

        // RPM redline hint
        float rpmFrac = Mathf.Clamp01(car.Rpm / car.MaxRpm);
        GUI.Label(new Rect(box.x + box.width - 90, box.y + 26, 80, rowH),
            car.Rpm > car.Redline ? "REDLINE" : "", new GUIStyle() { normal = { textColor = Color.red }, fontSize = 14 });
    }

    void DrawBar(Rect r, float value, Color col, string name)
    {
        GUI.Label(new Rect(r.x, r.y - 16, r.width, 14), name, labelStyle);
        GUI.Box(new Rect(r.x, r.y, r.width, r.height), "");
        Color prev = GUI.color;
        GUI.color = col;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(value), r.height), fillTex);
        GUI.color = prev;
    }
}
