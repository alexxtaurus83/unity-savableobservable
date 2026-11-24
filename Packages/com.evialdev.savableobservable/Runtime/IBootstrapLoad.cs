namespace SavableObservable {

    /// <summary>
    /// Synchronous startup hook run by <see cref="ServiceLocator"/> in its <c>Awake</c>, after every scene shared singleton is
    /// registered and before scene presenters are bound. Fill here only what presenters need at bind time (settings, static data).
    /// Called once per locator, sorted by <see cref="BootstrapOrder"/> (ascending; ties keep scene order), for every
    /// MonoBehaviour in the locator's scene, including inactive ones. Exceptions are not caught. Long or async work belongs in
    /// <see cref="IBootstrapStart"/>.
    /// </summary>
    public interface IBootstrapLoad {
        int BootstrapOrder { get; }
        void BootstrapLoad();
    }
}
