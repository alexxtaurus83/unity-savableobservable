namespace SavableObservable {

    /// <summary>
    /// Startup hook run by <see cref="ServiceLocator"/> in its <c>Start</c>, after all scene presenters are bound.
    /// Start long or asynchronous work here (loading screens, preloads). Called once per locator, sorted by
    /// <see cref="BootstrapOrder"/> (ascending; ties keep scene order), for every MonoBehaviour in the locator's scene that
    /// is still alive. Exceptions are not caught.
    /// </summary>
    public interface IBootstrapStart {
        int BootstrapOrder { get; }
        void BootstrapStart();
    }
}
