namespace SavableObservable {
    /// <summary>
    /// Lets <see cref="Observable"/> run a presenter's one-time initialization before attaching handlers
    /// and keep the presenter's internal bound flag in sync with the real subscription state.
    /// </summary>
    internal interface IPresenterBindingState {
        /// <summary>Runs <c>InitializePresenter()</c> once if it has not completed yet (no-op while it is already running).</summary>
        void EnsureInitializedForBinding();
        void MarkBound();
        void MarkUnbound();
        void CleanupPresenterLifetime();
    }
}
