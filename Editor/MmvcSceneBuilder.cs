using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
#if (UNITY_6000_0_OR_NEWER || UNITY_6_OR_NEWER) && HAS_UNITY_PIPELINE
using Unity.Pipeline.Commands;
#endif
using SavableObservable;
using SavableObservable.Samples;

namespace SavableObservable.Editor {
    /// <summary>
    /// Builder utility for creating and updating the MMVC Showcase 'Demo' and 'MmvcVerificationScene' test scenes.
    /// Exposes Unity Pipeline CLI command 'mmvc_build_scene' (on Unity 6+ with Pipeline) and an Editor menu item.
    /// </summary>
    public static class MmvcSceneBuilder {
        public const string SceneDirectory = "Assets/Scripts/unity-savableobservable/Scenes";
        public const string DemoScenePath = SceneDirectory + "/Demo.unity";
        public const string TestScenePath = SceneDirectory + "/MmvcVerificationScene.unity";

        [MenuItem("Tools/SavableObservable/Build Demo and Test Scenes")]
#if (UNITY_6000_0_OR_NEWER || UNITY_6_OR_NEWER) && HAS_UNITY_PIPELINE
        [CliCommand("mmvc_build_scene", "Builds or updates Demo.unity and MmvcVerificationScene.unity under Assets/Scripts/unity-savableobservable/Scenes.")]
#endif
        public static string BuildScene() {
            try {
                if (!Directory.Exists(SceneDirectory)) {
                    Directory.CreateDirectory(SceneDirectory);
                    AssetDatabase.Refresh();
                }

                // Build Demo Scene
                BuildSceneInternal(DemoScenePath, "SavableObservable Demo");

                // Build Test Verification Scene
                BuildSceneInternal(TestScenePath, "SavableObservable Test & Verification");

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                return $"SUCCESS: Built '{DemoScenePath}' and '{TestScenePath}'.";
            } catch (Exception ex) {
                return $"ERROR: Failed to build scenes: {ex.Message}\n{ex.StackTrace}";
            }
        }

        private static void BuildSceneInternal(string targetPath, string title) {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 1. Camera & Lighting
            var camObj = new GameObject("Main Camera");
            var cam = camObj.AddComponent<Camera>();
            camObj.tag = "MainCamera";
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.14f, 0.18f);
            camObj.transform.position = new Vector3(0, 0, -10);

            var lightObj = new GameObject("Directional Light");
            var light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = 1f;

            // 2. EventSystem
            var eventObj = new GameObject("EventSystem");
            eventObj.AddComponent<EventSystem>();
            eventObj.AddComponent<StandaloneInputModule>();

            // 3. Canvas & UI Hierarchy
            var canvasObj = new GameObject("Canvas");
            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();

            // Demo Panel Root
            var panelObj = CreateRectChild(canvasObj, "DemoPanel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700, 600));
            var panelImg = panelObj.AddComponent<Image>();
            panelImg.color = new Color(0.18f, 0.2f, 0.25f, 0.95f);

            // Title
            var titleObj = CreateTextChild(panelObj, "TitleText", title, 24, TextAlignmentOptions.Center);
            SetRect(titleObj.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0, -30), new Vector2(650, 40));

            // 4. PlayerUnit GameObject (Single MMVC Triad)
            var playerUnitObj = new GameObject("PlayerUnit");
            playerUnitObj.transform.SetParent(panelObj.transform, false);

            var model = playerUnitObj.AddComponent<SamplePlayerModel>();
            var logic = playerUnitObj.AddComponent<SamplePlayerLogic>();
            var presenter = playerUnitObj.AddComponent<SamplePlayerPresenter>();
            var loader = playerUnitObj.AddComponent<SamplePlayerLoader>();

            // 5. View Controls container
            var controlsObj = CreateRectChild(panelObj, "ViewControls", new Vector2(0.5f, 0.6f), new Vector2(-150, 20), new Vector2(320, 400));
            
            // Name Input Field
            var nameInputObj = CreateTMPInputField(controlsObj, "NameInputField", "Enter Name...");
            SetRect(nameInputObj.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0, -30), new Vector2(300, 40));
            var tmpInput = nameInputObj.GetComponent<TMP_InputField>();

            // Name Display Label
            var nameLabelObj = CreateTextChild(controlsObj, "NameLabel", "Name: Player", 18, TextAlignmentOptions.Left);
            SetRect(nameLabelObj.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0, -80), new Vector2(300, 30));
            var nameLabel = nameLabelObj.GetComponent<TextMeshProUGUI>();

            // Health Display Label
            var hpLabelObj = CreateTextChild(controlsObj, "HealthLabel", "Health: 100", 18, TextAlignmentOptions.Left);
            SetRect(hpLabelObj.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0, -120), new Vector2(300, 30));
            var hpLabel = hpLabelObj.GetComponent<TextMeshProUGUI>();

            // Energy Slider
            var sliderObj = CreateSlider(controlsObj, "EnergySlider");
            SetRect(sliderObj.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0, -170), new Vector2(300, 30));
            var energySlider = sliderObj.GetComponent<Slider>();

            // Shield Toggle
            var toggleObj = CreateToggle(controlsObj, "ShieldToggle", "Shield Active");
            SetRect(toggleObj.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0, -220), new Vector2(300, 35));
            var shieldToggle = toggleObj.GetComponent<Toggle>();

            // Inventory List Container
            var invObj = CreateRectChild(controlsObj, "InventoryListContainer", new Vector2(0.5f, 1f), new Vector2(0, -320), new Vector2(300, 140));
            var invImg = invObj.AddComponent<Image>();
            invImg.color = new Color(0.12f, 0.13f, 0.16f, 0.8f);
            var vlg = invObj.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandHeight = false;
            vlg.childControlHeight = true;
            vlg.spacing = 4;
            vlg.padding = new RectOffset(10, 10, 10, 10);

            // Wire Presenter fields
            presenter.playerNameText = nameLabel;
            presenter.healthText = hpLabel;
            presenter.energySlider = energySlider;
            presenter.shieldToggle = shieldToggle;
            presenter.nameInputField = tmpInput;
            presenter.inventoryContainer = invObj.transform;

            // 6. Action Buttons container
            var actionsObj = CreateRectChild(panelObj, "ActionButtons", new Vector2(0.5f, 0.6f), new Vector2(170, 20), new Vector2(280, 400));
            var actionVlg = actionsObj.AddComponent<VerticalLayoutGroup>();
            actionVlg.childForceExpandHeight = false;
            actionVlg.childControlHeight = true;
            actionVlg.spacing = 10;
            actionVlg.padding = new RectOffset(10, 10, 10, 10);

            CreateButton(actionsObj, "Btn_Damage", "-15 HP", () => logic.TakeDamage(15));
            CreateButton(actionsObj, "Btn_Heal", "+15 HP", () => logic.Heal(15));
            CreateButton(actionsObj, "Btn_AddItem", "Add Potion", () => logic.AddInventoryItem("Potion " + (model.inventory.Count + 1)));
            CreateButton(actionsObj, "Btn_ClearInv", "Clear Items", () => logic.ClearInventory());
            CreateButton(actionsObj, "Btn_ToggleShield", "Toggle Shield", () => logic.ToggleShield());
            CreateButton(actionsObj, "Btn_Save", "Save State", () => loader.SaveCurrentState());
            CreateButton(actionsObj, "Btn_Reset", "Reset Defaults", () => model.InitializeDefaults());

            // Save scene asset
            EditorSceneManager.SaveScene(scene, targetPath);
        }

        private static GameObject CreateRectChild(GameObject parent, string name, Vector2 anchor, Vector2 anchoredPos, Vector2 size) {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent.transform, false);
            var rect = obj.GetComponent<RectTransform>();
            SetRect(rect, anchor, anchoredPos, size);
            return obj;
        }

        private static void SetRect(RectTransform rect, Vector2 anchor, Vector2 anchoredPos, Vector2 size) {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = size;
        }

        private static GameObject CreateTextChild(GameObject parent, string name, string text, float fontSize, TextAlignmentOptions align) {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent.transform, false);
            var tmp = obj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = align;
            tmp.color = Color.white;
            return obj;
        }

        private static GameObject CreateTMPInputField(GameObject parent, string name, string placeholder) {
            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent.transform, false);
            var bg = root.AddComponent<Image>();
            bg.color = new Color(0.24f, 0.27f, 0.33f);

            var input = root.AddComponent<TMP_InputField>();

            var textViewport = CreateRectChild(root, "Text Area", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var vpRect = textViewport.GetComponent<RectTransform>();
            vpRect.anchorMin = Vector2.zero;
            vpRect.anchorMax = Vector2.one;
            vpRect.sizeDelta = new Vector2(-16, -10);
            textViewport.AddComponent<RectMask2D>();

            var textObj = CreateTextChild(textViewport, "Text", "", 16, TextAlignmentOptions.Left);
            var tRect = textObj.GetComponent<RectTransform>();
            tRect.anchorMin = Vector2.zero;
            tRect.anchorMax = Vector2.one;
            tRect.sizeDelta = Vector2.zero;
            var textComp = textObj.GetComponent<TextMeshProUGUI>();

            var placeObj = CreateTextChild(textViewport, "Placeholder", placeholder, 16, TextAlignmentOptions.Left);
            var pRect = placeObj.GetComponent<RectTransform>();
            pRect.anchorMin = Vector2.zero;
            pRect.anchorMax = Vector2.one;
            pRect.sizeDelta = Vector2.zero;
            var placeComp = placeObj.GetComponent<TextMeshProUGUI>();
            placeComp.color = new Color(0.7f, 0.7f, 0.7f, 0.5f);

            input.textViewport = vpRect;
            input.textComponent = textComp;
            input.placeholder = placeComp;

            return root;
        }

        private static GameObject CreateSlider(GameObject parent, string name) {
            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent.transform, false);

            var slider = root.AddComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;

            var bg = CreateRectChild(root, "Background", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0.1f, 0.1f, 0.1f, 0.6f);

            var fillArea = CreateRectChild(root, "Fill Area", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var faRect = fillArea.GetComponent<RectTransform>();
            faRect.anchorMin = Vector2.zero;
            faRect.anchorMax = Vector2.one;
            faRect.sizeDelta = new Vector2(-20, 0);

            var fill = CreateRectChild(fillArea, "Fill", new Vector2(0, 0.5f), Vector2.zero, Vector2.zero);
            var fRect = fill.GetComponent<RectTransform>();
            fRect.anchorMin = Vector2.zero;
            fRect.anchorMax = Vector2.one;
            fRect.sizeDelta = Vector2.zero;
            var fillImg = fill.AddComponent<Image>();
            fillImg.color = new Color(0.2f, 0.7f, 0.9f);

            slider.fillRect = fRect;

            return root;
        }

        private static GameObject CreateToggle(GameObject parent, string name, string label) {
            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent.transform, false);

            var toggle = root.AddComponent<Toggle>();

            var bg = CreateRectChild(root, "Background", new Vector2(0, 0.5f), new Vector2(15, 0), new Vector2(24, 24));
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0.25f, 0.28f, 0.35f);

            var check = CreateRectChild(bg, "Checkmark", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(16, 16));
            var checkImg = check.AddComponent<Image>();
            checkImg.color = new Color(0.2f, 0.9f, 0.4f);

            var labelObj = CreateTextChild(root, "Label", label, 16, TextAlignmentOptions.Left);
            var lRect = labelObj.GetComponent<RectTransform>();
            lRect.anchorMin = new Vector2(0, 0);
            lRect.anchorMax = new Vector2(1, 1);
            lRect.anchoredPosition = new Vector2(40, 0);
            lRect.sizeDelta = new Vector2(-40, 0);

            toggle.targetGraphic = bgImg;
            toggle.graphic = checkImg;

            return root;
        }

        private static GameObject CreateButton(GameObject parent, string name, string text, UnityEngine.Events.UnityAction onClick) {
            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent.transform, false);
            var rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(240, 36);

            var img = root.AddComponent<Image>();
            img.color = new Color(0.26f, 0.36f, 0.55f);

            var btn = root.AddComponent<Button>();
            btn.targetGraphic = img;
            if (onClick != null) {
                btn.onClick.AddListener(onClick);
            }

            var labelObj = CreateTextChild(root, "Text", text, 15, TextAlignmentOptions.Center);
            var lRect = labelObj.GetComponent<RectTransform>();
            lRect.anchorMin = Vector2.zero;
            lRect.anchorMax = Vector2.one;
            lRect.sizeDelta = Vector2.zero;

            return root;
        }
    }
}
