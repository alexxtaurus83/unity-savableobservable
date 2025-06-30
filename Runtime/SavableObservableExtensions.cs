using UnityEngine;
using System;
using System.Collections.Generic;
using System.Reflection;
using Assets.Scripts.Base.SavableObservable;

namespace Assets.Scripts.Base.SavableObservable {
    public static class SavableObservableExtensions {
        /// <summary>
        /// Automatically sets up listeners for all observable variables in a component
        /// </summary>
        /// <param name="obj">The MonoBehaviour component to set up listeners for</param>
        public static void SetListeners(object obj) {
            var dataModel = ((MonoBehaviour)obj).GetComponent<BaseObservableDataModel>();
            if (dataModel == null) return;

            var initMethod = dataModel.GetType().GetMethod("InitFields");
            initMethod?.Invoke(dataModel, null);

            foreach (MemberInfo memberInfo in dataModel.GetType().GetMembers()) {
                if (memberInfo.MemberType == MemberTypes.Field) {
                    FieldInfo field = (FieldInfo)memberInfo;

                    if (!Observable.IsSupportedFieldType(field)) continue;

                    if (Observable.ObservableTypes.types.TryGetValue(field.FieldType, out Type valueType)) {
                        SetOnValueChangedHandler(obj, valueType, field, dataModel);
                    }
                }
            }
        }

        private static void SetOnValueChangedHandler(object obj, Type valueType, FieldInfo field, BaseObservableDataModel dataModel) {
            var method = obj.GetType().GetMethod("OnModelValueChanged", new Type[] { valueType, valueType, typeof(string) });
            if (method == null) {
                throw new Exception($"Can't find 'OnModelValueChanged' method for observable variable with {field.FieldType.Name} type for field {field.Name}.");
            }

            var eventInfo = field.FieldType.GetEvent("OnValueChanged");
            var handler = Delegate.CreateDelegate(eventInfo.EventHandlerType, obj, method);
            eventInfo.AddEventHandler(field.GetValue(dataModel), handler);
        }
    }
}
