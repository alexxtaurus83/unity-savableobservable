using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

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

        /// <summary>
        /// Adds a listener to the UI component to track changes.
        /// </summary>
        /// <returns>An opaque token representing the listener, used for removal.</returns>
        object AddListener(object uiComponent, Action<object> onValueChanged);

        /// <summary>
        /// Removes a previously added listener from the UI component.
        /// </summary>
        void RemoveListener(object uiComponent, object token);
    }

    /// <summary>
    /// Registry for UI adapters. Manages adapter registration and lookup.
    /// </summary>
    public static class UIAdapterRegistry
    {
        private static readonly List<IUIAdapter> _adapters = new List<IUIAdapter>();
        private static readonly object _lock = new object();
        private static bool _initialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            EnsureInitialized();
        }

        public static void RegisterAdapter(IUIAdapter adapter)
        {
            if (adapter == null) return;

            lock (_lock)
            {
                if (_adapters.Contains(adapter)) return;

                _adapters.Add(adapter);
                _adapters.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            }
        }

        public static IUIAdapter GetAdapter(Type uiComponentType)
        {
            EnsureInitialized();
            if (uiComponentType == null) return null;

            lock (_lock)
            {
                return _adapters.FirstOrDefault(adapter => adapter.CanHandle(uiComponentType));
            }
        }

        private static void EnsureInitialized()
        {
            if (_initialized) return;

            lock (_lock)
            {
                if (_initialized) return;

                RegisterAdapter(new TextMeshProAdapter());
                RegisterAdapter(new UnityTextAdapter());
                RegisterAdapter(new ToggleAdapter());
                RegisterAdapter(new ButtonTextAdapter());
                RegisterAdapter(new ImageAdapter());

                _initialized = true;
            }
        }
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

        public object AddListener(object uiComponent, Action<object> onValueChanged)
        {
            // TMP_Text does not have an onValueChanged event for editing, it's a display component.
            // If this was TMP_InputField, we would use onValueChanged.
            if (uiComponent is TMP_InputField inputField)
            {
                UnityAction<string> listener = val => onValueChanged(val);
                inputField.onValueChanged.AddListener(listener);
                return listener;
            }
            return null;
        }

        public void RemoveListener(object uiComponent, object token)
        {
            if (uiComponent is TMP_InputField inputField && token is UnityAction<string> listener)
            {
                inputField.onValueChanged.RemoveListener(listener);
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

        public object AddListener(object uiComponent, Action<object> onValueChanged)
        {
            // Text is a display component. InputField is interactive.
            if (uiComponent is InputField inputField)
            {
                UnityAction<string> listener = val => onValueChanged(val);
                inputField.onValueChanged.AddListener(listener);
                return listener;
            }
            return null;
        }

        public void RemoveListener(object uiComponent, object token)
        {
            if (uiComponent is InputField inputField && token is UnityAction<string> listener)
            {
                inputField.onValueChanged.RemoveListener(listener);
            }
        }
    }

    /// <summary>
    /// Adapter for Toggle components. Handles boolean values.
    /// </summary>
    public class ToggleAdapter : IUIAdapter
    {
        public int Priority => 100;

        public bool CanHandle(Type uiComponentType)
        {
            return typeof(Toggle).IsAssignableFrom(uiComponentType);
        }

        public void SetValue(object uiComponent, object value, Type valueType)
        {
            if (uiComponent is Toggle toggle && value is bool boolValue)
            {
                // Temporarily disable the listener to avoid infinite loops if needed, 
                // but usually checking value != current prevents this.
                if (toggle.isOn != boolValue)
                {
                    toggle.isOn = boolValue;
                }
            }
        }

        public object AddListener(object uiComponent, Action<object> onValueChanged)
        {
            if (uiComponent is Toggle toggle)
            {
                UnityAction<bool> listener = val => onValueChanged(val);
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
    /// Adapter for Button components. Sets text on nested TextMeshProUGUI.
    /// </summary>
    public class ButtonTextAdapter : IUIAdapter
    {
        public int Priority => 100;

        public bool CanHandle(Type uiComponentType)
        {
            return typeof(Button).IsAssignableFrom(uiComponentType);
        }

        public void SetValue(object uiComponent, object value, Type valueType)
        {
            if (uiComponent is Button button)
            {
                var textComponent = button.GetComponentInChildren<TMP_Text>();
                if (textComponent != null)
                {
                    textComponent.text = value?.ToString() ?? string.Empty;
                }
            }
        }

        public object AddListener(object uiComponent, Action<object> onValueChanged)
        {
            // Buttons trigger actions, they don't usually hold data values in this context.
            // But if we wanted to bind button click to something, we could.
            // For now, no two-way binding for simple buttons as value holders.
            return null;
        }

        public void RemoveListener(object uiComponent, object token)
        {
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
                image.sprite = value as Sprite;
            }
        }

        public object AddListener(object uiComponent, Action<object> onValueChanged)
        {
            // Images are display components.
            return null;
        }

        public void RemoveListener(object uiComponent, object token)
        {
        }
    }
}
