using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.VFX;

public static class BoardBuilder
{
    static readonly Color[] colors =
    {
        new Color(0.90f, 0.20f, 0.25f), // красный
        new Color(0.20f, 0.55f, 0.95f), // синий
        new Color(0.30f, 0.80f, 0.30f), // зелёный
        new Color(0.98f, 0.80f, 0.15f), // жёлтый
        new Color(0.65f, 0.35f, 0.90f), // фиолетовый
    };

    [MenuItem("Tools/Build Match3 Board")]
    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5.5f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.12f, 0.10f, 0.18f);
        camGo.transform.position = new Vector3(0, 0, -10);

        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        var mats = new Material[colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            var path = $"Assets/Board/Tile_{i}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.color = colors[i];
            EditorUtility.SetDirty(m);
            mats[i] = m;
        }

        var board = new GameObject("Board");
        var rnd = new System.Random(7);
        for (int x = 0; x < 8; x++)
        for (int y = 0; y < 8; y++)
        {
            var tile = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(tile.GetComponent<Collider>());
            tile.name = $"Tile_{x}_{y}";
            tile.transform.SetParent(board.transform);
            tile.transform.position = new Vector3(x - 3.5f, y - 3.5f, 0);
            tile.transform.localScale = Vector3.one * 0.9f;
            tile.GetComponent<Renderer>().sharedMaterial = mats[rnd.Next(mats.Length)];
        }

        var click = board.AddComponent<BoardClick>();
        click.effect = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>("Assets/match3vfx.vfx");

        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Board.unity");
        Debug.Log("Board scene built: Assets/Scenes/Board.unity");
    }

    static Material Mat(string name, Color c)
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        var path = $"Assets/Board/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        m.shader = shader; m.color = c; EditorUtility.SetDirty(m);
        return m;
    }

    [MenuItem("Tools/Build Match3 Demo")]
    public static void BuildDemo()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var bg = new Color(0.12f, 0.10f, 0.18f);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5.5f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = bg;
        camGo.transform.position = new Vector3(0, 0, -10);

        var mats = new Material[colors.Length];
        for (int i = 0; i < colors.Length; i++) mats[i] = Mat($"Tile_{i}", colors[i]);

        var go = new GameObject("Match3Demo");
        var d = go.AddComponent<Match3Demo>();
        d.tileMaterials = mats;
        d.cellA = Mat("CellA", new Color(0.27f, 0.24f, 0.38f));
        d.cellB = Mat("CellB", new Color(0.23f, 0.20f, 0.33f));
        d.boardBg = Mat("BoardBg", new Color(0.17f, 0.14f, 0.25f));
        d.mask = Mat("Mask", bg);
        d.popEffect = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>("Assets/match3vfx.vfx");

        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Match3Demo.unity");
        Debug.Log("Match3 demo built: Assets/Scenes/Match3Demo.unity");
    }
}
