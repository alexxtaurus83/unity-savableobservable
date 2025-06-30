using Assets.ThirdPaty.Flexiblesavesystem.Scripts.Runtime;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ZLinq;

namespace Assets.Scripts.Base.SavableObservable {

    public abstract class LoaderWithModelAndLogic<M, LO> : BaseLoader<M> {

        public LO GetLogic() {
            return GetComponent<LO>();
        }

        public M GetModel() {
            return GetComponent<M>();
        }
    }
}