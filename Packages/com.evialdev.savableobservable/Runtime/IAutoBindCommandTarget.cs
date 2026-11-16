namespace SavableObservable {

    /// <summary>
    /// Explicit contract for presenters that provide a paired Logic component for command-aware AutoBind and ClickCommand.
    /// </summary>
    public interface IAutoBindCommandTarget {
        /// <summary>
        /// Returns the paired Logic component that owns commands for this presenter.
        /// </summary>
        BaseLogic GetAutoBindCommandTarget();
    }
}
