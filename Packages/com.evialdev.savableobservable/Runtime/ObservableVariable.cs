using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SavableObservable {
    /// <summary>
    /// Internal hook that lets the model enable same-value notification without generic reflection.
    /// </summary>
    internal interface ISameValuePolicy {
        void EnableNotifyOnSameValue();
    }

    [Serializable]
    public class ObservableVariable<T> : IObservableVariable, ISameValuePolicy {
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

        [NonSerialized] private bool _notifyOnSameValue;

        /// <summary>
        /// When <c>true</c>, assigning a value equal to the current one still notifies subscribers
        /// (with <see cref="PreviousValue"/> equal to <see cref="Value"/>). Default is <c>false</c>: equal assignments are silent.
        /// Can be enabled per field with <see cref="NotifyOnSameValueAttribute"/>. Not serialized and not copied on load.
        /// </summary>
        public bool NotifyOnSameValue {
            get => _notifyOnSameValue;
            set => _notifyOnSameValue = value;
        }

        void ISameValuePolicy.EnableNotifyOnSameValue() {
            _notifyOnSameValue = true;
        }

        /// <summary>
        /// Main access point for getting and setting the variable.
        /// Triggers change events when the value changes (compared with <see cref="EqualityComparer{T}.Default"/>),
        /// or on every assignment when <see cref="NotifyOnSameValue"/> is enabled.
        /// </summary>
        public T Value {
            get => _value;
            set {
                if (!_notifyOnSameValue && EqualityComparer<T>.Default.Equals(_value, value)) return;
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
            OnValueChanged.Owner = this;
        }

        Delegate IObservableVariable.CreateHandler(object target, MethodInfo method, int paramCount) {
            switch (paramCount) {
                case 0: {
                    var act = (Action)Delegate.CreateDelegate(typeof(Action), target, method);
                    Action<ObservableVariable<T>> handler = _ => act();
                    return handler;
                }
                case 1: {
                    try {
                        var act = (Action<T>)Delegate.CreateDelegate(typeof(Action<T>), target, method);
                        Action<ObservableVariable<T>> handler = v => act(v.Value);
                        return handler;
                    } catch {
                        Action<ObservableVariable<T>> handler = v => method.Invoke(target, new object[] { v.Value });
                        return handler;
                    }
                }
                case 2: {
                    try {
                        var act = (Action<T, T>)Delegate.CreateDelegate(typeof(Action<T, T>), target, method);
                        Action<ObservableVariable<T>> handler = v => act(v.Value, v.PreviousValue);
                        return handler;
                    } catch {
                        Action<ObservableVariable<T>> handler = v => method.Invoke(target, new object[] { v.Value, v.PreviousValue });
                        return handler;
                    }
                }
                default:
                    return null;
            }
        }

        Delegate IObservableVariable.CreateAutoBindHandler(Action<object> modelToUiHandler) {
            Action<ObservableVariable<T>> handler = v => modelToUiHandler(v.Value);
            return handler;
        }

        void IObservableVariable.AddHandler(Delegate handler, object subscriber) {
            OnValueChanged.Add((Action<ObservableVariable<T>>)handler, subscriber, managed: true);
        }

        void IObservableVariable.RemoveHandler(Delegate handler) {
            OnValueChanged.Remove((Action<ObservableVariable<T>>)handler);
        }
    }

}