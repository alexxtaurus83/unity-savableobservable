using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;
using Unity.Scripting.LifecycleManagement;

namespace SavableObservable
{
    /// <summary>
    /// Interface for UI adapters that handle automatic value binding.
    /// </summary>
    public interface IUIAdapter
    {
        /// <summary>
        /// Determines if this adapter can handle the given UI component type.
        /// </summary>
        bool CanHandle(Type uiComponentType);

        /// <summary>
        /// Gets the priority of this adapter. Higher priority adapters are checked first.
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// Sets the value on the UI component.
        /// </summary>
        void SetValue(object uiComponent, object value, Type valueType);
    }

    /// <summary>
    /// Extended interface for UI adapters that support listening to value changes.
    /// </summary>
    public interface IUIListenerAdapter : IUIAdapter
    {
        /// <summary>
        /// Adds a listener to the UI component to track changes.
        /// </summary>
        /// <returns>An opaque token representing the listener, used for removal.</returns>
        object AddListener(object uiComponent, Action<object> onValueChanged, Type valueType);

        /// <summary>
        /// Removes a previously added listener from the UI component.
        /// </summary>
        void RemoveListener(object uiComponent, object token);
    }

    /// <summary>
    /// Registry for UI adapters. Manages adapter registration and lookup.
    /// </summary>
    public static partial class UIAdapterRegistry
    {
        [AutoStaticsCleanup]
        private static readonly List<IUIAdapter> _adapters = new List<IUIAdapter>();
        [NoAutoStaticsCleanup]
        private static readonly List<IUIAdapter> _customAdapters = new List<IUIAdapter>();
        [NoAutoStaticsCleanup]
        private static readonly object _lock = new object();
        [AutoStaticsCleanup]
        private static bool _initialized;
        
        // Immutable snapshot of adapters for lock-free iteration after initialization
        [AutoStaticsCleanup]
        private static IUIAdapter[] _adaptersSnapshot;
        
        // Cache mapping Type -> IUIAdapter for O(1) lookups after first call
        // Null adapters are cached separately to avoid repeated failed lookups
        [AutoStaticsCleanup]
        private static readonly Dictionary<Type, IUIAdapter> _adapterCache = new Dictionary<Type, IUIAdapter>();
        [AutoStaticsCleanup]
        private static readonly HashSet<Type> _negativeCache = new HashSet<Type>();
        [AutoStaticsCleanup]
        private static readonly HashSet<object> _activeSetValueTargets = new HashSet<object>();

        internal static bool EnterSetValue(object control)
        {
            if (control == null) return false;
            lock (_lock)
            {
                return _activeSetValueTargets.Add(control);
            }
        }

        internal static void ExitSetValue(object control)
        {
            if (control == null) return;
            lock (_lock)
            {
                _activeSetValueTargets.Remove(control);
            }
        }

        internal static bool IsSettingValue(object control)
        {
            if (control == null) return false;
            lock (_lock)
            {
                return _activeSetValueTargets.Contains(control);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry()
        {
            lock (_lock)
            {
                _adapters.Clear();
                _adapterCache.Clear();
                _negativeCache.Clear();
                _activeSetValueTargets.Clear();
                _adaptersSnapshot = null;
                _initialized = false;
                EnsureInitialized();
            }
        }

        public static void RegisterAdapter(IUIAdapter adapter)
        {
            if (adapter == null) return;

            lock (_lock)
            {
                EnsureInitialized();

                var adapterType = adapter.GetType();
                for (int i = _customAdapters.Count - 1; i >= 0; i--)
                {
                    if (_customAdapters[i].GetType() == adapterType)
                    {
                        var old = _customAdapters[i];
                        _customAdapters.RemoveAt(i);
                        _adapters.Remove(old);
                    }
                }

                _customAdapters.Add(adapter);

                _adapters.Remove(adapter);
                _adapters.Add(adapter);
                _adapters.Sort((a, b) => b.Priority.CompareTo(a.Priority));
                _adaptersSnapshot = _adapters.ToArray();
                _adapterCache.Clear();
                _negativeCache.Clear();
            }
        }

        public static void UnregisterAdapter(IUIAdapter adapter)
        {
            if (adapter == null) return;

            lock (_lock)
            {
                _customAdapters.Remove(adapter);
                if (_adapters.Remove(adapter))
                {
                    _adaptersSnapshot = _adapters.ToArray();
                    _adapterCache.Clear();
                    _negativeCache.Clear();
                }
            }
        }

        public static void ClearCustomAdapters()
        {
            lock (_lock)
            {
                for (int i = 0; i < _customAdapters.Count; i++)
                {
                    _adapters.Remove(_customAdapters[i]);
                }
                _customAdapters.Clear();
                _adaptersSnapshot = _adapters.ToArray();
                _adapterCache.Clear();
                _negativeCache.Clear();
            }
        }

        public static IUIAdapter GetAdapter(Type uiComponentType)
        {
            EnsureInitialized();
            if (uiComponentType == null) return null;

            // Check cache first (lock-free after initialization)
            lock (_lock)
            {
                if (_adapterCache.TryGetValue(uiComponentType, out var cachedAdapter))
                {
                    return cachedAdapter;
                }
                if (_negativeCache.Contains(uiComponentType))
                {
                    return null;
                }
            }

            // Iterate over immutable snapshot without locking
            IUIAdapter foundAdapter = null;
            var snapshot = _adaptersSnapshot;
            if (snapshot != null)
            {
                foreach (var adapter in snapshot)
                {
                    if (adapter.CanHandle(uiComponentType))
                    {
                        foundAdapter = adapter;
                        break;
                    }
                }
            }

            // Cache the result (positive or negative)
            lock (_lock)
            {
                if (foundAdapter != null)
                {
                    _adapterCache[uiComponentType] = foundAdapter;
                }
                else
                {
                    _negativeCache.Add(uiComponentType);
                }
            }

            return foundAdapter;
        }

        private static void EnsureInitialized()
        {
            if (_initialized) return;

            lock (_lock)
            {
                if (_initialized) return;

                RegisterBuiltInAdapter(new TMPInputFieldAdapter());
                RegisterBuiltInAdapter(new TextMeshProAdapter());
                RegisterBuiltInAdapter(new InputFieldAdapter());
                RegisterBuiltInAdapter(new UnityTextAdapter());
                RegisterBuiltInAdapter(new ToggleAdapter());
                RegisterBuiltInAdapter(new SliderAdapter());
                RegisterBuiltInAdapter(new TMP_DropdownAdapter());
                RegisterBuiltInAdapter(new DropdownAdapter());
                RegisterBuiltInAdapter(new ImageAdapter());

                for (int i = 0; i < _customAdapters.Count; i++)
                {
                    var custom = _customAdapters[i];
                    if (!_adapters.Contains(custom))
                    {
                        _adapters.Add(custom);
                    }
                }

                _adapters.Sort((a, b) => b.Priority.CompareTo(a.Priority));
                _adaptersSnapshot = _adapters.ToArray();
                _initialized = true;
            }
        }

        private static void RegisterBuiltInAdapter(IUIAdapter adapter)
        {
            if (adapter == null || _adapters.Contains(adapter)) return;
            _adapters.Add(adapter);
        }

        /// <summary>
        /// Tries to get a listener adapter for the specified UI component type.
        /// </summary>
        /// <param name="uiComponentType">The type of UI component.</param>
        /// <param name="listenerAdapter">The listener adapter if found; otherwise, null.</param>
        /// <returns>True if a listener adapter was found; otherwise, false.</returns>
        public static bool TryGetListenerAdapter(Type uiComponentType, out IUIListenerAdapter listenerAdapter)
        {
            listenerAdapter = GetAdapter(uiComponentType) as IUIListenerAdapter;
            return listenerAdapter != null;
        }

        internal static bool TryConvertValue(object rawValue, Type targetType, out object converted)
        {
            converted = null;
            if (targetType == null) return false;

            Type underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
            bool isNullable = Nullable.GetUnderlyingType(targetType) != null;

            if (rawValue == null || (rawValue is string str && string.IsNullOrWhiteSpace(str)))
            {
                if (isNullable)
                {
                    converted = null;
                    return true;
                }
                if (underlying == typeof(string))
                {
                    converted = rawValue ?? string.Empty;
                    return true;
                }
                if (underlying.IsValueType)
                {
                    converted = Activator.CreateInstance(underlying);
                    return true;
                }
                return false;
            }

            try
            {
                if (underlying.IsEnum)
                {
                    if (rawValue is string s)
                    {
                        if (Enum.TryParse(underlying, s, ignoreCase: true, out var parsed))
                        {
                            if (Enum.IsDefined(underlying, parsed) || underlying.IsDefined(typeof(FlagsAttribute), false))
                            {
                                converted = parsed;
                                return true;
                            }
                        }
                        return false;
                    }
                    else
                    {
                        var enumUnderlying = Enum.GetUnderlyingType(underlying);
                        var numericVal = Convert.ChangeType(rawValue, enumUnderlying, CultureInfo.InvariantCulture);
                        converted = Enum.ToObject(underlying, numericVal);
                        return true;
                    }
                }

                if (underlying == typeof(string))
                {
                    converted = rawValue.ToString();
                    return true;
                }

                converted = Convert.ChangeType(rawValue, underlying, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                try
                {
                    converted = Convert.ChangeType(rawValue, underlying);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    /// <summary>
    /// Shared implementation for string-based input fields (TMP and legacy UGUI).
    /// Model -> UI uses the field's "without notify" setter guarded by a per-adapter re-entrancy flag;
    /// UI -> Model converts the typed text to the bound value type (invariant culture first).
    /// Empty text maps to the default of a value type and is ignored for non-string reference types.
    /// </summary>
    public abstract class StringInputAdapterBase<TField> : IUIListenerAdapter
        where TField : Component
    {
        public abstract int Priority { get; }

        public bool CanHandle(Type uiComponentType)
        {
            return typeof(TField).IsAssignableFrom(uiComponentType);
        }

        protected abstract string GetText(TField field);
        protected abstract void SetTextWithoutNotify(TField field, string text);
        protected abstract void AddTextListener(TField field, UnityAction<string> listener);
        protected abstract void RemoveTextListener(TField field, UnityAction<string> listener);

        public void SetValue(object uiComponent, object value, Type valueType)
        {
            if (uiComponent is TField inputField)
            {
                string stringValue = value?.ToString() ?? string.Empty;
                if (GetText(inputField) != stringValue)
                {
                    UIAdapterRegistry.EnterSetValue(inputField);
                    try
                    {
                        SetTextWithoutNotify(inputField, stringValue);
                    }
                    finally
                    {
                        UIAdapterRegistry.ExitSetValue(inputField);
                    }
                }
            }
        }

        public object AddListener(object uiComponent, Action<object> onValueChanged, Type valueType)
        {
            if (uiComponent is TField inputField)
            {
                UnityAction<string> listener = val =>
                {
                    if (UIAdapterRegistry.IsSettingValue(inputField)) return;
                    if (UIAdapterRegistry.TryConvertValue(val, valueType, out var convertedValue))
                    {
                        onValueChanged(convertedValue);
                    }
                };
                AddTextListener(inputField, listener);
                return listener;
            }
            return null;
        }

        public void RemoveListener(object uiComponent, object token)
        {
            if (uiComponent is TField inputField && token is UnityAction<string> listener)
            {
                RemoveTextListener(inputField, listener);
            }
        }
    }

    /// <summary>
    /// Adapter for TMP_InputField components.
    /// </summary>
    public class TMPInputFieldAdapter : StringInputAdapterBase<TMP_InputField>
    {
        public override int Priority => 110; // Higher priority than TextMeshProAdapter

        protected override string GetText(TMP_InputField field) => field.text;
        protected override void SetTextWithoutNotify(TMP_InputField field, string text) => field.SetTextWithoutNotify(text);
        protected override void AddTextListener(TMP_InputField field, UnityAction<string> listener) => field.onValueChanged.AddListener(listener);
        protected override void RemoveTextListener(TMP_InputField field, UnityAction<string> listener) => field.onValueChanged.RemoveListener(listener);
    }

    /// <summary>
    /// Adapter for legacy UGUI InputField components.
    /// </summary>
    public class InputFieldAdapter : StringInputAdapterBase<InputField>
    {
        public override int Priority => 110; // Higher priority than UnityTextAdapter

        protected override string GetText(InputField field) => field.text;
        protected override void SetTextWithoutNotify(InputField field, string text) => field.SetTextWithoutNotify(text);
        protected override void AddTextListener(InputField field, UnityAction<string> listener) => field.onValueChanged.AddListener(listener);
        protected override void RemoveTextListener(InputField field, UnityAction<string> listener) => field.onValueChanged.RemoveListener(listener);
    }

    /// <summary>
    /// Adapter for TextMeshProUGUI components.
    /// Converts any value to string via ToString().
    /// </summary>
    public class TextMeshProAdapter : IUIAdapter
    {
        public int Priority => 100;

        public bool CanHandle(Type uiComponentType)
        {
            return typeof(TMP_Text).IsAssignableFrom(uiComponentType);
        }

        public void SetValue(object uiComponent, object value, Type valueType)
        {
            if (uiComponent is TMP_Text textComponent)
            {
                textComponent.text = value?.ToString() ?? string.Empty;
            }
        }
    }

    /// <summary>
    /// Adapter for Unity native Text components.
    /// </summary>
    public class UnityTextAdapter : IUIAdapter
    {
        public int Priority => 100;

        public bool CanHandle(Type uiComponentType)
        {
            return typeof(Text).IsAssignableFrom(uiComponentType);
        }

        public void SetValue(object uiComponent, object value, Type valueType)
        {
            if (uiComponent is Text textComponent)
            {
                textComponent.text = value?.ToString() ?? string.Empty;
            }
        }
    }

    /// <summary>
    /// Adapter for Toggle components. Handles boolean values.
    /// </summary>
    public class ToggleAdapter : IUIListenerAdapter
    {
        public int Priority => 100;

        public bool CanHandle(Type uiComponentType)
        {
            return typeof(Toggle).IsAssignableFrom(uiComponentType);
        }

        public void SetValue(object uiComponent, object value, Type valueType)
        {
            if (uiComponent is Toggle toggle)
            {
                if (value is bool boolValue)
                {
                    if (toggle.isOn != boolValue)
                    {
                        UIAdapterRegistry.EnterSetValue(toggle);
                        try
                        {
                            toggle.SetIsOnWithoutNotify(boolValue);
                        }
                        finally
                        {
                            UIAdapterRegistry.ExitSetValue(toggle);
                        }
                    }
                }
                else if (value != null)
                {
                    Debug.LogWarning($"ToggleAdapter: Expected boolean value for Toggle binding, but got {value.GetType().Name}.");
                }
            }
        }

        public object AddListener(object uiComponent, Action<object> onValueChanged, Type valueType)
        {
            if (uiComponent is Toggle toggle)
            {
                UnityAction<bool> listener = val =>
                {
                    if (UIAdapterRegistry.IsSettingValue(toggle)) return;
                    onValueChanged(val);
                };
                toggle.onValueChanged.AddListener(listener);
                return listener;
            }
            return null;
        }

        public void RemoveListener(object uiComponent, object token)
        {
            if (uiComponent is Toggle toggle && token is UnityAction<bool> listener)
            {
                toggle.onValueChanged.RemoveListener(listener);
            }
        }
    }

    /// <summary>
    /// Adapter for Slider components. Handles numeric values with two-way binding.
    /// </summary>
    public class SliderAdapter : IUIListenerAdapter
    {
        public int Priority => 100;

        public bool CanHandle(Type uiComponentType)
        {
            return typeof(Slider).IsAssignableFrom(uiComponentType);
        }

        public void SetValue(object uiComponent, object value, Type valueType)
        {
            if (uiComponent is Slider slider && value != null)
            {
                try
                {
                    float floatValue;
                    if (value is Enum)
                    {
                        floatValue = Convert.ToSingle(Convert.ChangeType(value, Enum.GetUnderlyingType(value.GetType()), CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        floatValue = Convert.ToSingle(value, CultureInfo.InvariantCulture);
                    }

                    if (float.IsNaN(floatValue) || float.IsInfinity(floatValue))
                    {
                        return;
                    }

                    if (!Mathf.Approximately(slider.value, floatValue))
                    {
                        UIAdapterRegistry.EnterSetValue(slider);
                        try
                        {
                            slider.SetValueWithoutNotify(floatValue);
                        }
                        finally
                        {
                            UIAdapterRegistry.ExitSetValue(slider);
                        }
                    }
                }
                catch
                {
                    // Ignore conversion failures
                }
            }
        }

        public object AddListener(object uiComponent, Action<object> onValueChanged, Type valueType)
        {
            if (uiComponent is Slider slider)
            {
                UnityAction<float> listener = val =>
                {
                    if (UIAdapterRegistry.IsSettingValue(slider)) return;
                    if (UIAdapterRegistry.TryConvertValue(val, valueType, out var convertedValue))
                    {
                        onValueChanged(convertedValue);
                    }
                };
                slider.onValueChanged.AddListener(listener);
                return listener;
            }
            return null;
        }

        public void RemoveListener(object uiComponent, object token)
        {
            if (uiComponent is Slider slider && token is UnityAction<float> listener)
            {
                slider.onValueChanged.RemoveListener(listener);
            }
        }
    }

    /// <summary>
    /// Adapter for TMP_Dropdown components. Handles integer index and enum values.
    /// </summary>
    public class TMP_DropdownAdapter : IUIListenerAdapter
    {
        public int Priority => 100;

        public bool CanHandle(Type uiComponentType)
        {
            return typeof(TMP_Dropdown).IsAssignableFrom(uiComponentType);
        }

        public void SetValue(object uiComponent, object value, Type valueType)
        {
            if (uiComponent is TMP_Dropdown dropdown && value != null)
            {
                try
                {
                    int intValue;
                    if (value is Enum)
                    {
                        intValue = Convert.ToInt32(Convert.ChangeType(value, Enum.GetUnderlyingType(value.GetType()), CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        intValue = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                    }

                    if (dropdown.value != intValue)
                    {
                        UIAdapterRegistry.EnterSetValue(dropdown);
                        try
                        {
                            dropdown.SetValueWithoutNotify(intValue);
                        }
                        finally
                        {
                            UIAdapterRegistry.ExitSetValue(dropdown);
                        }
                    }
                }
                catch
                {
                    // Ignore conversion failures
                }
            }
        }

        public object AddListener(object uiComponent, Action<object> onValueChanged, Type valueType)
        {
            if (uiComponent is TMP_Dropdown dropdown)
            {
                UnityAction<int> listener = val =>
                {
                    if (UIAdapterRegistry.IsSettingValue(dropdown)) return;
                    if (UIAdapterRegistry.TryConvertValue(val, valueType, out var convertedValue))
                    {
                        onValueChanged(convertedValue);
                    }
                };
                dropdown.onValueChanged.AddListener(listener);
                return listener;
            }
            return null;
        }

        public void RemoveListener(object uiComponent, object token)
        {
            if (uiComponent is TMP_Dropdown dropdown && token is UnityAction<int> listener)
            {
                dropdown.onValueChanged.RemoveListener(listener);
            }
        }
    }

    /// <summary>
    /// Adapter for legacy UGUI Dropdown components. Handles integer index and enum values.
    /// </summary>
    public class DropdownAdapter : IUIListenerAdapter
    {
        public int Priority => 100;

        public bool CanHandle(Type uiComponentType)
        {
            return typeof(Dropdown).IsAssignableFrom(uiComponentType);
        }

        public void SetValue(object uiComponent, object value, Type valueType)
        {
            if (uiComponent is Dropdown dropdown && value != null)
            {
                try
                {
                    int intValue;
                    if (value is Enum)
                    {
                        intValue = Convert.ToInt32(Convert.ChangeType(value, Enum.GetUnderlyingType(value.GetType()), CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        intValue = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                    }

                    if (dropdown.value != intValue)
                    {
                        UIAdapterRegistry.EnterSetValue(dropdown);
                        try
                        {
                            dropdown.SetValueWithoutNotify(intValue);
                        }
                        finally
                        {
                            UIAdapterRegistry.ExitSetValue(dropdown);
                        }
                    }
                }
                catch
                {
                    // Ignore conversion failures
                }
            }
        }

        public object AddListener(object uiComponent, Action<object> onValueChanged, Type valueType)
        {
            if (uiComponent is Dropdown dropdown)
            {
                UnityAction<int> listener = val =>
                {
                    if (UIAdapterRegistry.IsSettingValue(dropdown)) return;
                    if (UIAdapterRegistry.TryConvertValue(val, valueType, out var convertedValue))
                    {
                        onValueChanged(convertedValue);
                    }
                };
                dropdown.onValueChanged.AddListener(listener);
                return listener;
            }
            return null;
        }

        public void RemoveListener(object uiComponent, object token)
        {
            if (uiComponent is Dropdown dropdown && token is UnityAction<int> listener)
            {
                dropdown.onValueChanged.RemoveListener(listener);
            }
        }
    }

    /// <summary>
    /// Adapter for Image components. Handles Sprite values.
    /// </summary>
    public class ImageAdapter : IUIAdapter
    {
        public int Priority => 100;

        public bool CanHandle(Type uiComponentType)
        {
            return typeof(Image).IsAssignableFrom(uiComponentType);
        }

        public void SetValue(object uiComponent, object value, Type valueType)
        {
            if (uiComponent is Image image)
            {
                if (value == null)
                {
                    image.sprite = null;
                }
                else if (value is Sprite sprite)
                {
                    image.sprite = sprite;
                }
                else
                {
                    Debug.LogWarning($"ImageAdapter: Expected Sprite value for Image binding, but got {value.GetType().Name}.");
                }
            }
        }
    }
}
