using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SavableObservable;

namespace SavableObservable.Samples {
    /// <summary>
    /// Sample presenter demonstrating [AutoBind] and [ObservableHandler] attributes.
    /// Derives from BaseObservablePresenter&lt;SamplePlayerModel&gt;.
    /// </summary>
    [DisallowMultipleComponent]
    public class SamplePlayerPresenter : BaseObservablePresenter<SamplePlayerModel> {
        [Header("UI AutoBind Elements")]
        [AutoBind("playerName")]
        [SerializeField] public TextMeshProUGUI playerNameText;

        [AutoBind("health")]
        [SerializeField] public TextMeshProUGUI healthText;

        [AutoBind("energy")]
        [SerializeField] public Slider energySlider;

        [AutoBind("isShieldActive")]
        [SerializeField] public Toggle shieldToggle;

        [AutoBind("playerName")]
        [SerializeField] public TMP_InputField nameInputField;

        [Header("Inventory Container")]
        [SerializeField] public Transform inventoryContainer;

        // Observable inspection state for verification and testing
        public int LastObservedHealth { get; private set; }
        public int LastObservedPrevHealth { get; private set; }
        public int HealthChangeEventsCount { get; private set; }
        public int InventoryChangeEventsCount { get; private set; }
        public List<string> CurrentInventoryItems { get; } = new List<string>();

        private void Start() {
            var model = GetModel();
            if (model != null) {
                Observable.SetListeners(this, model);
            }
        }

        private void OnDestroy() {
            var model = GetModel();
            if (model != null) {
                Observable.RemoveListeners(model, this);
            }
        }

        public void InitializeBinding() {
            var model = GetModel();
            if (model != null) {
                Observable.SetListeners(this, model);
            }
        }

        [ObservableHandler("health")]
        private void OnHealthChanged(int current, int prev) {
            LastObservedHealth = current;
            LastObservedPrevHealth = prev;
            HealthChangeEventsCount++;
        }

        [ObservableHandler("inventory")]
        private void OnInventoryChanged(ObservableList<string> list) {
            InventoryChangeEventsCount++;
            CurrentInventoryItems.Clear();
            if (list != null) {
                for (int i = 0; i < list.Count; i++) {
                    CurrentInventoryItems.Add(list[i]);
                }
            }
            RefreshInventoryView();
        }

        private void RefreshInventoryView() {
            if (inventoryContainer == null) return;

            // Clear old children if any
            for (int i = inventoryContainer.childCount - 1; i >= 0; i--) {
                var child = inventoryContainer.GetChild(i);
                if (Application.isPlaying) {
                    Destroy(child.gameObject);
                } else {
                    DestroyImmediate(child.gameObject);
                }
            }

            // Create simple text element for each item
            foreach (var item in CurrentInventoryItems) {
                var itemObj = new GameObject($"Item_{item}", typeof(RectTransform));
                itemObj.transform.SetParent(inventoryContainer, false);
                var text = itemObj.AddComponent<TextMeshProUGUI>();
                text.text = item;
                text.fontSize = 18;
            }
        }
    }
}
