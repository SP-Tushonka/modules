using UnityEngine;

public class BundleUtils : MonoBehaviour
{
    private string[] errorLines;
    private Texture2D bgTexture;
    private bool started;
    private GUIStyle labelStyle;
    private GUIStyle windowStyle;
    private Rect windowRect;

    public static BundleUtils ShowError(string[] lines)
    {
        GameObject bundleUtilsObject = new GameObject("BundleUtilsObject");
        BundleUtils bundleUtils = bundleUtilsObject.AddComponent<BundleUtils>();
        bundleUtils.enabled = true;
        bundleUtils.bgTexture = new Texture2D(2, 2);
        bundleUtils.errorLines = lines;
        bundleUtils.windowRect = bundleUtils.CreateRectangle(700, 60 + (lines.Length * 20));

        // The caller throws straight after this, which means this will never render
        DontDestroyOnLoad(bundleUtilsObject);

        return bundleUtils;
    }

    public void OnGUI()
    {
        if (!started)
        {
            CreateStyles();
        }

        GUI.backgroundColor = Color.black;
        GUI.Window(0, windowRect, DrawWindow, "Bundle Error", windowStyle);
    }

    private void CreateStyles()
    {
        labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
        windowStyle = new GUIStyle(GUI.skin.window) { alignment = TextAnchor.UpperCenter };
        windowStyle.normal.background = bgTexture;
        started = true;
    }

    private Rect CreateRectangle(int width, int height)
    {
        return new Rect((Screen.width / 2) - (width / 2), (Screen.height / 2) - (height / 2), width, height);
    }

    private void DrawWindow(int windowId)
    {
        for (var i = 0; i < errorLines.Length; i++)
        {
            GUI.Label(new Rect(0, 35 + (i * 20), 700, 20), errorLines[i], labelStyle);
        }
    }
}
