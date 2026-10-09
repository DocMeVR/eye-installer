using UnityEditor;
using UnityEngine;

namespace DocMe.EyeInstaller
{
    /// <summary>Standalone click-to-hide for any avatar mesh part (highlights, decorations). Same engine as
    /// the installer's Done page; the two can be used on the same avatar.</summary>
    public class HideIslandsWindow : EditorWindow
    {
        const string PK = "DocMe.IslandHide.";
        const string LangKey = "DocMe.EyeInstaller.japanese";

        GameObject avatar;
        bool japanese;
        bool picking;
        bool advanced;
        string log = "";
        Vector2 scroll;
        readonly HidePickSession hides = new HidePickSession();
        readonly IslandPreview previews = new IslandPreview();

        [MenuItem("Tools/DocMe/Hide Mesh Islands")]
        static void Open()
        {
            var w = GetWindow<HideIslandsWindow>("Hide Islands");
            w.minSize = new Vector2(360, 300);
        }

        void OnEnable()
        {
            japanese = EditorPrefs.GetBool(LangKey, false);
            hides.deleteGeometry = EditorPrefs.GetBool(PK + "delete", false);
            hides.sinkIntoHead = EditorPrefs.GetBool(PK + "sink", true);
            if (avatar == null) avatar = LoadSavedAvatar() ?? AvatarFromSelection();
            OnAvatarSet();
            SceneView.duringSceneGui += OnSceneGUI;
        }

        void OnDisable()
        {
            Save();
            SceneView.duringSceneGui -= OnSceneGUI;
            previews.Clear();
            picking = false;
        }

        void Save()
        {
            EditorPrefs.SetBool(LangKey, japanese);
            EditorPrefs.SetBool(PK + "delete", hides.deleteGeometry);
            EditorPrefs.SetBool(PK + "sink", hides.sinkIntoHead);
            EditorPrefs.SetString(PK + "avatar", avatar != null ? GlobalObjectId.GetGlobalObjectIdSlow(avatar).ToString() : "");
        }

        static GameObject LoadSavedAvatar()
        {
            string id = EditorPrefs.GetString(PK + "avatar", "");
            if (string.IsNullOrEmpty(id) || !GlobalObjectId.TryParse(id, out var gid)) return null;
            return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid) as GameObject;
        }

        static GameObject AvatarFromSelection()
        {
            for (var p = Selection.activeTransform; p != null; p = p.parent)
            {
                var anim = p.GetComponent<Animator>();
                if (anim != null && anim.isHuman) return p.gameObject;
            }
            return null;
        }

        void OnAvatarSet()
        {
            hides.avatar = avatar;
            hides.Forget();
            previews.Clear();
            picking = false;
            log = "";
            if (avatar != null && hides.Count > 0) hides.Reload();
            Save();
            SceneView.RepaintAll();
        }

        string L(string en, string jp) => japanese ? jp : en;
        GUIContent GC(string en, string jp, string tEn = null, string tJp = null)
            => new GUIContent(L(en, jp), tEn == null ? null : L(tEn, tJp));

        GUIStyle _hint, _ok, _box;
        void EnsureStyles()
        {
            if (_box != null) return;
            _hint = new GUIStyle(EditorStyles.label) { wordWrap = true };
            _hint.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.75f, 0.75f, 0.75f) : new Color(0.3f, 0.3f, 0.3f);
            _ok = new GUIStyle(EditorStyles.label) { wordWrap = true, fontStyle = FontStyle.Bold };
            _ok.normal.textColor = new Color(0.35f, 0.75f, 0.4f);
            _box = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(12, 12, 10, 10) };
        }

        void OnGUI()
        {
            EnsureStyles();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(L("Hide Mesh Islands", "メッシュの一部を非表示"), new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 });
                GUILayout.FlexibleSpace();
                int cur = japanese ? 1 : 0;
                int sel = GUILayout.Toolbar(cur, new[] { "EN", "日本語" }, GUILayout.Width(110), GUILayout.Height(20));
                if (sel != cur) { japanese = sel == 1; Save(); GUIUtility.keyboardControl = 0; }
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.Space(6);
            GUILayout.Label(L("Hide any small part of a mesh: a highlight, a sticker, a decoration. Each click hides one connected piece. Nothing is lost; Show all brings everything back.",
                              "メッシュの小さな部分（ハイライト、シール、飾りなど）を隠します。クリックごとに繋がった1つの部分が消えます。消えたものは「全て表示」で戻せます。"), _hint);
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

                EditorGUILayout.Space(6);
                int hidden = hides.Count;
                using (new EditorGUI.DisabledScope(avatar == null))
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUI.backgroundColor = picking ? new Color(0.6f, 0.8f, 1f) : Color.white;
                    if (GUILayout.Button(picking ? L("Clicking… press when done", "選択中… 終わったら押す") : L("Click parts to hide", "隠す部分をクリック"), GUILayout.Height(26)))
                    { picking = !picking; SceneView.RepaintAll(); }
                    GUI.backgroundColor = Color.white;
                    using (new EditorGUI.DisabledScope(hidden == 0))
                    {
                        if (GUILayout.Button(L("Undo last", "最後を取り消す"), GUILayout.Width(90), GUILayout.Height(26))) hides.UndoLast();
                        if (GUILayout.Button(L("Show all", "全て表示"), GUILayout.Width(80), GUILayout.Height(26))) hides.Clear();
                    }
                }
                if (picking)
                    GUILayout.Label(L("Click the part in the Scene view. Esc or the button ends picking; Ctrl+Z undoes the last click.",
                                      "シーンで部分をクリックしてください。Esc かボタンで終了、Ctrl+Z で最後のクリックを取り消し。"), _hint);
                if (hidden > 0) GUILayout.Label(L($"{hidden} {(hidden == 1 ? "part" : "parts")} hidden.", $"{hidden} 箇所を隠しました。"), _ok);
                if (!string.IsNullOrEmpty(log)) GUILayout.Label(log, _hint);
            }

            advanced = EditorGUILayout.Foldout(advanced, L("Advanced", "詳細設定"), true);
            if (advanced)
            {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                hides.deleteGeometry = EditorGUILayout.ToggleLeft(GC("Delete hidden parts instead of folding them", "隠す部分を畳まずに削除",
                    "Folding uses a blendshape. Deleting removes the triangles, which stops other blendshapes from pulling them back into view.",
                    "畳む方法はブレンドシェイプです。削除はポリゴンを消すので、他のブレンドシェイプで再表示されなくなります。"), hides.deleteGeometry);
                using (new EditorGUI.DisabledScope(hides.deleteGeometry))
                    hides.sinkIntoHead = EditorGUILayout.ToggleLeft(GC("Fold hidden parts into the head", "隠す部分を頭の中に畳み込む"), hides.sinkIntoHead);
                if (EditorGUI.EndChangeCheck()) { Save(); if (hides.Count > 0) hides.Reapply(); }
                DocMeReapply.AutoEnabled = EditorGUILayout.ToggleLeft(GC("Rebuild automatically when the model is reimported", "モデルの再インポート時に自動で作り直す"), DocMeReapply.AutoEnabled);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndScrollView();
        }

        void OnSceneGUI(SceneView sv)
        {
            if (avatar == null) return;

            if (picking)
            {
                var hi = new Color(1f, 0.35f, 0.35f);
                Handles.color = hi;
                foreach (var p in hides.Picks)
                    Handles.SphereHandleCap(0, p.point, Quaternion.identity, HandleUtility.GetHandleSize(p.point) * 0.05f, EventType.Repaint);
            }

            if (!picking) return;
            sv.Repaint();

            Handles.BeginGUI();
            GUI.Label(new Rect(8, 8, 520, 22), L($"Click parts to hide ({hides.Count} hidden). Esc when done, Ctrl+Z undoes the last click.",
                                                  $"隠す部分をクリック（{hides.Count} 箇所）。終わったら Esc、Ctrl+Z で最後のクリックを取り消し。"));
            Handles.EndGUI();
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { picking = false; e.Use(); Repaint(); return; }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Z && (e.control || e.command)) { hides.UndoLast(); e.Use(); Repaint(); return; }

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (MeshPickUtil.Raycast(avatar, e.mousePosition, out var r, out int vert, out Vector3 pt, out string why))
                {
                    switch (hides.Add(new EyeSeed { renderer = r, vertex = vert, point = pt }))
                    {
                        case HidePickSession.AddResult.Hidden: log = ""; break;
                        case HidePickSession.AddResult.IsNewEye: log = L("That is the installed eye, so it stays.", "それはインストール済みの目なので隠しません。"); break;
                        default: log = L("That part is too big to hide. Click a small piece such as a highlight.", "その部分は大きすぎて隠せません。ハイライトなど小さな部分をクリックしてください。"); break;
                    }
                }
                else log = why;
                e.Use(); Repaint(); SceneView.RepaintAll();
            }
        }
    }
}
