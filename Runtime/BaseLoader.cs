using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SavableObservable {


    /// <summary>Aabstract presenter class with model and save methods.</summary>
    /// <typeparam name="M">Regular class or mode of type <see cref="BaseObservableDataModel" /></typeparam>

    [DisallowMultipleComponent]
    //[UnityEngine.RequireComponent(typeof(SaveableEntity))]
    public abstract class BaseLoader<M> : MonoBehaviour  { //ISaveable


        /// <summary>Derived from Saving engine: Return the serializable data structure which shuld be stored</summary>
        /// <returns>Regular class with data or<see cref="BaseObservableDataModel" /></returns>
        public virtual object SaveState() { return GetComponent<M>(); }

        /// <summary>
        /// Derived from Saving engine: (Overriides to support non-observable data model) Load the same serializable data structure back in to the object Don't make relations between objects here, because the nessesary objects may not instantiated here.
        /// </summary>
        /// <param name="state">Non-observable data model</param>
        //public virtual void LoadState(object state) { throw new NotImplementedException(); }
  
        public virtual void LoadState(object state) {
            SavableObservable.SavableObservableExtensions.SetListeners(GetComponent(typeof(BaseObservablePresenter<M>)));
            typeof(M).GetMethod("LoadDataFromModel").Invoke(GetComponent<M>(), new object[] { state });
            //SaveLoadSystem.SaveLoadSystem.loadedFromSaveObjects.Add(this.gameObject.GetComponent<SaveableEntity>().GetID());
        }

        /// <summary>Derived from Saving engine: Check if need to Instantinate prefab</summary>
        /// <returns>Return true, if this object needs to be reinstantiated at load or false if the loading is enough This only works for Prefab Objects<br /></returns>        
        public virtual bool NeedsReinstantiation() { return false; }

        /// <summary>Derived from Saving engine: When returned false, the object will be ignored in the save progress</summary>         
        public virtual bool NeedsToBeSaved() { return true; }

        /// <summary>
        /// Will be called when a loaded Object has been added to its saved parent which is a child of this gameObject. So you get notified here if any objects get added to any child gameObjects of this
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <param name="hisParent">His parent.</param>
        public virtual void GotAddedAsChild(GameObject obj, GameObject hisParent) { }


        /// <summary>Will be called after all saved Objects are instantiated You can make the relations between loaded objects here</summary>
        /// <param name="state">The state.</param>
        public virtual void PostInstantiation(object state) { }

    }
}