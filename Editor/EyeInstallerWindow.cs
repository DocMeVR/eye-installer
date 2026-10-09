using System.Collections.Generic;
using UnityEditor;
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
        bool japanese = false;
        float widthFactor = 1.3f;
        float heightFactor = 1.3f;
        float depthOffset = 0f;
        float verticalOffset = 0f;
        float horizontalOffset = 0f;
        float rollOffset = 0f;

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
        readonly Dictionary<string, List<EyeSeed>> seeds = new Dictionary<string, List<EyeSeed>>();

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
            EditorPrefs.SetBool(PK + "japanese", japanese);
            EditorPrefs.SetBool(PK + "preview", previewOn);
            EditorPrefs.SetBool("DocMe.IslandHide.delete", hides.deleteGeometry);
            EditorPrefs.SetBool("DocMe.IslandHide.sink", hides.sinkIntoHead);
            EditorPrefs.SetFloat(PK + "widthFactor", widthFactor);
            EditorPrefs.SetFloat(PK + "heightFactor", heightFactor);
            EditorPrefs.SetFloat(PK + "depthOffset", depthOffset);
            EditorPrefs.SetFloat(PK + "verticalOffset", verticalOffset);
            EditorPrefs.SetFloat(PK + "horizontalOffset", horizontalOffset);
            EditorPrefs.SetFloat(PK + "rollOffset", rollOffset);
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
            japanese         = EditorPrefs.GetBool(PK + "japanese", japanese);
            hides.deleteGeometry = EditorPrefs.GetBool("DocMe.IslandHide.delete", hides.deleteGeometry);
            hides.sinkIntoHead   = EditorPrefs.GetBool("DocMe.IslandHide.sink", hides.sinkIntoHead);
            widthFactor      = EditorPrefs.GetFloat(PK + "widthFactor", widthFactor);
            heightFactor     = EditorPrefs.GetFloat(PK + "heightFactor", heightFactor);
            depthOffset      = EditorPrefs.GetFloat(PK + "depthOffset", depthOffset);
            verticalOffset   = EditorPrefs.GetFloat(PK + "verticalOffset", verticalOffset);
            horizontalOffset = EditorPrefs.GetFloat(PK + "horizontalOffset", horizontalOffset);
            rollOffset       = EditorPrefs.GetFloat(PK + "rollOffset", rollOffset);
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


        string L(string en, string jp) => japanese ? jp : en;
        GUIContent GC(string en, string jp, string tEn = null, string tJp = null)
            => new GUIContent(L(en, jp), tEn == null ? null : L(tEn, tJp));

        GUIStyle _title, _big, _hint, _ok, _warn, _box, _choice;
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
                GUILayout.Label(L("DocMe Eyes", "DocMe アイ"), _title);
                GUILayout.FlexibleSpace();
                int cur = japanese ? 1 : 0;
                int sel = GUILayout.Toolbar(cur, new[] { "EN", "日本語" }, GUILayout.Width(110), GUILayout.Height(20));
                if (sel != cur) { japanese = sel == 1; SaveSettings(); GUIUtility.keyboardControl = 0; }
            }

            // Step strip. Finished steps can be clicked; later ones wait until the step before is done.
            string[] names = { L("1 Avatar", "1 アバター"), L("2 Eyes", "2 目"), L("3 Look", "3 見た目"), L("4 Done", "4 完了") };
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
                    if (GUILayout.Button(L("◀ Back", "◀ 戻る"), GUILayout.Height(26), GUILayout.Width(90)))
                        GoTo(page == Page.Done ? Page.Look : (Page)((int)page - 1));

                GUILayout.FlexibleSpace();

                switch (page)
                {
                    case Page.Avatar:
                        using (new EditorGUI.DisabledScope(!CanReach(Page.Eyes)))
                            if (GUILayout.Button(L("Next ▶", "次へ ▶"), GUILayout.Height(26), GUILayout.Width(110))) GoTo(Page.Eyes);
                        break;
                    case Page.Eyes:
                        using (new EditorGUI.DisabledScope(!CanReach(Page.Look)))
                            if (GUILayout.Button(L("Looks right ▶", "これで合っている ▶"), GUILayout.Height(26), GUILayout.Width(150))) GoTo(Page.Look);
                        break;
                    case Page.Look:
                        GUI.backgroundColor = new Color(0.55f, 0.85f, 0.55f);
                        using (new EditorGUI.DisabledScope(material == null))
                            if (GUILayout.Button(HasInstall() ? L("Apply changes", "変更を反映") : L("Install eyes", "目をインストール"), GUILayout.Height(28), GUILayout.Width(150)))
                                Install();
                        GUI.backgroundColor = Color.white;
                        break;
                    case Page.Done:
                        if (GUILayout.Button(L("Another avatar", "別のアバター"), GUILayout.Height(26), GUILayout.Width(130)))
                        {
                            EndPreview();
                            avatar = null; detected = null; seeds.Clear(); hides.Forget(); hides.avatar = null; previews.Clear();
                            SaveSettings(); GoTo(Page.Avatar);
                        }
                        break;
                }
            }

            showDetails = EditorGUILayout.Foldout(showDetails, L("Details", "詳細"), true);
            if (showDetails)
            {
                string text = string.IsNullOrEmpty(log) ? detectLog : log;
                EditorGUILayout.TextArea(string.IsNullOrEmpty(text) ? L("(nothing yet)", "（まだありません）") : text, GUILayout.MinHeight(60), GUILayout.MaxHeight(140));
            }
        }


        void DrawAvatarPage()
        {
            GUILayout.Label(L("Which avatar gets the new eyes?", "どのアバターに目を入れますか？"), _big);
            GUILayout.Label(L("Drag your avatar from the Hierarchy into the field below. If it is selected already, it is picked up for you.",
                              "Hierarchy からアバターを下の欄にドラッグしてください。選択中なら自動で入ります。"), _hint);
            EditorGUILayout.Space(6);

            using (new EditorGUILayout.VerticalScope(_box))
            {
                EditorGUI.BeginChangeCheck();
                avatar = (GameObject)EditorGUILayout.ObjectField(GC("Avatar", "アバター"), avatar, typeof(GameObject), true);
                if (EditorGUI.EndChangeCheck()) OnAvatarSet();

                if (avatar == null)
                {
                    var fromSel = AvatarFromSelection();
                    if (fromSel != null && GUILayout.Button(L($"Use selected: {fromSel.name}", $"選択中の「{fromSel.name}」を使う"), GUILayout.Height(24)))
                    { avatar = fromSel; OnAvatarSet(); }
                }
                else
                {
                    EditorGUILayout.Space(4);
                    DrawDetectionStatus();
                    if (HasInstall())
                        GUILayout.Label(L("This avatar already has DocMe eyes. Step 4 lets you adjust or remove them.",
                                          "このアバターには DocMe の目が入っています。ステップ4で調整や削除ができます。"), _hint);
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
                GUILayout.Label(L("⚠ No eye bones found on this avatar. Make sure the Animator is humanoid with LeftEye and RightEye mapped.",
                                  "⚠ 目のボーンが見つかりません。Animator が Humanoid で LeftEye / RightEye が設定されているか確認してください。"), _warn);
                return;
            }
            var l = detected.Find(d => d.label == "L"); var r = detected.Find(d => d.label == "R");
            bool okL = l != null && (l.HasIsland || l.HasSeed), okR = r != null && (r.HasIsland || r.HasSeed);
            string mesh = l?.renderer != null ? l.renderer.name : r?.renderer != null ? r.renderer.name : "?";
            if (okL && okR) GUILayout.Label(L($"✓ Found both eyes on '{mesh}'.", $"✓ 両目を「{mesh}」で見つけました。"), _ok);
            else if (okL || okR) GUILayout.Label(L($"⚠ Found only the {(okL ? "left" : "right")} eye. Fix the other one in step 2.", $"⚠ {(okL ? "左" : "右")}目だけ見つかりました。ステップ2でもう片方を直してください。"), _warn);
            else GUILayout.Label(L("⚠ Eye bones found, but not the eye surface. Click the eyes in step 2.", "⚠ 目のボーンはありますが目の面が見つかりません。ステップ2で目をクリックしてください。"), _warn);
        }


        void DrawEyesPage()
        {
            GUILayout.Label(L("Are these the eyes?", "ここが目で合っていますか？"), _big);
            GUILayout.Label(L("The found eyes are tinted in the Scene view: blue = left, orange = right. If a tint sits on the wrong spot or is missing, press Fix and click the iris.",
                              "見つけた目はシーンに色が付きます（青＝左、オレンジ＝右）。場所が違う、または色が無い場合は「直す」を押して目（虹彩）をクリックしてください。"), _hint);
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
                    GUILayout.Label(L("Click the iris in the Scene view. Each click adds one piece; press Esc or the button again when done.",
                                      "シーンで虹彩をクリックしてください。クリックごとに1つ追加されます。終わったら Esc か同じボタンを押します。"), _hint);
                bool anyPicks = false; foreach (var kv in seeds) anyPicks |= kv.Value.Count > 0;
                if (anyPicks)
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(pickMode == null || !seeds.TryGetValue(pickMode, out var l) || l.Count == 0))
                            if (GUILayout.Button(L("Undo last click", "最後のクリックを取り消す"))) RemoveLastPoint();
                        if (GUILayout.Button(L("Use auto again", "自動検出に戻す"))) { seeds.Clear(); previews.Clear(); pickMode = null; SceneView.RepaintAll(); }
                    }
            }

            advancedEyes = EditorGUILayout.Foldout(advancedEyes, L("Advanced", "詳細設定"), true);
            if (advancedEyes)
            {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                eyeRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(
                    GC("Eyes are on this mesh", "目があるメッシュ",
                       "Leave empty to search every body mesh. Set it when the eyes sit on a specific mesh.",
                       "空欄なら全てのボディメッシュを探します。目が特定のメッシュにある場合に指定します。"),
                    eyeRenderer, typeof(SkinnedMeshRenderer), true);
                if (EditorGUI.EndChangeCheck()) SaveSettings();
                EditorGUI.indentLevel--;
            }
        }

        string PickName(string key) => key == "L" ? L("left eye", "左目") : key == "R" ? L("right eye", "右目") : L("parts to hide", "隠す部分");

        void DrawPickButton(string label)
        {
            bool active = pickMode == label;
            int count = seeds.TryGetValue(label, out var l) ? l.Count : 0;
            GUI.backgroundColor = active ? new Color(0.6f, 0.8f, 1f) : (count > 0 ? new Color(0.6f, 0.9f, 0.6f) : Color.white);
            string side = label == "L" ? L("left", "左") : L("right", "右");
            string text = active ? L($"Clicking {side}… ({count})", $"{side}目を選択中… ({count})")
                        : count > 0 ? L($"{side} ✓ {count} picked", $"{side}目 ✓ {count}") : L($"Fix {side} eye", $"{side}目を直す");
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
            GUILayout.Label(L("How should the new eyes look?", "新しい目の見た目"), _big);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                previewOn = EditorGUILayout.ToggleLeft(GC("Preview in the Scene view", "シーンでプレビュー",
                    "Shows the result on the avatar as you change settings. Nothing is saved until you press Install.",
                    "設定を変えるたびに結果をアバターに表示します。「インストール」を押すまで何も保存されません。"), previewOn, GUILayout.Width(200));
                if (EditorGUI.EndChangeCheck()) { SaveSettings(); if (previewOn) MarkPreviewDirty(); else EndPreview(); }
                GUILayout.FlexibleSpace();
                GUILayout.Label(!previewOn ? "" : previewDirty ? L("Updating preview…", "プレビューを更新中…") : preview != null && !preview.IsEmpty ? L("Preview shown", "プレビュー表示中") : "", _hint);
            }
            EditorGUILayout.Space(4);

            using (new EditorGUILayout.VerticalScope(_box))
            {
                EditorGUI.BeginChangeCheck();
                material = (Material)EditorGUILayout.ObjectField(GC("Eye material", "目のマテリアル",
                    "One of the DocMe eye materials. You can swap it later on the renderer.", "DocMe の目マテリアル。後からレンダラーで差し替えできます。"),
                    material, typeof(Material), false);
                if (material == null) GUILayout.Label(L("Pick an eye material to continue.", "目のマテリアルを選んでください。"), _warn);

                EditorGUILayout.Space(6);
                GUILayout.Label(L("How to build the eye", "目の作り方"), EditorStyles.boldLabel);
                DrawModeChoice(false,
                    L("Flat eye in the socket", "まぶたの奥に平らな目"),
                    L("A new flat eye behind the eyelids, the old one hidden. Works on every avatar.", "まぶたの奥に新しい平らな目を置き、元の目は隠します。どのアバターでも使えます。"));
                DrawModeChoice(true,
                    L("Reuse the avatar's own eye", "アバターの目をそのまま使う"),
                    L("The original eye surface gets the new shader, so it still moves with blinks and expressions. Best when the old eye is a flat sheet.", "元の目の面に新しいシェーダーを当てます。まばたきや表情に合わせて動きます。元の目が平らな場合に向いています。"));

                EditorGUILayout.Space(6);
                if (lockAspect)
                {
                    float s = EditorGUILayout.Slider(GC("Size", "サイズ", "1.0 = same size as the avatar's eye.", "1.0 ＝ 元の目と同じ大きさ。"), widthFactor, 0.3f, 3f);
                    widthFactor = heightFactor = s;
                }
                else
                {
                    widthFactor  = EditorGUILayout.Slider(GC("Width", "幅"), widthFactor, 0.3f, 3f);
                    heightFactor = EditorGUILayout.Slider(GC("Height", "高さ"), heightFactor, 0.3f, 3f);
                }
                if (EditorGUI.EndChangeCheck()) { SaveSettings(); MarkPreviewDirty(); }
            }

            advancedLook = EditorGUILayout.Foldout(advancedLook, L("Advanced", "詳細設定"), true);
            if (advancedLook)
            {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                lockAspect = EditorGUILayout.ToggleLeft(GC("Same width and height", "幅と高さを同じにする"), lockAspect);
                depthOffset      = EditorGUILayout.Slider(GC("Depth", "前後", "Forward (+) or deeper (−), in metres.", "前へ(＋)／奥へ(−)、メートル。"), depthOffset, -0.02f, 0.02f);
                verticalOffset   = EditorGUILayout.Slider(GC("Up / down", "上下"), verticalOffset, -0.02f, 0.02f);
                horizontalOffset = EditorGUILayout.Slider(GC("Apart / together", "左右", "Apart (+) or toward the nose (−).", "外へ(＋)／鼻側へ(−)。"), horizontalOffset, -0.02f, 0.02f);
                rollOffset       = EditorGUILayout.Slider(GC("Roll", "回転"), rollOffset, -180f, 180f);
                EditorGUILayout.Space(4);

                if (!deformWithFace)
                {
                    eyeMesh = (Mesh)EditorGUILayout.ObjectField(GC("Custom eye mesh", "独自の目メッシュ",
                        "Empty = a generated flat quad. Use your own mesh if you have one.", "空欄なら平面を自動生成します。独自メッシュがあれば指定してください。"), eyeMesh, typeof(Mesh), false);
                    inSocket = EditorGUILayout.ToggleLeft(GC("Place behind the eyelids", "まぶたの内側に配置",
                        "Off puts the eye on the face surface instead of at the iris depth.", "オフにすると目を顔の表面に置きます。"), inSocket);
                    hideOriginal = EditorGUILayout.ToggleLeft(GC("Hide the original eyes", "元の目を隠す"), hideOriginal);
                    using (new EditorGUI.DisabledScope(!hideOriginal))
                    {
                        deleteOriginal = EditorGUILayout.ToggleLeft(GC("Delete them instead of folding them away", "畳まずに削除する",
                            "Folding uses a blendshape and can be undone. Deleting removes the triangles, which stops other blendshapes from pulling them back into view.",
                            "畳む方法はブレンドシェイプで、元に戻せます。削除はポリゴンを消すので、他のブレンドシェイプで再表示されることがありません。"), deleteOriginal);
                        using (new EditorGUI.DisabledScope(deleteOriginal))
                            sinkIntoHead = EditorGUILayout.ToggleLeft(GC("Fold them into the head", "頭の中に畳み込む"), sinkIntoHead);
                    }
                }
                else
                {
                    flattenEye = EditorGUILayout.ToggleLeft(GC("Flatten the eye", "目を平らにする", "Projects the eye onto a flat plane facing forward.", "目を正面向きの平面にそろえます。"), flattenEye);
                    using (new EditorGUI.DisabledScope(!flattenEye))
                        dropBackFaces = EditorGUILayout.ToggleLeft(GC("Remove the back half of a round eye", "丸い目の裏半分を削除",
                            "A ball-shaped eye would fold onto itself when flattened. Turn off if the eye is already flat.", "球状の目は平らにすると重なります。元から平らな目ならオフにしてください。"), dropBackFaces);
                    evenEyeShapes = EditorGUILayout.Popup(GC("Blendshapes scale the eye evenly", "ブレンドシェイプで目を均等に変形",
                        "Rewrites the avatar's blendshapes so they move the new eye as one flat sheet. A shape that pinches one corner would warp the parallax.",
                        "ブレンドシェイプが新しい目を平らなまま均等に動かすように書き換えます。一部だけをつまむシェイプは視差を歪めます。"),
                        evenEyeShapes, new[] { L("Off (as authored)", "オフ（元のまま）"), L("Mean fit", "平均"), L("Max extent", "最大") });
                    if (evenEyeShapes != 0 && previewOn)
                        GUILayout.Label(L("The preview keeps the blendshapes as authored. The evening is applied when you install.",
                                          "プレビューではブレンドシェイプは元のままです。均等化はインストール時に適用されます。"), _hint);
                }
                if (EditorGUI.EndChangeCheck()) { SaveSettings(); MarkPreviewDirty(); }
                EditorGUI.indentLevel--;
            }

            showPresets = EditorGUILayout.Foldout(showPresets, L("Presets", "プリセット"), true);
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

            GUILayout.Label(L("✓ Eyes installed", "✓ 目を入れました"), _ok);
            GUILayout.Label(L("Look at the avatar in the Scene view. Everything below is optional.", "シーンでアバターを確認してください。以下はすべて任意です。"), _hint);

            if (DocMeReapply.IsStale(avatar))
            {
                EditorGUILayout.HelpBox(L("The model file changed since the eyes were installed. Re-apply to rebuild them on the new mesh.",
                                          "目を入れた後にモデルファイルが変わりました。新しいメッシュに作り直すには再適用してください。"), MessageType.Warning);
                if (GUILayout.Button(L("Re-apply to the updated model", "更新されたモデルに再適用"), GUILayout.Height(24)))
                {
                    log = DocMeReapply.ReapplyAll(avatar);
                    seeds.Clear(); previews.Clear(); showOriginal = false;
                    hides.Reload();
                }
            }

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.VerticalScope(_box))
            {
                GUILayout.Label(L("Something still showing over the eye?", "目の上にまだ何か見えていますか？"), EditorStyles.boldLabel);
                GUILayout.Label(L("Highlights or decorations from the old eye can sit on top of the new one. Click them in the Scene view and they disappear.",
                                  "元の目のハイライトや飾りが新しい目の上に残ることがあります。シーンでクリックすると消えます。"), _hint);
                bool hiding = pickMode == HideKey;
                int hidden = HiddenCount();
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUI.backgroundColor = hiding ? new Color(0.6f, 0.8f, 1f) : Color.white;
                    if (GUILayout.Button(hiding ? L("Clicking… press when done", "選択中… 終わったら押す") : L("Click parts to hide", "隠す部分をクリック"), GUILayout.Height(26)))
                    { pickMode = hiding ? null : HideKey; SceneView.RepaintAll(); }
                    GUI.backgroundColor = Color.white;
                    using (new EditorGUI.DisabledScope(hidden == 0))
                    {
                        if (GUILayout.Button(L("Undo last", "最後を取り消す"), GUILayout.Width(90), GUILayout.Height(26))) UndoLastHide();
                        if (GUILayout.Button(L("Show all", "全て表示"), GUILayout.Width(80), GUILayout.Height(26))) ClearHides();
                    }
                }
                if (hidden > 0) GUILayout.Label(L($"{hidden} {(hidden == 1 ? "part" : "parts")} hidden.", $"{hidden} 箇所を隠しました。"), _ok);
            }

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.VerticalScope(_box))
            {
                GUILayout.Label(L("Fine-tune", "微調整"), EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                if (lockAspect)
                {
                    float s = EditorGUILayout.Slider(GC("Size", "サイズ"), widthFactor, 0.3f, 3f);
                    widthFactor = heightFactor = s;
                }
                else
                {
                    widthFactor  = EditorGUILayout.Slider(GC("Width", "幅"), widthFactor, 0.3f, 3f);
                    heightFactor = EditorGUILayout.Slider(GC("Height", "高さ"), heightFactor, 0.3f, 3f);
                }
                depthOffset      = EditorGUILayout.Slider(GC("Depth", "前後"), depthOffset, -0.02f, 0.02f);
                verticalOffset   = EditorGUILayout.Slider(GC("Up / down", "上下"), verticalOffset, -0.02f, 0.02f);
                horizontalOffset = EditorGUILayout.Slider(GC("Apart / together", "左右"), horizontalOffset, -0.02f, 0.02f);
                bool changed = EditorGUI.EndChangeCheck();
                if (changed) SaveSettings();
                if (changed && !deformWithFace) UpdateFit();
                if (deformWithFace)
                {
                    GUILayout.Label(L("Reused eyes are rebuilt when you press Apply.", "再利用した目は「反映」を押すと作り直されます。"), _hint);
                    if (GUILayout.Button(L("Apply", "反映"), GUILayout.Height(24))) Install();
                }

                EditorGUI.BeginChangeCheck();
                showOriginal = EditorGUILayout.ToggleLeft(GC("Show the original eyes for comparison", "比較のため元の目を表示"), showOriginal);
                if (EditorGUI.EndChangeCheck()) SetOriginalEyesVisible(showOriginal);
            }

            advancedDone = EditorGUILayout.Foldout(advancedDone, L("Advanced", "詳細設定"), true);
            if (advancedDone)
            {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                hides.deleteGeometry = EditorGUILayout.ToggleLeft(GC("Delete hidden parts instead of folding them", "隠す部分を畳まずに削除",
                    "Deleting stops other blendshapes from pulling them back into view.", "削除すると他のブレンドシェイプで再表示されなくなります。"), hides.deleteGeometry);
                using (new EditorGUI.DisabledScope(hides.deleteGeometry))
                    hides.sinkIntoHead = EditorGUILayout.ToggleLeft(GC("Fold hidden parts into the head", "隠す部分を頭の中に畳み込む"), hides.sinkIntoHead);
                if (EditorGUI.EndChangeCheck()) { SaveSettings(); if (hides.Count > 0) hides.Reapply(); }

                DocMeReapply.AutoEnabled = EditorGUILayout.ToggleLeft(GC("Rebuild automatically when the model is reimported", "モデルの再インポート時に自動で作り直す",
                    "Edit the FBX in Blender and reimport: the eyes and hidden parts come back on the new mesh.",
                    "Blender で FBX を編集して再インポートすると、目と隠した部分が新しいメッシュに戻ります。"), DocMeReapply.AutoEnabled);

                EditorGUILayout.Space(6);
                GUI.backgroundColor = new Color(0.92f, 0.55f, 0.55f);
                if (GUILayout.Button(L("Remove the eyes (back to the original)", "目を削除（元に戻す）"), GUILayout.Height(24)))
                {
                    HideIslandsCore.Revert(avatar);
                    EyeInstallerCore.Revert(avatar);
                    hides.Forget(); previews.Clear(); showOriginal = false; pickMode = null;
                    log = L("Removed. The avatar is back to its original eyes.", "削除しました。アバターは元の目に戻っています。");
                    GoTo(Page.Look);
                }
                GUI.backgroundColor = Color.white;
                EditorGUI.indentLevel--;
            }
        }

        int HiddenCount() => hides.Count;
        void UndoLastHide() => hides.UndoLast();
        void ClearHides() => hides.Clear();

        void AddHide(EyeSeed seed)
        {
            switch (hides.Add(seed))
            {
                case HidePickSession.AddResult.Hidden: break;
                case HidePickSession.AddResult.IsNewEye: log = L("That is the new eye itself, so it stays.", "それは新しい目なので隠しません。"); break;
                default: log = L("That part is too big to hide. Click a small piece such as a highlight.", "その部分は大きすぎて隠せません。ハイライトなど小さな部分をクリックしてください。"); break;
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
                if (seeds.TryGetValue(d.label, out var list) && list.Count > 0) d.seeds = new List<EyeSeed>(list);
            EyeInstallerCore.Install(avatar, detected, BuildOptions());
            SaveSettings();
            showOriginal = false;

            log = DescribeInstall();
            if (keepHides != null && keepHides.Count > 0)
            {
                log += "\n" + DocMeReapply.ReapplyHide(avatar, keepHides, keepDelete, keepSink);
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

            EditorUtility.DisplayProgressBar("DocMe Eyes", L("Building preview…", "プレビューを作成中…"), 0.4f);
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
                    if (seeds.TryGetValue(d.label, out var list) && list.Count > 0) d.seeds = new List<EyeSeed>(list);
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
            eyeRenderers = eyeRenderer != null ? new List<SkinnedMeshRenderer> { eyeRenderer } : null,
        };

        bool HasInstall() => avatar != null && avatar.GetComponent<DocMeEyeInstall>() != null;

        string DescribeInstall()
        {
            var sb = new System.Text.StringBuilder();
            if (detected != null)
                foreach (var d in detected)
                {
                    string src = d.HasSeed ? L($"{d.seeds.Count} click(s)", $"クリック {d.seeds.Count} 回") : L("auto", "自動");
                    sb.AppendLine($"[{d.label}] {src}");
                    if (d.HasSeed)
                        foreach (var s in d.seeds)
                            if (s.renderer == null || !s.renderer.transform.IsChildOf(avatar.transform))
                                sb.AppendLine(L($"  ⚠ a click landed on '{(s.renderer ? s.renderer.transform.root.name : "a deleted object")}', not on this avatar. Use auto again and re-click.",
                                                $"  ⚠ クリックがこのアバター外（'{(s.renderer ? s.renderer.transform.root.name : "削除済み")}'）にあります。自動検出に戻してから選び直してください。"));
                }

            var marker = avatar != null ? avatar.GetComponent<DocMeEyeInstall>() : null;
            if (marker == null) return sb.Append(L("⚠ Nothing was installed.", "⚠ 何もインストールされませんでした。")).ToString();

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
                    ? L($"Changed '{where}': new eye {eyeTris} triangles, {before - after} removed.", $"'{where}' を変更：新しい目 {eyeTris} ポリゴン、削除 {before - after} ポリゴン。")
                    : L($"Changed '{where}': original eyes hidden ({before - after} triangles removed).", $"'{where}' を変更：元の目を非表示（削除 {before - after} ポリゴン）。"));
                if (!h.renderer.gameObject.activeInHierarchy)
                    sb.AppendLine(L("  ⚠ that mesh is hidden in the scene, so you won't see the change.", "  ⚠ このメッシュはシーンで非表示のため、変化が見えません。"));
            }
            if (marker.createdEyes.Count > 0)
                sb.AppendLine(L($"Placed {marker.createdEyes.Count} eye {(marker.createdEyes.Count == 1 ? "object" : "objects")}.", $"目のオブジェクトを {marker.createdEyes.Count} 個配置しました。"));
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

            foreach (var c in marker.createdEyes)
            {
                if (c.go == null) continue;
                var d = fresh.Find(e => e.label == c.label);
                if (d != null) EyeInstallerCore.ApplyFit(c.go.transform, d, opt, c.depth);
            }
            EditorUtility.SetDirty(avatar);
        }


        void DrawPresetBar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                preset = (DocMeEyePreset)EditorGUILayout.ObjectField(GC("Preset", "プリセット", "Saved settings. Choosing one loads it.", "保存した設定。選ぶと読み込まれます。"),
                    preset, typeof(DocMeEyePreset), false);
                if (EditorGUI.EndChangeCheck() && preset != null) LoadPreset(preset);
                using (new EditorGUI.DisabledScope(preset == null))
                    if (GUILayout.Button(GC("Save", "保存", "Overwrite this preset with the current settings.", "現在の設定でこのプリセットを上書きします。"), GUILayout.Width(56))) StoreInto(preset);
                if (GUILayout.Button(GC("New…", "新規…", "Save the current settings as a new preset.", "現在の設定を新しいプリセットとして保存します。"), GUILayout.Width(56))) SaveAsNewPreset();
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
            SaveSettings();
            MarkPreviewDirty();
            log = L($"Loaded preset '{p.name}'.", $"プリセット「{p.name}」を読み込みました。");
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
            EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssets();
            log = L($"Saved preset '{p.name}'.", $"プリセット「{p.name}」を保存しました。");
        }

        void SaveAsNewPreset()
        {
            string suggested = avatar != null ? avatar.name : "DocMe Eye Preset";
            string path = EditorUtility.SaveFilePanelInProject(L("Save Eye Preset", "プリセットを保存"), suggested, "asset",
                L("Save the current settings as a reusable preset.", "現在の設定をプリセットとして保存します。"));
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

            if (pickMode == null) return;
            sv.Repaint();

            Handles.BeginGUI();
            int picked = pickMode == HideKey ? HiddenCount() : seeds.TryGetValue(pickMode, out var cur) ? cur.Count : 0;
            GUI.Label(new Rect(8, 8, 520, 22), L(
                $"Click the {PickName(pickMode)} ({picked} so far). Esc when done, Ctrl+Z undoes the last click.",
                $"{PickName(pickMode)}をクリック（{picked} 個）。終わったら Esc、Ctrl+Z で最後のクリックを取り消し。"));
            Handles.EndGUI();

            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { pickMode = null; e.Use(); Repaint(); return; }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Z && (e.control || e.command)) { RemoveLastPoint(); e.Use(); Repaint(); return; }

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (MeshPickUtil.Raycast(avatar, e.mousePosition, out var r, out int vert, out Vector3 pt, out string why))
                {
                    var seed = new EyeSeed { renderer = r, vertex = vert, point = pt };
                    if (pickMode == HideKey) AddHide(seed);
                    else
                    {
                        if (!seeds.TryGetValue(pickMode, out var list)) seeds[pickMode] = list = new List<EyeSeed>();
                        list.Add(seed);
                    }
                }
                else log = why;
                e.Use(); Repaint(); SceneView.RepaintAll();
            }
        }
    }
}
