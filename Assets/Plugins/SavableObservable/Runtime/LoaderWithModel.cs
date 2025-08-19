using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SavableObservable {


    public abstract class LoaderWithModel<M> : BaseLoader<M> {

        public M GetModel() {
            return GetComponent<M>();
        }

    }
}