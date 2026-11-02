using System.IO;
using PlayGoblin;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace BrokenGame.Editor
{
    /// <summary>
    /// Generates the two sample scenes: Broken (seven planted bugs) and Clean (same level, no bugs).
    /// Run: Unity -batchmode -quit -projectPath samples/broken-game -executeMethod BrokenGame.Editor.BrokenGameBuilder.BuildAll
    /// </summary>
    public static class BrokenGameBuilder
    {
        const float Half = 30f;
        // The trapdoor tile sits in a hole of the floor, close to where the player starts.
        static readonly Rect Hole = new Rect(-12, -12, 8, 8);

        [MenuItem("PlayGoblin/Build broken-game scenes")]
        public static void BuildAll()
        {
            Directory.CreateDirectory("Assets/Scenes");
            Directory.CreateDirectory("Assets/Materials");
            Build("Broken", broken: true);
            Build("Clean", broken: false);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/Scenes/Broken.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/Clean.unity", true)
            };
            AssetDatabase.SaveAssets();
        }

        static void Build(string name, bool broken)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(55, 30, 0);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.AddComponent<Camera>();
            camGo.transform.position = new Vector3(0, 52, 0);
            camGo.transform.rotation = Quaternion.Euler(90, 0, 0);
            if (broken) camGo.AddComponent<CameraBlackout>();

            var floorMat = Mat("Floor", new Color(0.35f, 0.5f, 0.35f));
            Slab("Floor West", -Half, Hole.xMin, -Half, Half, floorMat);
            Slab("Floor East", Hole.xMax, Half, -Half, Half, floorMat);
            Slab("Floor South", Hole.xMin, Hole.xMax, -Half, Hole.yMin, floorMat);
            Slab("Floor North", Hole.xMin, Hole.xMax, Hole.yMax, Half, floorMat);

            var trap = Slab("Trapdoor", Hole.xMin, Hole.xMax, Hole.yMin, Hole.yMax, Mat("Trapdoor", new Color(0.55f, 0.45f, 0.3f)));
            if (broken) trap.AddComponent<Trapdoor>();

            // A long step across the level. The walk grid allows steps up to 0.6 m but the player controller climbs only 0.3 m, so the 0.5 m step traps it; the 0.2 m kerb in the clean scene is fine.
            var step = broken ? 0.5f : 0.2f;
            var ledge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ledge.name = "Ledge";
            ledge.transform.position = new Vector3(12.5f, step / 2f, 0);
            ledge.transform.localScale = new Vector3(35, step, 2);
            ledge.GetComponent<Renderer>().sharedMaterial = Mat("Ledge", new Color(0.6f, 0.6f, 0.65f));

            var decal = GameObject.CreatePrimitive(PrimitiveType.Cube);
            decal.name = broken ? "Missing Material Decal" : "Floor Decal";
            decal.transform.position = new Vector3(-18, 0.05f, 15);
            decal.transform.localScale = new Vector3(10, 0.1f, 10);
            decal.GetComponent<Renderer>().sharedMaterial = broken ? Missing() : Mat("Decal", new Color(0.3f, 0.35f, 0.7f));

            var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Player";
            Object.DestroyImmediate(player.GetComponent<CapsuleCollider>());
            var cc = player.AddComponent<CharacterController>();
            cc.stepOffset = 0.3f;
            player.AddComponent<PlayGoblinPlayer>().moveSpeed = 5f;
            player.transform.position = new Vector3(-20, 1.1f, -20);
            player.SetActive(false); // the Play button of the main menu turns the player on

            // Menu: Options, Play, Quit. Sorted by path, PlayGoblin meets Options first.
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var menu = new GameObject("Menu", typeof(RectTransform));
            menu.transform.SetParent(canvasGo.transform, false);
            var options = new GameObject("OptionsPanel", typeof(RectTransform), typeof(Image));
            options.transform.SetParent(canvasGo.transform, false);
            options.SetActive(false);

            var ctl = canvasGo.AddComponent<MenuController>();
            ctl.menu = menu; ctl.options = options; ctl.player = player;

            Label(menu.transform, "Title", broken ? "TODO: game title" : "Garden Walk", new Vector2(0, 160), 36);
            var optionsBtn = Button(menu.transform, "Options", new Vector2(0, 60));
            var playBtn = Button(menu.transform, "Play", new Vector2(0, 0));
            var quitBtn = Button(menu.transform, "Quit", new Vector2(0, -60));
            if (!broken) UnityEventTools.AddPersistentListener(optionsBtn.onClick, ctl.ShowOptions);
            UnityEventTools.AddPersistentListener(playBtn.onClick, ctl.Play);
            UnityEventTools.AddPersistentListener(quitBtn.onClick, ctl.Quit);

            var hud = new GameObject("DeathHud", typeof(RectTransform));
            hud.transform.SetParent(canvasGo.transform, false);
            var hudLabel = Label(hud.transform, "HudLabel", "", new Vector2(0, -200), 20);
            var hudScript = hud.AddComponent<DeathHud>();
            if (!broken) hudScript.label = hudLabel;
            if (broken) new GameObject("Frame Stutter").AddComponent<FrameStutter>();

            EditorSceneManager.SaveScene(scene, "Assets/Scenes/" + name + ".unity");
        }

        static GameObject Slab(string name, float x0, float x1, float z0, float z1, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = new Vector3((x0 + x1) / 2f, -0.5f, (z0 + z1) / 2f);
            go.transform.localScale = new Vector3(x1 - x0, 1f, z1 - z0);
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        static Material Mat(string name, Color c)
        {
            var path = "Assets/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit")) { color = c };
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        /// <summary>A material whose shader failed to compile renders as flat magenta. Reproduce that look.</summary>
        static Material Missing()
        {
            const string path = "Assets/Materials/Missing.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find("Hidden/InternalErrorShader"));
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Text Label(Transform parent, string name, string text, Vector2 at, int size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).anchoredPosition = at;
            ((RectTransform)go.transform).sizeDelta = new Vector2(400, 50);
            var t = go.GetComponent<Text>();
            t.text = text;
            t.fontSize = size;
            t.alignment = TextAnchor.MiddleCenter;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return t;
        }

        static Button Button(Transform parent, string label, Vector2 at)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).anchoredPosition = at;
            ((RectTransform)go.transform).sizeDelta = new Vector2(200, 40);
            var t = Label(go.transform, "Label", label, Vector2.zero, 20);
            t.color = Color.black;
            return go.GetComponent<Button>();
        }
    }
}
