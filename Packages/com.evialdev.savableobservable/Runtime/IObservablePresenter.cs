namespace SavableObservable {
    public interface IObservablePresenter {
        BaseObservableDataModel GetObservableModel();
        void EnsureInitialized();
        void EnsurePresenterInitialized();
    }
}
