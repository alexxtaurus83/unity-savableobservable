using System;
using UnityEngine.Scripting;

namespace SavableObservable {

    /// <summary>
    /// Marker for typed event-bus messages (<c>Services.Publish</c> / <c>Services.Subscribe</c> / <see cref="EventHandlerAttribute"/>).
    /// Implement it on a <c>readonly struct</c> that carries an order-independent notification between MMVC units
    /// ("match finished"), never a command whose result or ordering a caller relies on.
    /// </summary>
    public interface IMmvcEvent { }

    /// <summary>
    /// Subscribes a method to the event bus: <c>[EventHandler] void OnMatchFinished(MatchFinishedEvent e)</c>, where the single
    /// parameter is a struct implementing <see cref="IMmvcEvent"/>.
    /// Bound automatically for registered shared singletons (Logic and services, when <see cref="ServiceLocator"/> registers them)
    /// and for presenters (when they bind to their model); removed when they unregister, unbind or are destroyed.
    /// Not allowed on models (logged as an error, never bound). Implies <see cref="PreserveAttribute"/> (IL2CPP stripping).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class EventHandlerAttribute : PreserveAttribute { }
}
