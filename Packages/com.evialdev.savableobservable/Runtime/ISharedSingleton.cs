namespace SavableObservable {
    /// <summary>
    /// Marker interface for scene-resident shared singletons registered in <see cref="ServiceLocator"/>.
    /// Implemented automatically by <see cref="BaseLogic"/> for MMVC units, and may be implemented manually
    /// by custom non-MMVC <see cref="UnityEngine.MonoBehaviour"/> services.
    /// Presenters (<see cref="IObservablePresenter"/> / <see cref="BaseObservablePresenter{M}"/>), Models
    /// (<see cref="BaseObservableDataModel"/>), and interfaces must NEVER implement <see cref="ISharedSingleton"/>.
    /// </summary>
    public interface ISharedSingleton {
        string Name => GetType().Name;
    }
}
