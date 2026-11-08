using System.Collections.Generic;
using UnityEditor;
using DocMe.HideIslands;
using HideL = DocMe.HideIslands.L;
using UnityEngine;

namespace DocMe.EyeInstaller
{
    /// <summary>Guided installer: 1 Avatar, 2 Eyes, 3 Look, 4 Done. Hiding leftover parts lives on the
    /// Done page. Advanced settings stay behind folds so the first screen shows one thing to do.</summary>
    public class EyeInstallerWindow : EditorWindow
    {
        const string PK = "DocMe.EyeInstaller.";
        const string HideKey = "HIDE";

        enum Page { Avatar, Eyes, Look, Done }
        Page page = Page.Avatar;

        GameObject avatar;
        Material material;
        Mesh eyeMesh;                    // null => generated quad
        SkinnedMeshRenderer eyeRenderer; // null => auto (hide on every active body mesh)
        bool hideOriginal = true;
        bool inSocket = true;
        bool deformWithFace = false;
        bool flattenEye = true;
        bool deleteOriginal = false;
        bool sinkIntoHead = true;
        bool dropBackFaces = true;
        int evenEyeShapes = 0;
        bool lockAspect = true;
        float widthFactor = 1.3f;
        float heightFactor = 1.3f;
        float depthOffset = 0f;
        float verticalOffset = 0f;
        float horizontalOffset = 0f;
        float rollOffset = 0f;
        float rollRightOffset = 0f;
        int rollMode = EyeInstallerCore.RollMirror;
        float angleOut = 0f;
        float angleUp = 0f;

        readonly HidePickSession hides = new HidePickSession();
        bool advancedEyes, advancedLook, advancedDone, showDetails, showPresets;

        DocMeEyePreset preset;
        bool previewOn = true;                       // Look page: show the result in the Scene view before installing
        EyeInstallerCore.EyePreview preview;         // live result, nothing saved
        bool previewDirty;
        double previewStamp;
        bool previewSwapped;                         // an existing install is parked while previewing
        readonly List<GameObject> parkedEyes = new List<GameObject>();
        readonly IslandPreview previews = new IslandPreview();
        bool showOriginal = false;
        string pickMode;                 // "L" / "R" / HideKey while clicking in the Scene view, else null
        readonly Dictionary<string, List<IslandPick>> seeds = new Dictionary<string, List<IslandPick>>();

        List<EyeData> detected;
        string detectLog = "";
        string log = "";
        Vector2 scroll;

        [MenuItem("Tools/DocMe/Eye Shader Installer")]
        static void Open()
        {
            var w = GetWindow<EyeInstallerWindow>("DocMe Eyes");
            w.minSize = new Vector2(380, 420);
            if (w.position.height < 560 || w.position.width < 400)
                w.position = new Rect(w.position.x, w.position.y, Mathf.Max(w.position.width, 440), 620);
        }

        void OnEnable()
        {
            LoadSettings();
            if (material == null) material = FindDefaultMaterial();
            if (avatar == null) avatar = LoadSavedAvatar() ?? AvatarFromSelection();
            previewOn = EditorPrefs.GetBool(PK + "preview", true);
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.update += OnUpdate;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving += OnSceneSaving;
            AssemblyReloadEvents.beforeAssemblyReload += EndPreview;
            EditorApplication.playModeStateChanged += OnPlayMode;
            if (avatar != null) OnAvatarSet();
        }

        void OnSceneSaving(UnityEngine.SceneManagement.Scene scene, string path) => EndPreview();
        void OnPlayMode(PlayModeStateChange change) { if (change == PlayModeStateChange.ExitingEditMode) EndPreview(); }

        void OnDisable()
        {
            EndPreview();
            SaveSettings();
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.update -= OnUpdate;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving -= OnSceneSaving;
            AssemblyReloadEvents.beforeAssemblyReload -= EndPreview;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            previews.Clear();
            pickMode = null;
            if (showOriginal) { SetOriginalEyesVisible(false); showOriginal = false; }
        }

        void OnUndoRedo() { previews.Clear(); Repaint(); }


        void SaveSettings()
        {
            EditorPrefs.SetString(PK + "material", AssetGuid(material));
            EditorPrefs.SetString(PK + "eyeMesh", AssetGuid(eyeMesh));
            EditorPrefs.SetString(PK + "avatar", avatar != null ? GlobalObjectId.GetGlobalObjectIdSlow(avatar).ToString() : "");
            EditorPrefs.SetBool(PK + "hideOriginal", hideOriginal);
            EditorPrefs.SetBool(PK + "inSocket", inSocket);
            EditorPrefs.SetBool(PK + "deformWithFace", deformWithFace);
            EditorPrefs.SetBool(PK + "flattenEye", flattenEye);
            EditorPrefs.SetBool(PK + "deleteOriginal", deleteOriginal);
            EditorPrefs.SetBool(PK + "sinkIntoHead", sinkIntoHead);
            EditorPrefs.SetBool(PK + "dropBackFaces", dropBackFaces);
            EditorPrefs.SetInt(PK + "evenEyeShapes", evenEyeShapes);
            EditorPrefs.SetBool(PK + "lockAspect", lockAspect);
            EditorPrefs.SetBool(PK + "preview", previewOn);
            EditorPrefs.SetBool("DocMe.IslandHide.delete", hides.deleteGeometry);
            EditorPrefs.SetBool("DocMe.IslandHide.sink", hides.sinkIntoHead);
            EditorPrefs.SetFloat(PK + "widthFactor", widthFactor);
            EditorPrefs.SetFloat(PK + "heightFactor", heightFactor);
            EditorPrefs.SetFloat(PK + "depthOffset", depthOffset);
            EditorPrefs.SetFloat(PK + "verticalOffset", verticalOffset);
            EditorPrefs.SetFloat(PK + "horizontalOffset", horizontalOffset);
            EditorPrefs.SetFloat(PK + "rollOffset", rollOffset);
            EditorPrefs.SetFloat(PK + "rollRightOffset", rollRightOffset);
            EditorPrefs.SetInt(PK + "rollMode", rollMode);
            EditorPrefs.SetFloat(PK + "angleOut", angleOut);
            EditorPrefs.SetFloat(PK + "angleUp", angleUp);
        }

        void LoadSettings()
        {
            var m = LoadAsset<Material>(PK + "material"); if (m != null) material = m;
            eyeMesh          = LoadAsset<Mesh>(PK + "eyeMesh");
            hideOriginal     = EditorPrefs.GetBool(PK + "hideOriginal", hideOriginal);
            inSocket         = EditorPrefs.GetBool(PK + "inSocket", inSocket);
            deformWithFace   = EditorPrefs.GetBool(PK + "deformWithFace", deformWithFace);
            flattenEye       = EditorPrefs.GetBool(PK + "flattenEye", flattenEye);
            deleteOriginal   = EditorPrefs.GetBool(PK + "deleteOriginal", deleteOriginal);
            sinkIntoHead     = EditorPrefs.GetBool(PK + "sinkIntoHead", sinkIntoHead);
            dropBackFaces    = EditorPrefs.GetBool(PK + "dropBackFaces", dropBackFaces);
            evenEyeShapes    = EditorPrefs.GetInt(PK + "evenEyeShapes", evenEyeShapes);
            lockAspect       = EditorPrefs.GetBool(PK + "lockAspect", lockAspect);
            hides.deleteGeometry = EditorPrefs.GetBool("DocMe.IslandHide.delete", hides.deleteGeometry);
            hides.sinkIntoHead   = EditorPrefs.GetBool("DocMe.IslandHide.sink", hides.sinkIntoHead);
            widthFactor      = EditorPrefs.GetFloat(PK + "widthFactor", widthFactor);
            heightFactor     = EditorPrefs.GetFloat(PK + "heightFactor", heightFactor);
            depthOffset      = EditorPrefs.GetFloat(PK + "depthOffset", depthOffset);
            verticalOffset   = EditorPrefs.GetFloat(PK + "verticalOffset", verticalOffset);
            horizontalOffset = EditorPrefs.GetFloat(PK + "horizontalOffset", horizontalOffset);
            rollOffset       = EditorPrefs.GetFloat(PK + "rollOffset", rollOffset);
            rollRightOffset  = EditorPrefs.GetFloat(PK + "rollRightOffset", rollRightOffset);
            rollMode         = EditorPrefs.GetInt(PK + "rollMode", rollMode);
            if (rollMode == EyeInstallerCore.RollSame) rollMode = EyeInstallerCore.RollMirror;
            angleOut         = EditorPrefs.GetFloat(PK + "angleOut", angleOut);
            angleUp          = EditorPrefs.GetFloat(PK + "angleUp", angleUp);
        }

        static GameObject LoadSavedAvatar()
        {
            string id = EditorPrefs.GetString(PK + "avatar", "");
            if (string.IsNullOrEmpty(id) || !GlobalObjectId.TryParse(id, out var gid)) return null;
            return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid) as GameObject;
        }

        /// <summary>The avatar root above whatever the user has selected in the Hierarchy.</summary>
        static GameObject AvatarFromSelection()
        {
            var t = Selection.activeTransform;
            if (t == null) return null;
            for (var p = t; p != null; p = p.parent)
            {
                var anim = p.GetComponent<Animator>();
                if (anim != null && anim.isHuman) return p.gameObject;
            }
            return null;
        }

        /// <summary>A DocMe eye material in the project, if one is installed; the field stays empty otherwise.</summary>
        static Material FindDefaultMaterial()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Material M_BasicAnimeEye"))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m != null && m.shader != null && m.shader.name.StartsWith("DocMe/")) return m;
            }
            return null;
        }

        static string AssetGuid(Object o)
        {
            string p = o != null ? AssetDatabase.GetAssetPath(o) : null;
            return string.IsNullOrEmpty(p) ? "" : AssetDatabase.AssetPathToGUID(p);
        }

        static T LoadAsset<T>(string key) where T : Object
        {
            string g = EditorPrefs.GetString(key, "");
            if (string.IsNullOrEmpty(g)) return null;
            string p = AssetDatabase.GUIDToAssetPath(g);
            return string.IsNullOrEmpty(p) ? null : AssetDatabase.LoadAssetAtPath<T>(p);
        }


        static GUIContent GC(string key, string tipKey = null) => new GUIContent(L.Text(key), L.Tip(tipKey ?? key + ".tip"));
        static GUIContent HGC(string key, string tipKey = null) => new GUIContent(HideL.Text(key), HideL.Tip(tipKey ?? key + ".tip"));

        GUIStyle _title, _big, _hint, _ok, _warn, _sel, _box, _choice;
        void EnsureStyles()
        {
            if (_box != null) return;
            _title  = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
            _big    = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, wordWrap = true };
            _hint   = new GUIStyle(EditorStyles.label) { wordWrap = true };
            _hint.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.75f, 0.75f, 0.75f) : new Color(0.3f, 0.3f, 0.3f);
            _ok     = new GUIStyle(EditorStyles.label) { wordWrap = true, fontStyle = FontStyle.Bold };
            _ok.normal.textColor = new Color(0.35f, 0.75f, 0.4f);
            _warn   = new GUIStyle(EditorStyles.label) { wordWrap = true, fontStyle = FontStyle.Bold };
            _warn.normal.textColor = new Color(0.95f, 0.65f, 0.25f);
            _sel    = new GUIStyle(EditorStyles.label) { wordWrap = true, fontStyle = FontStyle.Bold };
            _sel.normal.textColor = new Color(1f, 0.55f, 0.5f);
            _box    = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(12, 12, 10, 10) };
            _choice = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(10, 10, 8, 8) };
        }

        void OnGUI()
        {
            EnsureStyles();
            DrawHeader();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.Space(6);
            switch (page)
            {
                case Page.Avatar: DrawAvatarPage(); break;
                case Page.Eyes:   DrawEyesPage(); break;
                case Page.Look:   DrawLookPage(); break;
                case Page.Done:   DrawDonePage(); break;
            }
            EditorGUILayout.Space(6);
            EditorGUILayout.EndScrollView();

            DrawFooter();
        }

        void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(L.Text("window.title"), _title);
                GUILayout.FlexibleSpace();
                if (DocMeLanguage.DrawPicker(L.Table.Languages())) Repaint();
            }

            // Step strip. Finished steps can be clicked; later ones wait until the step before is done.
            string[] names = { L.Text("step.avatar"), L.Text("step.eyes"), L.Text("step.look"), L.Text("step.done") };
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int i = 0; i < names.Length; i++)
                {
                    var p = (Page)i;
                    bool reachable = CanReach(p);
                    bool current = page == p;
                    GUI.backgroundColor = current ? new Color(0.55f, 0.85f, 0.55f) : Color.white;
                    using (new EditorGUI.DisabledScope(!reachable))
                        if (GUILayout.Toggle(current, names[i], EditorStyles.miniButton, GUILayout.Height(22)) && !current) GoTo(p);
                    GUI.backgroundColor = Color.white;
                }
            }
        }

        bool CanReach(Page p)
        {
            switch (p)
            {
                case Page.Avatar: return true;
                case Page.Eyes:   return avatar != null;
                case Page.Look:   return avatar != null && detected != null && detected.Count > 0;
                case Page.Done:   return HasInstall();
            }
            return false;
        }

        void GoTo(Page p)
        {
            if (pickMode != null) { pickMode = null; SceneView.RepaintAll(); }
            if (page == Page.Look && p != Page.Look) EndPreview();
            if (p == Page.Look) MarkPreviewDirty();
            page = p;
            scroll = Vector2.zero;
            GUIUtility.keyboardControl = 0;
        }

        void DrawFooter()
        {
            EditorGUILayout.Space(2);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(page == Page.Avatar))
                    if (GUILayout.Button(L.Text("nav.back"), GUILayout.Height(26), GUILayout.Width(90)))
                        GoTo(page == Page.Done ? Page.Look : (Page)((int)page - 1));

                GUILayout.FlexibleSpace();

                switch (page)
                {
                    case Page.Avatar:
                        using (new EditorGUI.DisabledScope(!CanReach(Page.Eyes)))
                            if (GUILayout.Button(L.Text("nav.next"), GUILayout.Height(26), GUILayout.Width(110))) GoTo(Page.Eyes);
                        break;
                    case Page.Eyes:
                        using (new EditorGUI.DisabledScope(!CanReach(Page.Look)))
                            if (GUILayout.Button(L.Text("nav.looksRight"), GUILayout.Height(26), GUILayout.Width(150))) GoTo(Page.Look);
                        break;
                    case Page.Look:
                        GUI.backgroundColor = new Color(0.55f, 0.85f, 0.55f);
                        using (new EditorGUI.DisabledScope(material == null))
                            if (GUILayout.Button(HasInstall() ? L.Text("nav.applyChanges") : L.Text("nav.install"), GUILayout.Height(28), GUILayout.Width(150)))
                                Install();
                        GUI.backgroundColor = Color.white;
                        break;
                    case Page.Done:
                        if (GUILayout.Button(L.Text("nav.anotherAvatar"), GUILayout.Height(26), GUILayout.Width(130)))
                        {
                            EndPreview();
                            avatar = null; detected = null; seeds.Clear(); hides.Forget(); hides.avatar = null; previews.Clear();
                            SaveSettings(); GoTo(Page.Avatar);
                        }
                        break;
                }
            }

            showDetails = EditorGUILayout.Foldout(showDetails, L.Text("details"), true);
            if (showDetails)
            {
                string text = string.IsNullOrEmpty(log) ? detectLog : log;
                EditorGUILayout.TextArea(string.IsNullOrEmpty(text) ? L.Text("details.empty") : text, GUILayout.MinHeight(60), GUILayout.MaxHeight(140));
            }
        }


        void DrawAvatarPage()
        {
            GUILayout.Label(L.Text("avatar.title"), _big);
            GUILayout.Label(L.Text("avatar.intro"), _hint);
            EditorGUILayout.Space(6);

            using (new EditorGUILayout.VerticalScope(_box))
            {
                EditorGUI.BeginChangeCheck();
                avatar = (GameObject)EditorGUILayout.ObjectField(GC("avatar.field"), avatar, typeof(GameObject), true);
                if (EditorGUI.EndChangeCheck()) OnAvatarSet();

                if (avatar == null)
                {
                    var fromSel = AvatarFromSelection();
                    if (fromSel != null && GUILayout.Button(L.Text("avatar.useSelected", fromSel.name), GUILayout.Height(24)))
                    { avatar = fromSel; OnAvatarSet(); }
                }
                else
                {
                    EditorGUILayout.Space(4);
                    DrawDetectionStatus();
                    if (HasInstall())
                        GUILayout.Label(L.Text("avatar.hasEyes"), _hint);
                }
            }
        }

        void OnAvatarSet()
        {
            EndPreview();
            detected = null; seeds.Clear(); previews.Clear(); pickMode = null; log = "";
            hides.avatar = avatar; hides.Forget();
            SaveSettings();
            if (avatar == null) return;
            detected = EyeAvatarDetector.Detect(avatar, out detectLog);
            if (HasInstall()) { if (hides.Count > 0) hides.Reload(); page = Page.Done; }
            SceneView.RepaintAll();
        }

        void DrawDetectionStatus()
        {
            if (detected == null || detected.Count == 0)
            {
                GUILayout.Label(L.Text("eyes.noBones"), _warn);
                return;
            }
            var l = detected.Find(d => d.label == "L"); var r = detected.Find(d => d.label == "R");
            bool okL = l != null && (l.HasIsland || l.HasSeed), okR = r != null && (r.HasIsland || r.HasSeed);
            string mesh = l?.renderer != null ? l.renderer.name : r?.renderer != null ? r.renderer.name : "?";
            if (okL && okR) GUILayout.Label(L.Text("eyes.foundBoth", mesh), _ok);
            else if (okL || okR) GUILayout.Label(L.Text(okL ? "eyes.foundOnlyLeft" : "eyes.foundOnlyRight"), _warn);
            else GUILayout.Label(L.Text("eyes.noSurface"), _warn);
        }


        void DrawEyesPage()
        {
            GUILayout.Label(L.Text("eyes.title"), _big);
            GUILayout.Label(L.Text("eyes.about"), _hint);
            EditorGUILayout.Space(6);

            using (new EditorGUILayout.VerticalScope(_box))
            {
                DrawDetectionStatus();
                EditorGUILayout.Space(6);
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawPickButton("L");
                    DrawPickButton("R");
                }
                if (pickMode == "L" || pickMode == "R")
                    GUILayout.Label(L.Text("eyes.pickHelp"), _hint);
                bool anyPicks = false; foreach (var kv in seeds) anyPicks |= kv.Value.Count > 0;
                if (anyPicks)
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(pickMode == null || !seeds.TryGetValue(pickMode, out var l) || l.Count == 0))
                            if (GUILayout.Button(L.Text("eyes.undoClick"))) RemoveLastPoint();
                        if (GUILayout.Button(L.Text("eyes.useAuto"))) { seeds.Clear(); previews.Clear(); pickMode = null; SceneView.RepaintAll(); }
                    }
            }

            advancedEyes = EditorGUILayout.Foldout(advancedEyes, L.Text("advanced"), true);
            if (advancedEyes)
            {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                eyeRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(
                    GC("eyes.mesh"),
                    eyeRenderer, typeof(SkinnedMeshRenderer), true);
                if (EditorGUI.EndChangeCheck()) SaveSettings();
                EditorGUI.indentLevel--;
            }
        }

        void DrawPickButton(string label)
        {
            bool active = pickMode == label;
            int count = seeds.TryGetValue(label, out var l) ? l.Count : 0;
            GUI.backgroundColor = active ? new Color(0.6f, 0.8f, 1f) : (count > 0 ? new Color(0.6f, 0.9f, 0.6f) : Color.white);
            bool left = label == "L";
            string text = active ? L.Text(left ? "eyes.pick.left.active" : "eyes.pick.right.active", count)
                        : count > 0 ? L.Text(left ? "eyes.pick.left.picked" : "eyes.pick.right.picked", count)
                        : L.Text(left ? "eyes.pick.left.fix" : "eyes.pick.right.fix");
            if (GUILayout.Button(text, GUILayout.Height(26)))
            {
                pickMode = active ? null : label;
                SceneView.RepaintAll();
            }
            GUI.backgroundColor = Color.white;
        }

        void RemoveLastPoint()
        {
            if (pickMode == HideKey) { UndoLastHide(); return; }
            if (pickMode != null && seeds.TryGetValue(pickMode, out var list) && list.Count > 0)
            {
                list.RemoveAt(list.Count - 1);
                SceneView.RepaintAll();
            }
        }


        void DrawLookPage()
        {
            GUILayout.Label(L.Text("look.title"), _big);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                previewOn = EditorGUILayout.ToggleLeft(GC("look.preview"), previewOn, GUILayout.Width(200));
                if (EditorGUI.EndChangeCheck()) { SaveSettings(); if (previewOn) MarkPreviewDirty(); else EndPreview(); }
                GUILayout.FlexibleSpace();
                GUILayout.Label(!previewOn ? "" : previewDirty ? L.Text("look.preview.updating") : preview != null && !preview.IsEmpty ? L.Text("look.preview.shown") : "", _hint);
            }
            EditorGUILayout.Space(4);

            using (new EditorGUILayout.VerticalScope(_box))
            {
                EditorGUI.BeginChangeCheck();
                material = (Material)EditorGUILayout.ObjectField(GC("look.material"),
                    material, typeof(Material), false);
                if (material == null) GUILayout.Label(L.Text("look.material.missing"), _warn);

                EditorGUILayout.Space(6);
                GUILayout.Label(L.Text("look.build"), EditorStyles.boldLabel);
                DrawModeChoice(false,
                    L.Text("look.mode.flat"),
                    L.Text("look.mode.flat.about"));
                DrawModeChoice(true,
                    L.Text("look.mode.reuse"),
                    L.Text("look.mode.reuse.about"));

                EditorGUILayout.Space(6);
                if (lockAspect)
                {
                    float s = EditorGUILayout.Slider(GC("look.size"), widthFactor, 0.3f, 3f);
                    widthFactor = heightFactor = s;
                }
                else
                {
                    widthFactor  = EditorGUILayout.Slider(GC("look.width"), widthFactor, 0.3f, 3f);
                    heightFactor = EditorGUILayout.Slider(GC("look.height"), heightFactor, 0.3f, 3f);
                }
                if (EditorGUI.EndChangeCheck()) { SaveSettings(); MarkPreviewDirty(); }
            }

            advancedLook = EditorGUILayout.Foldout(advancedLook, L.Text("advanced"), true);
            if (advancedLook)
            {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                lockAspect = EditorGUILayout.ToggleLeft(GC("look.sameSize"), lockAspect);
                depthOffset      = EditorGUILayout.Slider(GC("look.depth"), depthOffset, -0.02f, 0.02f);
                verticalOffset   = EditorGUILayout.Slider(GC("look.upDown"), verticalOffset, -0.02f, 0.02f);
                horizontalOffset = EditorGUILayout.Slider(GC("look.apart"), horizontalOffset, -0.02f, 0.02f);
                angleOut = EditorGUILayout.Slider(GC("look.angleOut"), angleOut, -30f, 30f);
                angleUp = EditorGUILayout.Slider(GC("look.tilt"), angleUp, -30f, 30f);
                bool mirror = rollMode != EyeInstallerCore.RollIndependent;
                rollOffset = EditorGUILayout.Slider(GC(mirror ? "look.roll" : "look.roll.left", "look.roll.tip"), rollOffset, -180f, 180f);
                bool mirrorNow = EditorGUILayout.ToggleLeft(GC("look.mirrorRoll"), mirror);
                if (mirrorNow != mirror) rollMode = mirrorNow ? EyeInstallerCore.RollMirror : EyeInstallerCore.RollIndependent;
                if (!mirrorNow) rollRightOffset = EditorGUILayout.Slider(GC("look.roll.right", "look.roll.tip"), rollRightOffset, -180f, 180f);
                EditorGUILayout.Space(4);

                if (!deformWithFace)
                {
                    eyeMesh = (Mesh)EditorGUILayout.ObjectField(GC("look.customMesh"), eyeMesh, typeof(Mesh), false);
                    inSocket = EditorGUILayout.ToggleLeft(GC("look.behindLids"), inSocket);
                    hideOriginal = EditorGUILayout.ToggleLeft(GC("look.hideOriginal"), hideOriginal);
                    using (new EditorGUI.DisabledScope(!hideOriginal))
                    {
                        deleteOriginal = EditorGUILayout.ToggleLeft(GC("look.deleteOriginal"), deleteOriginal);
                        using (new EditorGUI.DisabledScope(deleteOriginal))
                            sinkIntoHead = EditorGUILayout.ToggleLeft(GC("look.foldIntoHead"), sinkIntoHead);
                    }
                }
                else
                {
                    flattenEye = EditorGUILayout.ToggleLeft(GC("look.flatten"), flattenEye);
                    using (new EditorGUI.DisabledScope(!flattenEye))
                        dropBackFaces = EditorGUILayout.ToggleLeft(GC("look.dropBack"), dropBackFaces);
                    evenEyeShapes = EditorGUILayout.Popup(GC("look.shapes"),
                        evenEyeShapes, new[] { L.Text("look.shapes.authored"), L.Text("look.shapes.mean"), L.Text("look.shapes.max"), L.Text("look.shapes.move") });
                    if (evenEyeShapes != 0 && previewOn)
                        GUILayout.Label(L.Text("look.shapes.previewNote"), _hint);
                }
                if (EditorGUI.EndChangeCheck()) { SaveSettings(); MarkPreviewDirty(); }
                EditorGUI.indentLevel--;
            }

            showPresets = EditorGUILayout.Foldout(showPresets, L.Text("presets"), true);
            if (showPresets) { EditorGUI.indentLevel++; DrawPresetBar(); EditorGUI.indentLevel--; }
        }

        void DrawModeChoice(bool deform, string title, string body)
        {
            bool on = deformWithFace == deform;
            GUI.backgroundColor = on ? new Color(0.72f, 0.9f, 0.72f) : Color.white;
            using (new EditorGUILayout.VerticalScope(_choice))
            {
                GUI.backgroundColor = Color.white;
                bool now = EditorGUILayout.ToggleLeft(title, on, EditorStyles.boldLabel);
                GUILayout.Label(body, _hint);
                if (now && !on) { deformWithFace = deform; SaveSettings(); MarkPreviewDirty(); }
            }
            GUI.backgroundColor = Color.white;
        }


        void DrawDonePage()
        {
            if (!HasInstall()) { GoTo(Page.Avatar); return; }

            GUILayout.Label(L.Text("done.installed"), _ok);
            GUILayout.Label(L.Text("done.about"), _hint);

            bool missing = DocMeReapply.HasMissingMesh(avatar);
            if (missing || DocMeReapply.IsStale(avatar))
            {
                if (missing)
                    EditorGUILayout.HelpBox(L.Text("done.meshMissing"), MessageType.Error);
                else
                    EditorGUILayout.HelpBox(L.Text("done.modelChanged"), MessageType.Warning);
                if (GUILayout.Button(missing ? L.Text("done.rebuild") : L.Text("done.reapply"), GUILayout.Height(24)))
                {
                    log = DocMeReapply.ReapplyAll(avatar);
                    seeds.Clear(); previews.Clear(); showOriginal = false;
                    hides.Reload();
                }
            }

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.VerticalScope(_box))
            {
                GUILayout.Label(L.Text("done.hide.title"), EditorStyles.boldLabel);
                GUILayout.Label(L.Text("done.hide.about"), _hint);
                bool hiding = pickMode == HideKey;
                int hidden = HiddenCount(), selected = SelectedCount();
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUI.backgroundColor = hiding ? new Color(0.6f, 0.8f, 1f) : Color.white;
                    if (GUILayout.Button(HGC(hiding ? "hide.selecting" : "hide.select", "hide.select.tip"), GUILayout.Height(26)))
                    { pickMode = hiding ? null : HideKey; SceneView.RepaintAll(); }
                    GUI.backgroundColor = selected > 0 ? new Color(0.6f, 0.9f, 0.65f) : Color.white;
                    using (new EditorGUI.DisabledScope(selected == 0))
                        if (GUILayout.Button(new GUIContent(selected == 0 ? HideL.Text("hide.button.none") : HideL.Count(selected, "hide.button", selected), HideL.Tip("hide.button.tip")), GUILayout.Height(26), GUILayout.Width(160)))
                            HideSelected();
                    GUI.backgroundColor = Color.white;
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(hidden == 0 && selected == 0))
                        if (GUILayout.Button(HGC("hide.undo"), GUILayout.Width(100), GUILayout.Height(22)))
                            UndoLastHide();
                    using (new EditorGUI.DisabledScope(hidden == 0))
                        if (GUILayout.Button(HGC("hide.showAll"), GUILayout.Width(90), GUILayout.Height(22)))
                            ClearHides();
                    GUILayout.FlexibleSpace();
                }
                if (selected > 0) GUILayout.Label(HideL.Count(selected, "hide.selected", selected), _sel);
                if (hidden > 0) GUILayout.Label(HideL.Count(hidden, "hide.hidden", hidden), _ok);
            }

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.VerticalScope(_box))
            {
                GUILayout.Label(L.Text("done.fineTune"), EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                if (lockAspect)
                {
                    float s = EditorGUILayout.Slider(GC("look.size"), widthFactor, 0.3f, 3f);
                    widthFactor = heightFactor = s;
                }
                else
                {
                    widthFactor  = EditorGUILayout.Slider(GC("look.width"), widthFactor, 0.3f, 3f);
                    heightFactor = EditorGUILayout.Slider(GC("look.height"), heightFactor, 0.3f, 3f);
                }
                depthOffset      = EditorGUILayout.Slider(GC("look.depth"), depthOffset, -0.02f, 0.02f);
                verticalOffset   = EditorGUILayout.Slider(GC("look.upDown"), verticalOffset, -0.02f, 0.02f);
                horizontalOffset = EditorGUILayout.Slider(GC("look.apart"), horizontalOffset, -0.02f, 0.02f);
                bool changed = EditorGUI.EndChangeCheck();
                if (changed) SaveSettings();
                if (changed && !deformWithFace) UpdateFit();
                if (deformWithFace)
                {
                    GUILayout.Label(L.Text("done.reusedNote"), _hint);
                    if (GUILayout.Button(L.Text("done.apply"), GUILayout.Height(24))) Install();
                }

                EditorGUI.BeginChangeCheck();
                showOriginal = EditorGUILayout.ToggleLeft(GC("done.showOriginal"), showOriginal);
                if (EditorGUI.EndChangeCheck()) SetOriginalEyesVisible(showOriginal);
            }

            advancedDone = EditorGUILayout.Foldout(advancedDone, L.Text("advanced"), true);
            if (advancedDone)
            {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                hides.deleteGeometry = EditorGUILayout.ToggleLeft(HGC("hide.delete"), hides.deleteGeometry);
                using (new EditorGUI.DisabledScope(hides.deleteGeometry))
                    hides.sinkIntoHead = EditorGUILayout.ToggleLeft(HGC("hide.fold"), hides.sinkIntoHead);
                if (EditorGUI.EndChangeCheck()) { SaveSettings(); if (hides.Count > 0) Busy(HideL.Text("hide.busy.updating"), hides.Reapply); }

                DocMeReapply.AutoEnabled = EditorGUILayout.ToggleLeft(HGC("hide.autoRebuild"), DocMeReapply.AutoEnabled);

                EditorGUILayout.Space(6);
                GUI.backgroundColor = new Color(0.92f, 0.55f, 0.55f);
                if (GUILayout.Button(L.Text("done.remove"), GUILayout.Height(24)))
                {
                    HideIslandsCore.Revert(avatar);
                    EyeInstallerCore.Revert(avatar);
                    hides.Forget(); previews.Clear(); showOriginal = false; pickMode = null;
                    log = L.Text("done.removed");
                    GoTo(Page.Look);
                }
                GUI.backgroundColor = Color.white;
                EditorGUI.indentLevel--;
            }
        }

        int HiddenCount() => hides.Count;
        int SelectedCount() => hides.PendingCount;

        /// <summary>Runs a rebuild behind a progress bar: every rebuild writes the whole mesh asset.</summary>
        void Busy(string text, System.Action work)
        {
            EditorUtility.DisplayProgressBar(L.Text("window.title"), text, 0.4f);
            try { work(); }
            finally { EditorUtility.ClearProgressBar(); previews.Clear(); Repaint(); SceneView.RepaintAll(); }
        }

        void UndoLastHide()
        {
            if (hides.PendingCount > 0) hides.UndoLast();
            else Busy(HideL.Text("hide.busy.updating"), hides.UndoLast);
        }

        void ClearHides() => Busy(HideL.Text("hide.busy.showAll"), hides.Clear);

        void HideSelected()
        {
            int n = hides.PendingCount;
            if (n == 0) return;
            int done = 0;
            Busy(HideL.Count(n, "hide.busy.hiding", n), () => done = hides.HideSelected());
            log = done == n ? "" : HideL.Text("hide.notHidden", n - done);
        }

        void AddHide(IslandPick seed)
        {
            switch (hides.Select(seed))
            {
                case HidePickSession.SelectResult.Selected:
                case HidePickSession.SelectResult.Deselected: log = ""; break;
                case HidePickSession.SelectResult.Protected: log = L.Text("done.hide.isEye"); break;
                case HidePickSession.SelectResult.TooBig: log = HideL.Text("hide.tooBig"); break;
                default: log = HideL.Text("hide.unreadable"); break;
            }
        }


        void Install()
        {
            if (avatar == null) return;
            EndPreview();
            if (pickMode != null) { pickMode = null; SceneView.RepaintAll(); }

            // Hidden parts sit on top of the installed eye, so they come off first and go back on after.
            var hideMarker = avatar.GetComponent<DocMeIslandHide>();
            var keepHides = hideMarker != null ? new List<DocMePick>(hideMarker.picks) : null;
            bool keepDelete = hideMarker != null ? hideMarker.deleteGeometry : hides.deleteGeometry;
            bool keepSink = hideMarker != null ? hideMarker.sinkIntoHead : hides.sinkIntoHead;
            HideIslandsCore.Revert(avatar);
            if (HasInstall()) EyeInstallerCore.Revert(avatar);

            detected = EyeAvatarDetector.Detect(avatar, out detectLog);
            foreach (var d in detected)
                if (seeds.TryGetValue(d.label, out var list) && list.Count > 0) d.seeds = new List<IslandPick>(list);
            EyeInstallerCore.Install(avatar, detected, BuildOptions());
            SaveSettings();
            showOriginal = false;

            log = DescribeInstall();
            if (keepHides != null && keepHides.Count > 0)
            {
                log += "\n" + HideIslandsCore.Reapply(avatar, keepHides, keepDelete, keepSink);
                hides.Reload();
            }
            previews.Clear();
            SceneView.RepaintAll();
            if (HasInstall()) GoTo(Page.Done);
        }


        void MarkPreviewDirty()
        {
            if (!previewOn || page != Page.Look) return;
            previewDirty = true;
            previewStamp = EditorApplication.timeSinceStartup;
            Repaint();
        }

        // Rebuilds a little after the last change, so a slider drag does not rebuild on every frame.
        void OnUpdate()
        {
            if (!previewDirty || page != Page.Look) return;
            if (EditorApplication.timeSinceStartup - previewStamp < 0.2) return;
            previewDirty = false;
            RefreshPreview();
            Repaint();
        }

        void RefreshPreview()
        {
            EndPreview();
            if (!previewOn || avatar == null || material == null) return;

            EditorUtility.DisplayProgressBar("DocMe Eyes", L.Text("look.preview.building"), 0.4f);
            try
            {
                // An existing install is parked (originals shown, placed eyes off) so the preview builds from the real mesh.
                var marker = avatar.GetComponent<DocMeEyeInstall>();
                if (marker != null)
                {
                    SetOriginalEyesVisible(true);
                    foreach (var c in marker.createdEyes)
                        if (c.go != null && c.go.activeSelf) { c.go.SetActive(false); parkedEyes.Add(c.go); }
                    previewSwapped = true;
                }

                var eyes = EyeAvatarDetector.Detect(avatar, out _);
                foreach (var d in eyes)
                    if (seeds.TryGetValue(d.label, out var list) && list.Count > 0) d.seeds = new List<IslandPick>(list);
                preview = EyeInstallerCore.Preview(avatar, eyes, BuildOptions());
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                SceneView.RepaintAll();
            }
        }

        void EndPreview()
        {
            previewDirty = false;
            if (preview != null) { preview.Restore(); preview = null; }
            if (previewSwapped)
            {
                foreach (var go in parkedEyes) if (go != null) go.SetActive(true);
                parkedEyes.Clear();
                SetOriginalEyesVisible(showOriginal);
                previewSwapped = false;
            }
            SceneView.RepaintAll();
        }

        EyeInstallerCore.Options BuildOptions() => new EyeInstallerCore.Options
        {
            material = material,
            eyeMesh = eyeMesh,
            hideOriginal = hideOriginal,
            deleteOriginal = deleteOriginal,
            sinkIntoHead = sinkIntoHead,
            inSocket = inSocket,
            deformWithFace = deformWithFace,
            flattenEye = flattenEye,
            dropBackFaces = dropBackFaces,
            evenEyeShapes = evenEyeShapes,
            widthFactor = widthFactor,
            heightFactor = heightFactor,
            depthOffset = depthOffset,
            verticalOffset = verticalOffset,
            horizontalOffset = horizontalOffset,
            rollOffset = rollOffset,
            rollRightOffset = rollRightOffset,
            rollMode = rollMode,
            angleOut = angleOut,
            angleUp = angleUp,
            eyeRenderers = eyeRenderer != null ? new List<SkinnedMeshRenderer> { eyeRenderer } : null,
        };

        bool HasInstall() => avatar != null && avatar.GetComponent<DocMeEyeInstall>() != null;

        string DescribeInstall()
        {
            var sb = new System.Text.StringBuilder();
            if (detected != null)
                foreach (var d in detected)
                {
                    string src = d.HasSeed ? L.Count(d.seeds.Count, "report.clicks", d.seeds.Count) : L.Text("report.auto");
                    sb.AppendLine($"[{d.label}] {src}");
                    if (d.HasSeed)
                        foreach (var s in d.seeds)
                            if (s.renderer == null || !s.renderer.transform.IsChildOf(avatar.transform))
                                sb.AppendLine("  " + L.Text("report.clickElsewhere", s.renderer ? s.renderer.transform.root.name : L.Text("report.deletedObject")));
                }

            var marker = avatar != null ? avatar.GetComponent<DocMeEyeInstall>() : null;
            if (marker == null) return sb.Append(L.Text("report.nothing")).ToString();

            foreach (var h in marker.hiddenRenderers)
            {
                if (h.renderer == null) continue;
                string where = h.renderer.transform.IsChildOf(avatar.transform)
                    ? AnimationUtility.CalculateTransformPath(h.renderer.transform, avatar.transform)
                    : "⚠ " + h.renderer.transform.root.name + "/" + h.renderer.name;
                int before = h.originalMesh ? h.originalMesh.triangles.Length / 3 : 0;
                int after = h.modifiedMesh ? h.modifiedMesh.triangles.Length / 3 : 0;
                int eyeTris = h.reshaded && h.modifiedMesh ? h.modifiedMesh.GetTriangles(h.modifiedMesh.subMeshCount - 1).Length / 3 : 0;
                sb.AppendLine(h.reshaded
                    ? L.Text("report.reshaded", where, eyeTris, before - after)
                    : L.Text("report.hidden", where, before - after));
                if (!h.renderer.gameObject.activeInHierarchy)
                    sb.AppendLine("  " + L.Text("report.meshHidden"));
            }
            if (marker.createdEyes.Count > 0)
                sb.AppendLine(L.Count(marker.createdEyes.Count, "report.placed", marker.createdEyes.Count));
            return sb.ToString().TrimEnd();
        }

        void SetOriginalEyesVisible(bool visible)
        {
            var marker = avatar != null ? avatar.GetComponent<DocMeEyeInstall>() : null;
            if (marker == null) return;
            foreach (var h in marker.hiddenRenderers)
            {
                if (h.renderer == null) continue;
                if (!h.reshaded)
                {
                    if (h.renderer.sharedMesh == null || string.IsNullOrEmpty(h.blendShapeName)) continue;
                    int idx = h.renderer.sharedMesh.GetBlendShapeIndex(h.blendShapeName);
                    if (idx >= 0) h.renderer.SetBlendShapeWeight(idx, visible ? 0f : 100f);
                }
                else if (visible)
                {
                    if (h.originalMesh != null) h.renderer.sharedMesh = h.originalMesh;
                    if (h.originalMaterials != null) h.renderer.sharedMaterials = h.originalMaterials;
                }
                else
                {
                    // Hidden parts were applied on top of the installed mesh; put that one back when it exists.
                    var hide = avatar.GetComponent<DocMeIslandHide>();
                    Mesh top = h.modifiedMesh;
                    if (hide != null) foreach (var e in hide.entries) if (e.renderer == h.renderer && e.modifiedMesh != null) top = e.modifiedMesh;
                    if (top != null) h.renderer.sharedMesh = top;
                    if (h.modifiedMaterials != null) h.renderer.sharedMaterials = h.modifiedMaterials;
                    if (top != null && !string.IsNullOrEmpty(h.blendShapeName))
                    {
                        int idx = top.GetBlendShapeIndex(h.blendShapeName);
                        if (idx >= 0) h.renderer.SetBlendShapeWeight(idx, 100f);
                    }
                }
            }
            SceneView.RepaintAll();
        }

        void UpdateFit()
        {
            var marker = avatar != null ? avatar.GetComponent<DocMeEyeInstall>() : null;
            if (marker == null) return;

            var fresh = EyeAvatarDetector.Detect(avatar, out _);
            if (marker.eyeRadius > 0f) foreach (var d in fresh) d.radiusWorld = marker.eyeRadius;
            var opt = BuildOptions();
            marker.widthFactor = widthFactor;
            marker.heightFactor = heightFactor;
            marker.forwardOffset = depthOffset;
            marker.verticalOffset = verticalOffset;
            marker.recipe.widthFactor = widthFactor; marker.recipe.heightFactor = heightFactor;
            marker.recipe.depthOffset = depthOffset; marker.recipe.verticalOffset = verticalOffset;
            marker.recipe.horizontalOffset = horizontalOffset;
            marker.recipe.rollOffset = rollOffset; marker.recipe.rollRightOffset = rollRightOffset; marker.recipe.rollMode = rollMode;
            marker.recipe.angleOut = angleOut; marker.recipe.angleUp = angleUp;

            foreach (var c in marker.createdEyes)
            {
                if (c.go == null) continue;
                var d = fresh.Find(e => e.label == c.label);
                if (d != null) { EyeInstallerCore.ApplyFit(c.go.transform, d, opt, c.depth); EyeInstallerCore.RefreshGazeBias(c.go, d, opt); }
            }
            EditorUtility.SetDirty(avatar);
        }


        void DrawPresetBar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                preset = (DocMeEyePreset)EditorGUILayout.ObjectField(GC("preset.field"),
                    preset, typeof(DocMeEyePreset), false);
                if (EditorGUI.EndChangeCheck() && preset != null) LoadPreset(preset);
                using (new EditorGUI.DisabledScope(preset == null))
                    if (GUILayout.Button(GC("preset.save"), GUILayout.Width(56))) StoreInto(preset);
                if (GUILayout.Button(GC("preset.new"), GUILayout.Width(56))) SaveAsNewPreset();
            }
        }

        void LoadPreset(DocMeEyePreset p)
        {
            material = p.material; eyeMesh = p.eyeMesh;
            hideOriginal = p.hideOriginal; inSocket = p.inSocket;
            deformWithFace = p.deformWithFace; flattenEye = p.flattenEye;
            lockAspect = p.lockAspect;
            widthFactor = p.widthFactor; heightFactor = p.heightFactor;
            depthOffset = p.depthOffset; verticalOffset = p.verticalOffset;
            horizontalOffset = p.horizontalOffset; rollOffset = p.rollOffset;
            rollRightOffset = p.rollRightOffset; angleOut = p.angleOut; angleUp = p.angleUp;
            rollMode = p.rollMode == EyeInstallerCore.RollIndependent ? EyeInstallerCore.RollIndependent : EyeInstallerCore.RollMirror;
            SaveSettings();
            MarkPreviewDirty();
            log = L.Text("preset.loaded", p.name);
        }

        void StoreInto(DocMeEyePreset p)
        {
            p.material = material; p.eyeMesh = eyeMesh;
            p.hideOriginal = hideOriginal; p.inSocket = inSocket;
            p.deformWithFace = deformWithFace; p.flattenEye = flattenEye;
            p.lockAspect = lockAspect;
            p.widthFactor = widthFactor; p.heightFactor = heightFactor;
            p.depthOffset = depthOffset; p.verticalOffset = verticalOffset;
            p.horizontalOffset = horizontalOffset; p.rollOffset = rollOffset;
            p.rollRightOffset = rollRightOffset; p.rollMode = rollMode; p.angleOut = angleOut; p.angleUp = angleUp;
            EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssets();
            log = L.Text("preset.saved", p.name);
        }

        void SaveAsNewPreset()
        {
            string suggested = avatar != null ? avatar.name : "DocMe Eye Preset";
            string path = EditorUtility.SaveFilePanelInProject(L.Text("preset.dialog.title"), suggested, "asset",
                L.Text("preset.dialog.message"));
            if (string.IsNullOrEmpty(path)) return;
            var p = ScriptableObject.CreateInstance<DocMeEyePreset>();
            AssetDatabase.CreateAsset(p, path);
            StoreInto(p);
            preset = p;
            EditorGUIUtility.PingObject(p);
        }


        void OnSceneGUI(SceneView sv)
        {
            if (avatar == null) return;

            // On the Eyes page: tint what will be used as the eye. Picks replace auto detection per eye.
            if (page == Page.Eyes && Event.current.type == EventType.Repaint)
            {
                for (int i = 0; i < 2; i++)
                {
                    string key = i == 0 ? "L" : "R";
                    Color c = i == 0 ? new Color(0.3f, 0.85f, 1f) : new Color(1f, 0.65f, 0.25f);
                    if (seeds.TryGetValue(key, out var list) && list.Count > 0)
                        foreach (var seed in list) previews.Draw(previews.Get(seed.renderer, seed.vertex), c, pickMode == key);
                    else if (detected != null)
                    {
                        var d = detected.Find(x => x.label == key);
                        if (d != null && d.HasIsland) previews.Draw(previews.GetFromVerts(d.renderer, d.island, i), c, pickMode == key);
                    }
                }
            }

            if (page == Page.Eyes)
                foreach (var kv in seeds)
                {
                    Handles.color = kv.Key == "L" ? new Color(0.3f, 0.85f, 1f) : new Color(1f, 0.65f, 0.25f);
                    foreach (var seed in kv.Value)
                        Handles.SphereHandleCap(0, seed.point, Quaternion.identity, HandleUtility.GetHandleSize(seed.point) * 0.05f, EventType.Repaint);
                }

            if (page == Page.Done && Event.current.type == EventType.Repaint)
            {
                var sel = new Color(1f, 0.35f, 0.35f);
                foreach (var p in hides.Pending) previews.Draw(previews.Get(p.renderer, p.vertex), sel, pickMode == HideKey);
                if (pickMode == HideKey)
                {
                    Handles.color = new Color(0.55f, 0.55f, 0.55f);
                    foreach (var p in hides.Picks)
                        Handles.SphereHandleCap(0, p.point, Quaternion.identity, HandleUtility.GetHandleSize(p.point) * 0.04f, EventType.Repaint);
                }
            }

            if (pickMode == null) return;
            sv.Repaint();

            Handles.BeginGUI();
            string overlay;
            if (pickMode == HideKey)
                overlay = HideL.Text("hide.overlay", SelectedCount(), HiddenCount());
            else
            {
                int picked = seeds.TryGetValue(pickMode, out var cur) ? cur.Count : 0;
                overlay = L.Text(pickMode == "L" ? "overlay.pick.left" : "overlay.pick.right", picked);
            }
            GUI.Label(new Rect(8, 8, 620, 22), overlay);
            Handles.EndGUI();

            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { pickMode = null; e.Use(); Repaint(); return; }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Z && (e.control || e.command)) { RemoveLastPoint(); e.Use(); Repaint(); return; }
            if (e.type == EventType.KeyDown && pickMode == HideKey && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)) { HideSelected(); e.Use(); Repaint(); return; }

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (MeshPickUtil.Raycast(avatar, e.mousePosition, out var r, out int vert, out Vector3 pt, out string why))
                {
                    var seed = new IslandPick { renderer = r, vertex = vert, point = pt };
                    if (pickMode == HideKey) AddHide(seed);
                    else
                    {
                        if (!seeds.TryGetValue(pickMode, out var list)) seeds[pickMode] = list = new List<IslandPick>();
                        list.Add(seed);
                    }
                }
                else log = why;
                e.Use(); Repaint(); SceneView.RepaintAll();
            }
        }
    }
}
