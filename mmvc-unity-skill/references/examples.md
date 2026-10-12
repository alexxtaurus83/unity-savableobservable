# Implementation Examples

Adapt these examples to the current project's naming and namespaces. Split public MonoBehaviour classes into matching `.cs` files. Treat snippets as source-reviewed patterns, not a compiled sample project.

## Score Feature

Keep state, rules, and rendering in separate components. Keep bootstrap ownership explicit rather than adding an automatic `Start` binding to every presenter.

```csharp
using SavableObservable;

public class ScoreDataModel : BaseObservableDataModel {
    public ObservableVariable<int> score;
}

public class ScoreLogic : BaseLogic<ScoreDataModel> {
    public void AddScore(int amount) {
        GetModel().score.Value += amount;
    }
}
```

```csharp
using SavableObservable;
using UnityEngine;

public class ScorePresenter : ObservablePresenterWithLogic<ScoreDataModel, ScoreLogic> {
    [AutoBind(nameof(ScoreDataModel.score))]
    [SerializeField] private TMPro.TextMeshProUGUI scoreText;

    public void Initialize() {
        Observable.SetListeners(this);
        RefreshDisplay();
    }

    public void RefreshDisplay() {
        if (scoreText != null) {
            scoreText.text = GetModel().score.Value.ToString();
        }
    }
}
```

Attach all three components to one GameObject, assign the text reference, and call Initialize from the owning bootstrap after configuration. Initialize fields earlier if logic runs before that point. Wire any requested button action separately; bind labels directly to TextMeshProUGUI or Text components.

## Dynamic Prefab

Use a complete prefab and configure the model before binding and initial presentation:

```csharp
using UnityEngine;

public class ScoreFactory : MonoBehaviour {
    [SerializeField] private ScorePresenter prefab;

    public ScorePresenter Spawn(Transform parent, int startingScore) {
        var presenter = Instantiate(prefab, parent);
        var model = presenter.GetModel();
        model.EnsureFieldsInitialized();
        model.score.Value = startingScore;
        presenter.Initialize();
        return presenter;
    }
}
```

Account for `Awake`/`OnEnable` running during active instantiation, before factory configuration. Keep those callbacks independent of uninitialized state or initialize explicitly at their required boundary. For pooling, add deliberate detach/rebind lifecycle behavior rather than copying this one-shot spawn flow unchanged.

## Manual Subscriber Lifetime

Initialize the model before registering, and detach when the subscriber stops participating. Use this pattern when the model outlives the observing UI. Cross-component observers access the foreign model through its presenter (`presenter.GetModel()`), never via a direct serialized model reference. Do not also run SetListeners for this subscriber afterward without reattaching its manual handler.

```csharp
using SavableObservable;
using UnityEngine;

public class ScoreObserver : MonoBehaviour {
    [SerializeField] private ScorePresenter presenter;
    [SerializeField] private TMPro.TextMeshProUGUI label;

    private void OnEnable() {
        if (presenter == null) return;
        var model = presenter.GetModel();
        if (model == null) return;
        model.EnsureFieldsInitialized();
        model.score.OnValueChanged.Add(OnScoreChanged, this);
        OnScoreChanged(model.score);
    }

    private void OnDisable() {
        if (presenter != null) {
            var model = presenter.GetModel();
            if (model != null) Observable.RemoveListeners(model, this);
        }
    }

    private void OnScoreChanged(ObservableVariable<int> value) {
        if (label != null) label.text = value.Value.ToString();
    }
}
```

## Custom Save-System Integration

Use this loader pattern only with a save system that can supply a compatible model instance. Treat the interface below as an example project contract, not a framework API. Do not construct MonoBehaviours with `new` or assume a JSON serializer can round-trip them.

```csharp
using SavableObservable;

public interface IExampleSaveable {
    object SaveState();
    void LoadState(object state);
}

public class ScoreLoader : LoaderWithModel<ScoreDataModel>, IExampleSaveable {
    public object SaveState() {
        return GetModelToSave();
    }

    public void LoadState(object state) {
        GetModel().EnsureFieldsInitialized();
        LoadDataFromModel(state);
        var presenter = GetComponent<ScorePresenter>();
        if (presenter != null) presenter.RefreshDisplay();
    }
}
```

For a DTO-based save system, map DTO values explicitly in the custom loader instead of passing an unrelated DTO to the base implementation. Test reload while listeners are already attached: this example does not suppress their notifications.

## Optional Singleton Integration

Use the following application-level example only when the project needs a service registry and does not already provide one. Keep it separate from framework code. Register all services first, then bind and refresh; defer consumer access until initialization has completed.

```csharp
using System;
using System.Collections.Generic;
using SavableObservable;
using UnityEngine;

public interface ISharedSingleton { }

public static class ExampleServices {
    private static readonly Dictionary<Type, MonoBehaviour> Items = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => Items.Clear();

    public static void Register(MonoBehaviour service) {
        var type = service.GetType();
        if (Items.TryGetValue(type, out var existing) && existing != null)
            throw new InvalidOperationException($"Duplicate service: {type.Name}");
        Items[type] = service;
    }

    public static T Get<T>() where T : MonoBehaviour, ISharedSingleton {
        return Items.TryGetValue(typeof(T), out var service) ? service as T : null;
    }

    public static void Unregister(MonoBehaviour service) {
        var type = service.GetType();
        if (Items.TryGetValue(type, out var existing) && existing == service)
            Items.Remove(type);
    }
}

public class SharedScorePresenter : ObservablePresenterWithLogic<ScoreDataModel, ScoreLogic>, ISharedSingleton {
    [AutoBind(nameof(ScoreDataModel.score))]
    [SerializeField] private TMPro.TextMeshProUGUI scoreText;

    public void Initialize() {
        Observable.SetListeners(this);
        scoreText.text = GetModel().score.Value.ToString();
    }

    private void OnDestroy() => ExampleServices.Unregister(this);
}

public class ExampleServiceBootstrap : MonoBehaviour {
    [SerializeField] private SharedScorePresenter scoreService;

    private void Start() {
        ExampleServices.Register(scoreService);
        scoreService.Initialize();
        ExampleServices.Get<SharedScorePresenter>().GetLogic().AddScore(1);
    }
}
```

Assign the service reference and required model/logic/UI components in the scene. For multiple services, register every instance before binding any of them. Do not rely on unrelated components' `Start` ordering; invoke dependent initialization explicitly from the bootstrap. For additive scenes and persistent managers, define ownership and unregister lifetimes deliberately.

### Optional Automatic Service Discovery

As an alternative to serialized service references, discover scene components implementing the marker interface. Use this bootstrap instead of `ExampleServiceBootstrap`, not alongside it. Reuse the `ISharedSingleton` and `ExampleServices` definitions above. Add the following application-level interface to services participating in this discovery flow, and have `SharedScorePresenter` implement it using its existing Initialize method:

```csharp
public interface IInitializableSharedService : ISharedSingleton {
    void Initialize();
}
```

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

public class DiscoveringServiceBootstrap : MonoBehaviour {
    private void Start() {
        var services = new List<MonoBehaviour>();
        var types = new HashSet<Type>();
        foreach (var component in FindObjectsByType<MonoBehaviour>(
            FindObjectsInactive.Include, FindObjectsSortMode.None)) {
            if (!(component is IInitializableSharedService)) continue;
            if (!component.gameObject.scene.IsValid() || !component.gameObject.scene.isLoaded)
                continue;
            if (!types.Add(component.GetType()))
                throw new InvalidOperationException($"Duplicate service: {component.GetType().Name}");
            services.Add(component);
        }

        foreach (var service in services)
            ExampleServices.Register(service);

        foreach (var service in services)
            ((IInitializableSharedService)service).Initialize();
    }
}
```

Let each service's Initialize method own binding and initial rendering; do not bind it again in the discovery loop. Registration completes before initialization begins, but unsorted discovery does not establish initialization dependency order. Use an explicit dependency order if one service requires another to be fully initialized, not merely registered. Call consumer startup after these passes rather than relying on unrelated `Start` methods.

Include inactive objects only intentionally. Ensure services selected this way can initialize while inactive, and define what happens when later additive scenes introduce another instance. Run discovery at the owning startup boundary, not every frame.

## Timer with AutoBind and a Handler

Keep ticking and state transitions in logic. Use a handler for visibility and explicit initial rendering to avoid replaying transition effects.

```csharp
using SavableObservable;
using UnityEngine;

public class TimerDataModel : BaseObservableDataModel {
    public ObservableVariable<string> status;
    public ObservableVariable<bool> timerEnabled;
    public ObservableVariable<float> timerValue;
}

public class TimerLogic : BaseLogic<TimerDataModel> {
    public void StartTimer() {
        GetModel().status.Value = "Timer Running";
        GetModel().timerEnabled.Value = true;
    }

    public void Tick(float deltaTime) {
        if (GetModel().timerEnabled.Value)
            GetModel().timerValue.Value += deltaTime;
    }
}

public class TimerPresenter : ObservablePresenterWithLogic<TimerDataModel, TimerLogic> {
    [AutoBind(nameof(TimerDataModel.status))]
    [SerializeField] private TMPro.TextMeshProUGUI statusText;
    [SerializeField] private TMPro.TextMeshProUGUI timeText;
    [SerializeField] private GameObject timerPanel;

    public void Initialize() {
        Observable.SetListeners(this);
        statusText.text = GetModel().status.Value;
        OnTimeChanged(GetModel().timerValue.Value);
        OnEnabledChanged(GetModel().timerEnabled.Value);
    }

    public void OnStartClicked() => GetLogic().StartTimer();

    [ObservableHandler(nameof(TimerDataModel.timerValue))]
    private void OnTimeChanged(float value) => timeText.text = value.ToString("F1");

    [ObservableHandler(nameof(TimerDataModel.timerEnabled))]
    private void OnEnabledChanged(bool enabled) => timerPanel.SetActive(enabled);
}
```

Assign all view references; keep the presenter outside the panel it hides. Call Initialize from the owner before input is enabled, and call Tick from the game loop with the desired scaled or unscaled delta time. Wire the button to OnStartClicked. Do not place time progression in a rendering handler.

## Reactive Inventory

Use wrapper mutations and match list handler types exactly:

```csharp
using System.Collections.Generic;
using SavableObservable;
using UnityEngine;

public class InventoryDataModel : BaseObservableDataModel {
    public ObservableList<string> items;
}

public class InventoryLogic : BaseLogic<InventoryDataModel> {
    public void AddItem(string item) => GetModel().items.Add(item);
    public void RemoveItem(string item) => GetModel().items.Remove(item);
}

public class InventoryPresenter : BaseObservablePresenter<InventoryDataModel> {
    [SerializeField] private TMPro.TextMeshProUGUI contents;

    public void Initialize() {
        Observable.SetListeners(this);
        contents.text = string.Join(", ", GetModel().items.Value);
    }

    [ObservableHandler(nameof(InventoryDataModel.items))]
    private void OnItemsChanged(List<string> current, IReadOnlyList<string> previous) {
        contents.text = string.Join(", ", current);
    }
}
```

Use `previous` for presentation that needs a before/after comparison. Avoid `items.Value.Add`, which bypasses notifications. For manual observation, use `items.OnChanged.Add(handler, subscriber)` with a handler accepting `ObservableList<string>` and pair it with teardown as shown in the manual subscriber example.

## Custom Display Adapter

Bind a float model value to an Image's fill amount through an explicit view component, avoiding replacement of the built-in Image sprite adapter:

```csharp
using System;
using SavableObservable;
using UnityEngine;

public class FillView : MonoBehaviour {
    [SerializeField] private UnityEngine.UI.Image image;
    public void SetFill(float value) => image.fillAmount = Mathf.Clamp01(value);
}

public class FillViewAdapter : IUIAdapter {
    public int Priority => 100;
    public bool CanHandle(Type type) => typeof(FillView).IsAssignableFrom(type);
    public void SetValue(object component, object value, Type valueType) {
        if (component is FillView view && value is float amount)
            view.SetFill(amount);
    }
}
```

Configure the Image as Filled, assign it on FillView, and bind a serialized FillView field using AutoBind. Register the adapter before initializing the presenter.

## Custom Interactive Adapter

Implement both value propagation and exact listener removal. Keep this example restricted to float observables rather than claiming arbitrary numeric conversion:

```csharp
using System;
using SavableObservable;
using UnityEngine.Events;

public class FloatSliderAdapter : IUIListenerAdapter {
    public int Priority => 100;
    public bool CanHandle(Type type) => typeof(UnityEngine.UI.Slider).IsAssignableFrom(type);

    public void SetValue(object component, object value, Type valueType) {
        if (component is UnityEngine.UI.Slider slider && value is float number)
            slider.SetValueWithoutNotify(number);
    }

    public object AddListener(object component, Action<object> onChanged, Type valueType) {
        if (!(component is UnityEngine.UI.Slider slider) || valueType != typeof(float))
            return null;
        UnityAction<float> listener = value => onChanged(value);
        slider.onValueChanged.AddListener(listener);
        return listener;
    }

    public void RemoveListener(object component, object token) {
        if (component is UnityEngine.UI.Slider slider && token is UnityAction<float> listener)
            slider.onValueChanged.RemoveListener(listener);
    }
}
```

Register from the same initialization owner before SetListeners:

```csharp
UIAdapterRegistry.RegisterAdapter(new FillViewAdapter());
UIAdapterRegistry.RegisterAdapter(new FloatSliderAdapter());
```

Use `UIAdapterRegistry.GetAdapter(typeof(FillView))` for display adapter lookup and `UIAdapterRegistry.TryGetListenerAdapter(typeof(UnityEngine.UI.Slider), out var adapter)` when interactive behavior is required. Configure slider limits and whole-number behavior to match model semantics. Supply an explicit initial refresh; registration and binding do not populate the initial value.
