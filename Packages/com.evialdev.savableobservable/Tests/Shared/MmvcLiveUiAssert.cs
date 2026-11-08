using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SavableObservable.Tests.Contracts {
    /// <summary>
    /// Live UI assertion and simulation helper for MMVC testing.
    /// Provides user-simulation drivers, visual readers, subscription trackers,
    /// and assertions for model-to-view and view-to-model data flow.
    /// </summary>
    public static class MmvcLiveUiAssert {
        #region Drivers (Simulate User Input)

        /// <summary>
        /// Simulates a user clicking a button after verifying it is enabled and interactable.
        /// </summary>
        public static IEnumerator Click(Button button) {
            Assert.IsNotNull(button, "Button must not be null.");
            Assert.IsTrue(button.enabled, $"Button '{button.name}' Behaviour must be enabled.");
            Assert.IsTrue(button.gameObject.activeInHierarchy, $"Button '{button.name}' GameObject must be active in hierarchy.");
            Assert.IsTrue(button.interactable, $"Button '{button.name}' must be interactable.");

            button.onClick.Invoke();
            yield return null;
        }

        /// <summary>
        /// Simulates a user toggling a Toggle control after verifying it is enabled and interactable.
        /// </summary>
        public static IEnumerator SetToggle(Toggle toggle, bool value) {
            Assert.IsNotNull(toggle, "Toggle must not be null.");
            Assert.IsTrue(toggle.enabled, $"Toggle '{toggle.name}' Behaviour must be enabled.");
            Assert.IsTrue(toggle.gameObject.activeInHierarchy, $"Toggle '{toggle.name}' GameObject must be active in hierarchy.");
            Assert.IsTrue(toggle.interactable, $"Toggle '{toggle.name}' must be interactable.");

            toggle.isOn = value;
            yield return null;
        }

        /// <summary>
        /// Simulates a user dragging or setting a Slider control after verifying it is enabled and interactable.
        /// </summary>
        public static IEnumerator SetSlider(Slider slider, float value) {
            Assert.IsNotNull(slider, "Slider must not be null.");
            Assert.IsTrue(slider.enabled, $"Slider '{slider.name}' Behaviour must be enabled.");
            Assert.IsTrue(slider.gameObject.activeInHierarchy, $"Slider '{slider.name}' GameObject must be active in hierarchy.");
            Assert.IsTrue(slider.interactable, $"Slider '{slider.name}' must be interactable.");

            slider.value = value;
            yield return null;
        }

        /// <summary>
        /// Simulates a user typing into a TMP_InputField after verifying it is enabled and interactable.
        /// </summary>
        public static IEnumerator Type(TMP_InputField input, string text) {
            Assert.IsNotNull(input, "TMP_InputField must not be null.");
            Assert.IsTrue(input.enabled, $"TMP_InputField '{input.name}' Behaviour must be enabled.");
            Assert.IsTrue(input.gameObject.activeInHierarchy, $"TMP_InputField '{input.name}' GameObject must be active in hierarchy.");
            Assert.IsTrue(input.interactable, $"TMP_InputField '{input.name}' must be interactable.");

            input.text = text;
            yield return null;
        }

        /// <summary>
        /// Simulates a user typing into a legacy uGUI InputField after verifying it is enabled and interactable.
        /// </summary>
        public static IEnumerator Type(InputField input, string text) {
            Assert.IsNotNull(input, "InputField must not be null.");
            Assert.IsTrue(input.enabled, $"InputField '{input.name}' Behaviour must be enabled.");
            Assert.IsTrue(input.gameObject.activeInHierarchy, $"InputField '{input.name}' GameObject must be active in hierarchy.");
            Assert.IsTrue(input.interactable, $"InputField '{input.name}' must be interactable.");

            input.text = text;
            yield return null;
        }

        #endregion

        #region Readers (Inspect Visual State)

        /// <summary>
        /// Reads the text content of a TextMeshPro component.
        /// </summary>
        public static string Text(TMP_Text text) => text != null ? text.text : null;

        /// <summary>
        /// Reads the text content of a legacy uGUI Text component.
        /// </summary>
        public static string Text(Text text) => text != null ? text.text : null;

        /// <summary>
        /// Reads sprite name from an Image component.
        /// </summary>
        public static string SpriteName(Image image) => image != null && image.sprite != null ? image.sprite.name : null;

        /// <summary>
        /// Reads alpha from a CanvasGroup component.
        /// </summary>
        public static float Alpha(CanvasGroup group) => group != null ? group.alpha : 0f;

        /// <summary>
        /// Reads alpha from a Graphic component.
        /// </summary>
        public static float Alpha(Graphic graphic) => graphic != null ? graphic.color.a : 0f;

        /// <summary>
        /// Inspects whether a Behaviour is non-null and enabled.
        /// </summary>
        public static bool IsEnabled(Behaviour behaviour) => behaviour != null && behaviour.enabled;

        /// <summary>
        /// Inspects whether a Selectable UI element is non-null and interactable.
        /// </summary>
        public static bool Interactable(Selectable selectable) => selectable != null && selectable.interactable;

        #endregion

        #region Subscription Helpers

        /// <summary>
        /// Tracked manual subscription helper that records callbacks and can be disposed to unregister.
        /// </summary>
        public sealed class TrackedSubscription<T> : IDisposable {
            private readonly ObservableVariable<T> _variable;
            private readonly Action<ObservableVariable<T>> _handler;
            private readonly List<T> _history = new List<T>();
            private int _callCount;
            private T _lastValue;

            public int CallCount => _callCount;
            public T LastValue => _lastValue;
            public IReadOnlyList<T> History => _history;

            public TrackedSubscription(ObservableVariable<T> variable, object owner, Action<T> callback = null) {
                _variable = variable ?? throw new ArgumentNullException(nameof(variable));
                _handler = v => {
                    _callCount++;
                    _lastValue = v.Value;
                    _history.Add(v.Value);
                    callback?.Invoke(v.Value);
                };
                _variable.OnValueChanged.Add(_handler, owner);
            }

            public void Reset() {
                _callCount = 0;
                _history.Clear();
            }

            public void Dispose() {
                _variable.OnValueChanged.Remove(_handler);
            }
        }

        /// <summary>
        /// Tracked manual subscription helper for ObservableList.
        /// </summary>
        public sealed class TrackedListSubscription<T> : IDisposable {
            private readonly ObservableList<T> _list;
            private readonly Action<ObservableList<T>> _handler;
            private int _callCount;

            public int CallCount => _callCount;

            public TrackedListSubscription(ObservableList<T> list, object owner, Action<ObservableList<T>> callback = null) {
                _list = list ?? throw new ArgumentNullException(nameof(list));
                _handler = l => {
                    _callCount++;
                    callback?.Invoke(l);
                };
                _list.OnChanged.Add(_handler, owner);
            }

            public void Reset() {
                _callCount = 0;
            }

            public void Dispose() {
                _list.OnChanged.Remove(_handler);
            }
        }

        /// <summary>
        /// Attaches a counting manual subscriber to an ObservableVariable and returns an IDisposable scope.
        /// </summary>
        public static TrackedSubscription<T> SubscribeTracked<T>(ObservableVariable<T> variable, object owner, Action<T> callback = null) {
            return new TrackedSubscription<T>(variable, owner, callback);
        }

        /// <summary>
        /// Attaches a counting manual subscriber to an ObservableList and returns an IDisposable scope.
        /// </summary>
        public static TrackedListSubscription<T> SubscribeTrackedList<T>(ObservableList<T> list, object owner, Action<ObservableList<T>> callback = null) {
            return new TrackedListSubscription<T>(list, owner, callback);
        }

        #endregion

        #region Assertions (Model-to-View & View-to-Model)

        /// <summary>
        /// Asserts that updating an ObservableVariable notifies subscribers exactly once,
        /// converges the model value, and renders the expected value in the view.
        /// </summary>
        public static IEnumerator ModelToView<T, TView>(
            ObservableVariable<T> variable,
            T value,
            Func<TView> readView,
            TView expectedView,
            string message = null)
        {
            Assert.IsNotNull(variable, "ObservableVariable must not be null.");
            Assert.IsNotNull(readView, "readView delegate must not be null.");

            object subscriber = new object();
            using (var sub = SubscribeTracked(variable, subscriber)) {
                variable.Value = value;
                yield return null;

                string prefix = string.IsNullOrEmpty(message) ? "" : message + ": ";
                Assert.AreEqual(1, sub.CallCount, prefix + "Model change must notify subscriber exactly once.");
                Assert.AreEqual(value, variable.Value, prefix + "Model value must converge to assigned value.");
                TView actualView = readView();
                Assert.AreEqual(expectedView, actualView, prefix + "View must reflect expected value.");
            }
        }

        /// <summary>
        /// Asserts that user interaction driving a UI view updates the model value exactly once
        /// and without bounce-back.
        /// </summary>
        public static IEnumerator ViewToModel<T, TView>(
            IEnumerator driveAction,
            ObservableVariable<T> variable,
            T expectedModelValue,
            Func<TView> readView,
            TView expectedViewValue,
            string message = null)
        {
            Assert.IsNotNull(driveAction, "driveAction must not be null.");
            Assert.IsNotNull(variable, "ObservableVariable must not be null.");

            object subscriber = new object();
            using (var sub = SubscribeTracked(variable, subscriber)) {
                yield return driveAction;

                string prefix = string.IsNullOrEmpty(message) ? "" : message + ": ";
                Assert.AreEqual(1, sub.CallCount, prefix + "User input driving view must notify exactly once.");
                Assert.AreEqual(expectedModelValue, variable.Value, prefix + "Model value must match expected after UI input.");
                if (readView != null) {
                    TView actualView = readView();
                    Assert.AreEqual(expectedViewValue, actualView, prefix + "View must match expected value without bounce-back.");
                }
            }
        }

        /// <summary>
        /// Overload of ViewToModel when only the model value needs to be verified.
        /// </summary>
        public static IEnumerator ViewToModel<T>(
            IEnumerator driveAction,
            ObservableVariable<T> variable,
            T expectedModelValue,
            string message = null)
        {
            return ViewToModel<T, object>(driveAction, variable, expectedModelValue, null, null, message);
        }

        #endregion
    }
}
