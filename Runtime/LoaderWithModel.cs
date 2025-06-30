using Assets.ThirdPaty.Flexiblesavesystem.Scripts.Runtime;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ZLinq;

namespace Assets.Scripts.Base.SavableObservable {


    public abstract class LoaderWithModel<M> : BaseLoader<M> {

        public M GetModel() {
            return GetComponent<M>();
        }

    }
}