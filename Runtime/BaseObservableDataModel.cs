using System;
using System.Reflection;
using UnityEngine;

namespace SavableObservable {

    /// <summary>Abstract DataModel class to keep observable keep data with ObservableVariable types</summary>
    [DisallowMultipleComponent]
    [Serializable]
    public abstract class BaseObservableDataModel : MonoBehaviour {

        /// <summary>
        /// Initializes a new instance of the <see cref="BaseObservableDataModel" /> class.
        /// Automatically creates instances of all fields with ObservableVariable type passing field name as parameter
        /// </summary>
        public void InitFields() {
            foreach (MemberInfo memberInfo in this.GetType().GetMembers()) {
                if (memberInfo.MemberType == MemberTypes.Field) {
                    FieldInfo field = (FieldInfo)memberInfo;
                    if (Observable.IsSupportedFieldType(field)) {
                        var instance = Activator.CreateInstance(field.FieldType, new object[] { field.Name });
                        field.SetValue(this, instance);
                    }
                }
            }
        }


        /// <summary>
        /// Loads the data from saved model of <see cref="BaseObservableDataModel" /> to the <see cref="ObservableVariable" /> types at current <see cref="BaseObservableDataModel" /> model. Do not change name of the Method as it used in reflection.
        /// </summary>
        /// <param name="model">The model from save of the type <see cref="BaseObservableDataModel" /></param>
        public void LoadDataFromModel(object model) {
            foreach (MemberInfo memberInfo in GetType().GetMembers()) {
                switch (memberInfo.MemberType) {
                    case MemberTypes.Property:
                        var prop = (PropertyInfo)memberInfo;
                        prop.SetValue(this, prop.GetValue(model));
                        break;
                    case MemberTypes.Field:
                        var field = (FieldInfo)memberInfo;
                        if (Observable.IsSupportedFieldType(field)) {
                            var fieldProp = field.FieldType.GetProperty("Value");
                            fieldProp.SetValue(field.GetValue(this), fieldProp.GetValue(field.GetValue(model)));
                        } else {
                            field.SetValue(this, field.GetValue(model));
                        }
                        break;
                }
            }
        }

    }
}