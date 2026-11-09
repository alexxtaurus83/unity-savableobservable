# Implementation Examples

Adapt these examples to the current project's naming and namespaces. Split public MonoBehaviour classes into matching `.cs` files. Treat snippets as source-reviewed patterns, not a compiled sample project.

## Score Feature

Keep state, rules, and rendering in separate components. `BaseLogic<M, P>` is the primary component that auto-requires `Model` and `Presenter` on the same GameObject.

```csharp
using SavableObservable;

public class ScoreDataModel : BaseObservableDataModel {
    public ObservableVariable<int> score;
}

public class ScoreLogic : BaseLogic<ScoreDataModel, ScorePresenter> {
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

    protected override void InitializePresenter() {
        base.InitializePresenter();
        RefreshDisplay();
    }

    public void RefreshDisplay() {
        if (scoreText != null) {
            scoreText.text = GetModel().score.Value.ToString();
        }
    }
}
```

Attach `ScoreLogic` to a GameObject. The Unity Editor automatically provisions `ScoreDataModel` and `ScorePresenter`. `ServiceLocator` automatically discovers `ScoreLogic` in Pass 1 and initializes/binds `ScorePresenter` in Pass 2.

## Dynamic Prefab

Instantiate the `Logic` component directly:

```csharp
using SavableObservable;
using UnityEngine;

public class ScoreFactory : MonoBehaviour {
    [SerializeField] private ScoreLogic prefab;

    public ScoreLogic Spawn(Transform parent, int startingScore) {
        var scoreLogic = Instantiate(prefab, parent);
        var model = scoreLogic.GetModel();
        model.score.Value = startingScore;
        return scoreLogic;
    }
}
```

`scoreLogic.GetModel()` automatically initializes model fields and privately triggers `EnsureInitialized()` on its sibling `ScorePresenter`, so even inactive prefabs are initialized and bound without manual `SetListeners` calls.

## Manual Subscriber Lifetime

Cross-component observers access the foreign model through `Services.GetModel<M>()` or `Services.Get<Logic>().GetModel()`, never via a direct Presenter reference:

```csharp
using SavableObservable;
using UnityEngine;

public class ScoreObserver : MonoBehaviour {
    [SerializeField] private TMPro.TextMeshProUGUI label;

    private void OnEnable() {
        var model = Services.GetModel<ScoreDataModel>();
        if (model == null) return;
        model.score.OnValueChanged.Add(OnScoreChanged, this);
        OnScoreChanged(model.score);
    }

    private void OnDisable() {
        var model = Services.GetModel<ScoreDataModel>();
        if (model != null) {
            Observable.RemoveListeners(model, this);
        }
    }

    private void OnScoreChanged(ObservableVariable<int> value) {
        if (label != null) label.text = value.Value.ToString();
    }
}
```

## Anti-Pattern: Logic Reaching into Presenter

Logic must never get, store, or call a Presenter. Direct presenter pushes bypass the observable pipeline and couple logic to presentation lifecycles.

### Don't: Calling Presenter from Logic

```csharp
// Anti-pattern: Logic calling Presenter methods directly
public class PlayerLogic : BaseLogic<PlayerDataModel, PlayerPresenter> {
    public void PlayHitEffect() {
        // VIOLATION: Never GetComponent a Presenter or invoke view methods directly
        GetComponent<PlayerPresenter>()?.FlashDamage();
    }
}
```

### Do: Event-Token Pattern for One-Shot View Commands

For transient view triggers (such as VFX, animations, or one-shot audio), publish an event token via the Model:

```csharp
using System;
using SavableObservable;
using UnityEngine;

public enum PlayerFxKind { None = 0, Hit = 1, LevelUp = 2 }

[Serializable]
public struct PlayerFxEvent {
    public PlayerFxKind Kind;
    public int Intensity;
}

public class PlayerDataModel : BaseObservableDataModel {
    [NonSaved] public ObservableVariable<int> eventToken;
    [NonSaved] public PlayerFxEvent latestEvent;

    public override void EnsureFieldsInitialized() {
        base.EnsureFieldsInitialized();
        if (eventToken == null) eventToken = new ObservableVariable<int> { Value = 0 };
    }
}

public class PlayerLogic : BaseLogic<PlayerDataModel, PlayerPresenter> {
    public void PlayHitEffect(int intensity) {
        var model = GetModel();
        if (model == null) return;
        model.latestEvent = new PlayerFxEvent {
            Kind = PlayerFxKind.Hit,
            Intensity = intensity
        };
        model.eventToken.Value++;
    }
}

public class PlayerPresenter : BaseObservablePresenter<PlayerDataModel> {
    [ObservableHandler(nameof(PlayerDataModel.eventToken))]
    private void OnEventTokenChanged(int token) {
        if (token <= 0) return;
        var model = GetModel();
        if (model == null) return;

        switch (model.latestEvent.Kind) {
            case PlayerFxKind.Hit:
                FlashDamage(model.latestEvent.Intensity);
                break;
            case PlayerFxKind.LevelUp:
                PlayCelebrationFx();
                break;
        }
    }

    private void FlashDamage(int intensity) {
        // Play visual effect
    }

    private void PlayCelebrationFx() {
        // Play celebration effect
    }
}
```

## Custom Save-System Integration

Use this loader pattern only with a save system that can supply a compatible model instance.

```csharp
using SavableObservable;

public interface IExampleSaveable {
    object SaveState();
    void LoadState(object state);
}

public class ScoreLoader : LoaderWithModel<ScoreDataModel>, IExampleSaveable {
    public object SaveState() {
        return GetModel();
    }

    public void LoadState(object state) {
        LoadDataFromModel(state);
    }
}
```

## Shared Singleton & Multi-Instance Examples

The framework provides built-in service locator support via `SavableObservable.Services`.

### 1. Single-Instance Lookup

```csharp
using SavableObservable;
using UnityEngine;

public class ScoreConsumer : MonoBehaviour {
    private void Start() {
        // Resolve Logic directly:
        var scoreLogic = Services.Get<ScoreLogic>();
        if (scoreLogic != null) {
            scoreLogic.AddScore(10);
        }

        // Resolve Model directly:
        var scoreModel = Services.GetModel<ScoreDataModel>();
        if (scoreModel != null) {
            Debug.Log($"Current score: {scoreModel.score.Value}");
        }
    }
}
```

### 2. Named Multi-Instance Lookup

When multiple instances of the same Logic class exist in a scene, assign distinct `singletonName` values in the Inspector:

```csharp
using SavableObservable;
using UnityEngine;

public class GameRulesCoordinator : MonoBehaviour {
    private void Start() {
        // DevSettings, BlueAISettings, RedAISettings share the BaseAISettingsLogic class
        var blueAI = Services.Get<BaseAISettingsLogic>("BlueAISettings");
        var redAI = Services.Get<BaseAISettingsLogic>("RedAISettings");

        var blueModel = Services.GetModel<BaseAISettingsDataModel>("BlueAISettings");
    }
}
```

### 3. Custom Non-MMVC MonoBehaviour Services

Plain `MonoBehaviour` services that need global access implement `ISharedSingleton`:

```csharp
using SavableObservable;
using UnityEngine;

public class AudioService : MonoBehaviour, ISharedSingleton {
    public void PlaySound(string soundName) {
        // play audio
    }
}

// Access from anywhere:
Services.Get<AudioService>()?.PlaySound("Click");
```

## Timer with AutoBind and a Handler

Keep ticking and state transitions in logic. Use `InitializePresenter()` for initial setup:

```csharp
using SavableObservable;
using UnityEngine;

public class TimerDataModel : BaseObservableDataModel {
    public ObservableVariable<string> status;
    public ObservableVariable<bool> timerEnabled;
    public ObservableVariable<float> timerValue;
}

public class TimerLogic : BaseLogic<TimerDataModel, TimerPresenter> {
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

    protected override void InitializePresenter() {
        base.InitializePresenter();
        if (statusText != null) statusText.text = GetModel().status.Value;
        OnTimeChanged(GetModel().timerValue.Value);
        OnEnabledChanged(GetModel().timerEnabled.Value);
    }

    public void OnStartClicked() => GetLogic().StartTimer();

    [ObservableHandler(nameof(TimerDataModel.timerValue))]
    private void OnTimeChanged(float value) {
        if (timeText != null) timeText.text = value.ToString("F1");
    }

    [ObservableHandler(nameof(TimerDataModel.timerEnabled))]
    private void OnEnabledChanged(bool enabled) {
        if (timerPanel != null) timerPanel.SetActive(enabled);
    }
}
```

## Reactive Inventory

Use wrapper mutations and match list handler types exactly:

```csharp
using System.Collections.Generic;
using SavableObservable;
using UnityEngine;

public class InventoryDataModel : BaseObservableDataModel {
    public ObservableList<string> items;
}

public class InventoryLogic : BaseLogic<InventoryDataModel, InventoryPresenter> {
    public void AddItem(string item) => GetModel().items.Add(item);
    public void RemoveItem(string item) => GetModel().items.Remove(item);
}

public class InventoryPresenter : BaseObservablePresenter<InventoryDataModel> {
    [SerializeField] private TMPro.TextMeshProUGUI contents;

    protected override void InitializePresenter() {
        base.InitializePresenter();
        if (contents != null) {
            contents.text = string.Join(", ", GetModel().items.Value);
        }
    }

    [ObservableHandler(nameof(InventoryDataModel.items))]
    private void OnItemsChanged(List<string> current, IReadOnlyList<string> previous) {
        if (contents != null) {
            contents.text = string.Join(", ", current);
        }
    }
}
```

## Custom Display Adapter

Bind a float model value to an Image's fill amount through an explicit view component:

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

## Custom Interactive Adapter

Implement both value propagation and exact listener removal:

```csharp
using System;
using SavableObservable;
using UnityEngine.Events;

public class ScrollbarAdapter : IUIListenerAdapter {
    public int Priority => 100;
    public bool CanHandle(Type type) => typeof(UnityEngine.UI.Scrollbar).IsAssignableFrom(type);

    public void SetValue(object component, object value, Type valueType) {
        if (component is UnityEngine.UI.Scrollbar scrollbar && value is float number)
            scrollbar.SetValueWithoutNotify(number);
    }

    public object AddListener(object component, Action<object> onChanged, Type valueType) {
        if (!(component is UnityEngine.UI.Scrollbar scrollbar) || valueType != typeof(float))
            return null;
        UnityAction<float> listener = value => onChanged(value);
        scrollbar.onValueChanged.AddListener(listener);
        return listener;
    }

    public void RemoveListener(object component, object token) {
        if (component is UnityEngine.UI.Scrollbar scrollbar && token is UnityAction<float> listener)
            scrollbar.onValueChanged.RemoveListener(listener);
    }
}
```
