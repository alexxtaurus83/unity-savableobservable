namespace SavableObservable {
    /// <summary>
    /// Lets <see cref="Observable"/> keep a presenter's internal bound flag in sync with the real subscription state.
    /// </summary>
    internal interface IPresenterBindingState {
        void MarkBound();
        void MarkUnbound();
    }
}
