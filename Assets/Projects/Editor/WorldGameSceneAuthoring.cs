using Projects.Scripts.GamePlay;
using Projects.Scripts.GamePlay.Board;
using Projects.Scripts.GamePlay.Configs;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Projects.Editor
{
    /// <summary>Creates editable prefab assets and places their instances in GameScene.</summary>
    public static class WorldGameSceneAuthoring
    {
        private const string PrefabFolder = "Assets/Projects/Prefabs/GamePlay";
        private const string ScenePath = "Assets/Projects/Scenes/BootScene/GameScene.unity";
        private const string CatPath = "Assets/Projects/Art/Build/CatBase/cat_base_idle_v3.png";
        private const string FallbackCatPath = "Assets/Projects/Art/Build/CatBase/cat_base_idle_v2.png";
        private const string BoardSquarePath = "Assets/Projects/Art/Build/Board/board_square_v1.png";
        private const string CatConfigPath = "Assets/Projects/Assets/Configuration/CatConfig.asset";
        private const string BrickConfigPath = "Assets/Projects/Assets/Configuration/BrickConfig.asset";
        private const string StageBalanceConfigPath = "Assets/Projects/Assets/Configuration/StageBalanceConfig.asset";
        private const string DropCameraConfigPath = "Assets/Projects/Assets/Configuration/DropCameraConfig.asset";
        private static readonly Color Ink = new Color(.31f, .25f, .26f);

        [MenuItem("Tools/Cat Merge Drop/Build World Game Scene")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new System.InvalidOperationException("Stop Play mode before building GameScene.");

            Sprite catSprite = AssetDatabase.LoadAssetAtPath<Sprite>(CatPath)
                ?? AssetDatabase.LoadAssetAtPath<Sprite>(FallbackCatPath);
            Sprite boardSquare = AssetDatabase.LoadAssetAtPath<Sprite>(BoardSquarePath);
            CatConfig catConfig = AssetDatabase.LoadAssetAtPath<CatConfig>(CatConfigPath);
            BrickConfig brickConfig = AssetDatabase.LoadAssetAtPath<BrickConfig>(BrickConfigPath);
            StageBalanceConfig stageBalanceConfig = AssetDatabase.LoadAssetAtPath<StageBalanceConfig>(StageBalanceConfigPath);
            DropCameraConfig dropCameraConfig = AssetDatabase.LoadAssetAtPath<DropCameraConfig>(DropCameraConfigPath);
            Sprite square = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            Sprite firstBrick = brickConfig != null ? brickConfig.SpriteFor(1) : null;
            if (catSprite == null || boardSquare == null || catConfig == null || brickConfig == null || stageBalanceConfig == null || dropCameraConfig == null || square == null || firstBrick == null)
                throw new System.InvalidOperationException("Cat sprite, board square, level-one Brick, or gameplay configs are missing.");

            EnsureFolder("Assets/Projects/Prefabs");
            EnsureFolder(PrefabFolder);
            GameObject cellPrefab = BuildCellPrefab(boardSquare, catSprite);
            GameObject brickPrefab = BuildBrickPrefab(firstBrick);
            GameObject goalPrefab = BuildGoalPrefab(square);
            GameObject hudPrefab = BuildHudPrefab();

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject scope = GameObject.Find("GameScope");
            if (scope == null) throw new System.InvalidOperationException("GameScene GameScope is missing.");
            string[] generatedRoots = { "MergeBoard", "TileBlocks", "DropCats", "GoalLine", "DropLeftWall", "DropRightWall", "GameplayHUD" };
            for (int i = scope.transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = scope.transform.GetChild(i).gameObject;
                if (System.Array.IndexOf(generatedRoots, child.name) >= 0)
                    Object.DestroyImmediate(child);
            }

            MergeBoardController screen = scope.GetComponent<MergeBoardController>();
            if (screen == null) screen = scope.AddComponent<MergeBoardController>();
            DropStageController presenter = scope.GetComponent<DropStageController>();
            if (presenter == null) presenter = scope.AddComponent<DropStageController>();
            DropCameraController cameraRig = scope.GetComponent<DropCameraController>();
            if (cameraRig == null) cameraRig = scope.AddComponent<DropCameraController>();
            SetObjects(cameraRig, ("config", dropCameraConfig));

            Transform boardRoot = NewRoot("MergeBoard", scope.transform);
            MergeBoardCell[] cells = new MergeBoardCell[MergeBoardModel.SlotCount];
            DropCatView[] cats = new DropCatView[MergeBoardModel.SlotCount];
            for (int i = 0; i < cells.Length; i++)
            {
                GameObject instance = Instance(cellPrefab, boardRoot, $"Cell_{i:00}");
                instance.transform.localPosition = new Vector3(
                    WorldPlayfieldLayout.ColumnX(i % MergeBoardModel.Columns),
                    WorldPlayfieldLayout.BoardRowY(i / MergeBoardModel.Columns), 0);
                cells[i] = instance.GetComponent<MergeBoardCell>();
                SetInt(cells[i], "slot", i);
                cats[i] = instance.GetComponentInChildren<DropCatView>(true);
                SetInt(cats[i], "slot", i);
            }

            Transform tileRoot = NewRoot("TileBlocks", scope.transform);
            DropBrickView[] bricks = new DropBrickView[WorldPlayfieldLayout.BrickCount];
            for (int i = 0; i < bricks.Length; i++)
            {
                int row = i / MergeBoardModel.Columns;
                int column = i % MergeBoardModel.Columns;
                GameObject instance = Instance(brickPrefab, tileRoot, $"Tile_{row}_{column}");
                instance.transform.localPosition = new Vector3(WorldPlayfieldLayout.ColumnX(column),
                    WorldPlayfieldLayout.DropY(WorldPlayfieldLayout.TileProgress(row)), 0);
                bricks[i] = instance.GetComponent<DropBrickView>();
                SetInt(bricks[i], "row", row);
                SetInt(bricks[i], "column", column);
            }

            GameObject goal = Instance(goalPrefab, scope.transform, "GoalLine");
            goal.transform.localPosition = new Vector3(0,
                WorldPlayfieldLayout.DropY(WorldPlayfieldLayout.GoalProgress), 0);

            GameObject ghost = NewObject("DragGhost", boardRoot);
            SpriteRenderer ghostSprite = ghost.AddComponent<SpriteRenderer>();
            ghostSprite.sprite = catSprite;
            ghostSprite.sortingOrder = 30;
            ghost.transform.localScale = Vector3.one * (WorldPlayfieldLayout.CatVisualSize /
                Mathf.Max(catSprite.bounds.size.x, catSprite.bounds.size.y));
            ghost.SetActive(false);

            GameObject hud = Instance(hudPrefab, scope.transform, "GameplayHUD");
            Text status = hud.transform.Find("StatusText").GetComponent<Text>();
            Text counter = hud.transform.Find("Header/HudText").GetComponent<Text>();
            Button add = hud.transform.Find("Controls/AddButton").GetComponent<Button>();
            Button drop = hud.transform.Find("Controls/DropButton").GetComponent<Button>();
            Button sell = hud.transform.Find("Controls/SellButton").GetComponent<Button>();
            Button autoMerge = hud.transform.Find("Controls/AutoMergeButton").GetComponent<Button>();
            Button autoSummon = hud.transform.Find("Controls/AutoSummonButton").GetComponent<Button>();

            SetObjectArray(screen, "cells", cells);
            SetObjects(screen, ("dropController", presenter),
                ("cameraRig", cameraRig), ("catConfig", catConfig), ("stageBalanceConfig", stageBalanceConfig), ("dragGhost", ghostSprite),
                ("status", status), ("addButton", add), ("dropButton", drop), ("sellButton", sell),
                ("autoMergeButton", autoMerge), ("autoSummonButton", autoSummon));
            SetObjectArray(presenter, "bricks", bricks);
            SetObjectArray(presenter, "cats", cats);
            SetObjects(presenter, ("hud", counter), ("cameraRig", cameraRig),
                ("catConfig", catConfig), ("brickConfig", brickConfig), ("stageBalanceConfig", stageBalanceConfig));

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.DeleteAsset($"{PrefabFolder}/DropCat.prefab");
            AssetDatabase.SaveAssets();
            Debug.Log($"World GameScene built: {cells.Length} merge cells, {bricks.Length} tiles, " +
                $"board background {boardSquare.name}, cats reused for Drop. Scene: {ScenePath}");
        }

        [MenuItem("Tools/Cat Merge Drop/Refresh Gameplay HUD")]
        public static void RefreshGameplayHud()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new System.InvalidOperationException("Stop Play mode before refreshing GameplayHUD.");

            BuildHudPrefab();
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject scope = GameObject.Find("GameScope");
            MergeBoardController screen = scope != null ? scope.GetComponent<MergeBoardController>() : null;
            Transform hud = scope != null ? scope.transform.Find("GameplayHUD") : null;
            if (screen == null || hud == null)
                throw new System.InvalidOperationException("GameScene GameScope or GameplayHUD is missing.");

            SetObjects(screen,
                ("autoMergeButton", hud.Find("Controls/AutoMergeButton").GetComponent<Button>()),
                ("autoSummonButton", hud.Find("Controls/AutoSummonButton").GetComponent<Button>()));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        private static GameObject BuildCellPrefab(Sprite square, Sprite catSprite)
        {
            GameObject root = NewObject("MergeBoardCell");
            BoxCollider2D hit = root.AddComponent<BoxCollider2D>();
            hit.size = new Vector2(WorldPlayfieldLayout.BoardCellWidth,
                WorldPlayfieldLayout.BoardCellHeight);
            MergeBoardCell view = root.AddComponent<MergeBoardCell>();
            SpriteRenderer background = SpriteChild(root.transform, "CellBackground", square,
                new Color(.91f, .89f, .85f), 0, new Vector3(
                    WorldPlayfieldLayout.BoardCellWidth, WorldPlayfieldLayout.BoardCellHeight, 1));
            Transform catVisual = NewRoot("CatVisual", root.transform);
            DropCatView dropView = catVisual.gameObject.AddComponent<DropCatView>();
            SpriteRenderer cat = SpriteChild(catVisual, "Cat", catSprite, Color.white, 2,
                new Vector3(WorldPlayfieldLayout.CatVisualSize, WorldPlayfieldLayout.CatVisualSize, 1));
            cat.transform.localPosition = new Vector3(0, WorldPlayfieldLayout.CatVisualOffsetY, 0);
            SetObjects(view, ("background", background), ("cat", cat), ("hitArea", hit));
            SetObjects(dropView, ("cat", cat));
            return SavePrefab(root, "MergeBoardCell");
        }

        private static GameObject BuildBrickPrefab(Sprite firstBrick)
        {
            GameObject root = NewObject("DropBrick");
            DropBrickView view = root.AddComponent<DropBrickView>();
            SpriteRenderer block = SpriteChild(root.transform, "Block", firstBrick,
                Color.white, 0,
                new Vector3(WorldPlayfieldLayout.TileWidth, WorldPlayfieldLayout.TileHeight, 1));
            SetObjects(view, ("block", block));
            return SavePrefab(root, "DropBrick");
        }

        private static GameObject BuildGoalPrefab(Sprite square)
        {
            GameObject root = NewObject("GoalLine");
            SpriteChild(root.transform, "Line", square, new Color(.95f, .42f, .42f), 1,
                new Vector3(WorldPlayfieldLayout.BoardHalfWidth * 2f - .1f, .11f, 1));
            TextMesh label = LabelChild(root.transform, "Label", "GOAL", .07f, 2);
            label.transform.localPosition = new Vector3(0, -.35f, 0);
            return SavePrefab(root, "GoalLine");
        }

        private static GameObject BuildHudPrefab()
        {
            GameObject root = NewObject("GameplayHUD");
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            root.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            root.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1080, 1920);
            root.GetComponent<CanvasScaler>().matchWidthOrHeight = .5f;
            root.AddComponent<GraphicRaycaster>();

            GameObject header = UiObject("Header", root.transform);
            StretchTop(header.GetComponent<RectTransform>(), 150);
            Image headerImage = header.AddComponent<Image>();
            headerImage.color = new Color(.98f, .95f, .91f, .93f);
            Text hud = UiText("HudText", header.transform,
                $"GOLD 0   •   ROW 1/{WorldPlayfieldLayout.BrickRows}", 36);
            hud.alignment = TextAnchor.MiddleCenter;
            Stretch(hud.rectTransform);

            Text status = UiText("StatusText", root.transform, "", 28);
            status.color = Color.white;
            status.alignment = TextAnchor.MiddleCenter;
            status.rectTransform.anchorMin = new Vector2(.08f, 1);
            status.rectTransform.anchorMax = new Vector2(.92f, 1);
            status.rectTransform.pivot = new Vector2(.5f, 1);
            status.rectTransform.anchoredPosition = new Vector2(0, -155);
            status.rectTransform.sizeDelta = new Vector2(0, 115);

            GameObject controls = UiObject("Controls", root.transform);
            RectTransform controlsRect = controls.GetComponent<RectTransform>();
            controlsRect.anchorMin = new Vector2(.5f, 0);
            controlsRect.anchorMax = new Vector2(.5f, 0);
            controlsRect.pivot = new Vector2(.5f, 0);
            controlsRect.anchoredPosition = new Vector2(0, 50);
            controlsRect.sizeDelta = new Vector2(950, 300);
            Button add = UiButton("AddButton", controls.transform, "고양이 추가", new Color(.66f, .79f, .86f));
            Button drop = UiButton("DropButton", controls.transform, "DROP", new Color(.96f, .70f, .46f));
            Button sell = UiButton("SellButton", controls.transform, "판매", new Color(.91f, .58f, .54f));
            Button autoMerge = UiButton("AutoMergeButton", controls.transform, "자동 머지", new Color(.67f, .82f, .64f));
            Button autoSummon = UiButton("AutoSummonButton", controls.transform, "빈칸 자동 소환", new Color(.73f, .68f, .88f));
            Button[] controlButtons = { add, drop, sell };
            for (int i = 0; i < controlButtons.Length; i++)
            {
                controlButtons[i].GetComponent<RectTransform>().anchoredPosition = new Vector2(-310 + i * 310, 0);
                controlButtons[i].GetComponent<RectTransform>().sizeDelta = new Vector2(285, 132);
            }
            autoMerge.GetComponent<RectTransform>().anchoredPosition = new Vector2(-235, 145);
            autoMerge.GetComponent<RectTransform>().sizeDelta = new Vector2(445, 112);
            autoSummon.GetComponent<RectTransform>().anchoredPosition = new Vector2(235, 145);
            autoSummon.GetComponent<RectTransform>().sizeDelta = new Vector2(445, 112);
            return SavePrefab(root, "GameplayHUD");
        }

        private static Button UiButton(string name, Transform parent, string caption, Color color)
        {
            GameObject root = UiObject(name, parent);
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(445, 132);
            Image image = root.AddComponent<Image>();
            image.color = color;
            Button button = root.AddComponent<Button>();
            button.targetGraphic = image;
            Text text = UiText("Label", root.transform, caption, 36);
            text.alignment = TextAnchor.MiddleCenter;
            Stretch(text.rectTransform);
            return button;
        }

        private static Text UiText(string name, Transform parent, string content, int size)
        {
            GameObject go = UiObject(name, parent);
            Text text = go.AddComponent<Text>();
            text.text = content;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.color = Ink;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            return text;
        }

        private static GameObject UiObject(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static SpriteRenderer SpriteChild(Transform parent, string name, Sprite sprite,
            Color color, int order, Vector3 scale)
        {
            GameObject go = NewObject(name, parent);
            go.transform.localScale = new Vector3(
                scale.x / sprite.bounds.size.x,
                scale.y / sprite.bounds.size.y,
                1f);
            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = order;
            return renderer;
        }

        private static TextMesh LabelChild(Transform parent, string name, string content,
            float characterSize, int order)
        {
            GameObject go = NewObject(name, parent);
            TextMesh mesh = go.AddComponent<TextMesh>();
            mesh.text = content;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.characterSize = characterSize;
            mesh.fontSize = 48;
            mesh.color = Ink;
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            renderer.sortingOrder = order;
            return mesh;
        }

        private static GameObject SavePrefab(GameObject source, string name)
        {
            string path = $"{PrefabFolder}/{name}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, path);
            Object.DestroyImmediate(source);
            return prefab;
        }

        private static GameObject Instance(GameObject prefab, Transform parent, string name)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = name;
            return instance;
        }

        private static GameObject NewObject(string name, Transform parent = null)
        {
            GameObject go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        private static Transform NewRoot(string name, Transform parent) => NewObject(name, parent).transform;

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void StretchTop(RectTransform rect, float height)
        {
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, 1);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0, height);
        }

        private static void SetObjects(Object target, params (string field, Object value)[] fields)
        {
            SerializedObject serialized = new SerializedObject(target);
            foreach ((string field, Object value) in fields)
            {
                SerializedProperty property = serialized.FindProperty(field);
                if (property == null) throw new System.InvalidOperationException($"Missing {target.name}.{field}");
                property.objectReferenceValue = value;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetObjectArray(Object target, string field, Object[] values)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null) throw new System.InvalidOperationException($"Missing {target.name}.{field}");
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetInt(Object target, string field, int value)
        {
            SerializedObject serialized = new SerializedObject(target);
            serialized.FindProperty(field).intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
