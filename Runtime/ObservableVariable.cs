using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SavableObservable {
    [Serializable]
    public class ObservableVariable<T> : IObservableVariable {
        /// <summary>
        /// The actual stored value. Set via the inspector or code.
        /// </summary>
        [SerializeField] private T _value;

        /// <summary>
        /// Custom tracked action that automatically manages subscriptions.
        /// </summary>
        /// <summary>
        /// Public access to the tracked action for subscription management.
        /// </summary>
        public ObservableTrackedAction<ObservableVariable<T>> OnValueChanged {
            get {
                if (_onValueChanged == null) {
                    _onValueChanged = new ObservableTrackedAction<ObservableVariable<T>>();
                }
                return _onValueChanged;
            }
            set {
                _onValueChanged = value ?? new ObservableTrackedAction<ObservableVariable<T>>();
            }
        }
        private ObservableTrackedAction<ObservableVariable<T>> _onValueChanged;

        /// <summary>
        /// Stores the previous value of the variable.
        /// </summary>
        public T PreviousValue { get; private set; }

#if UNITY_EDITOR
        [NonSerialized] private T _editorGuiSnapshot;
        [NonSerialized] private bool _hasEditorGuiSnapshot;
        [NonSerialized] private T _lastValidatedValue;
        [NonSerialized] private bool _hasLastValidatedValue;
#endif

        /// <summary>
        /// Default constructor.
        /// </summary>
        public ObservableVariable() {
        }

        /// <summary>
        /// Main access point for getting and setting the variable.
        /// Triggers change events when value is updated via code.
        /// </summary>
        public T Value {
            get => _value;
            set {
                PreviousValue = _value;
                _value = value;
                OnValueChanged?.Invoke(this);
            }
        }

        /// <summary>
        /// Forces the OnValueChanged event to fire, useful for when values are changed directly in the editor.
        /// </summary>
        public void ForceNotify() {
            PreviousValue = _value;
            OnValueChanged?.Invoke(this);
        }

        public override string ToString() => _value?.ToString();

#if UNITY_EDITOR
        /// <summary>
        /// Captures the value snapshot before drawing editor controls.
        /// </summary>
        public void OnBeginGui() {
            _editorGuiSnapshot = _value;
            _hasEditorGuiSnapshot = true;

            if (!_hasLastValidatedValue) {
                _lastValidatedValue = _value;
                _hasLastValidatedValue = true;
            }
        }

        /// <summary>
        /// Handles editor validation (including undo/redo) and emits change notifications with proper previous-value snapshots.
        /// </summary>
        public void OnValidate() {
            T previousValue;

            if (_hasEditorGuiSnapshot) {
                previousValue = _editorGuiSnapshot;
            }
            else if (_hasLastValidatedValue) {
                previousValue = _lastValidatedValue;
            }
            else {
                previousValue = _value;
            }

            _hasEditorGuiSnapshot = false;

            if (!EqualityComparer<T>.Default.Equals(previousValue, _value)) {
                PreviousValue = previousValue;
                OnValueChanged?.Invoke(this);
            }

            _lastValidatedValue = _value;
            _hasLastValidatedValue = true;
        }
#endif

        /// <summary>
        /// Sets the parent data model for cleanup purposes.
        /// </summary>
        /// <param name="dataModel">The parent data model</param>
        public void SetParentDataModel(BaseObservableDataModel dataModel) {
            OnValueChanged.ParentDataModel = dataModel;
        }
    }

}