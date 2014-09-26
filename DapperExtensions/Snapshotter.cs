﻿/*
 License: http://www.apache.org/licenses/LICENSE-2.0 
 Home page: http://code.google.com/p/dapper-dot-net/
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Reflection;
using System.Reflection.Emit;
using Dapper;
using DapperExtensions.Mapper;

namespace DapperExtensions
{
    public static class Snapshotter
    {
        public static Snapshot<T> Start<T>(T obj)
            where T : class
        {
            return new Snapshot<T>(obj);
        }

        public abstract class Snapshot
        {
            public class Change
            {
                public string Name { get; set; }
                public object NewValue { get; set; }
            }

            public abstract DynamicParameters Diff();
            public abstract void Revert();
        }

        public class Snapshot<T> : Snapshot
            where T : class
        {
            static Func<T, T> cloner;
            static Func<T, T, List<Change>> differ;
            static Action<T, T> reverter;
            T memberWiseClone;
            T trackedObject;

            public Snapshot(T original)
            {
                memberWiseClone = Clone(original);
                trackedObject = original;
            }

            public override DynamicParameters Diff()
            {
                return Diff(memberWiseClone, trackedObject);
            }

            public override void Revert()
            {
                Revert(trackedObject, memberWiseClone);
            }

            private static T Clone(T myObject)
            {
                cloner = cloner ?? GenerateCloner();
                return cloner(myObject);
            }

            private static void Revert(T origObject, T cloneObject)
            {
                reverter = reverter ?? GenerateReverter();
                reverter(origObject, cloneObject);
            }

            private static DynamicParameters Diff(T original, T current)
            {
                var dm = new DynamicParameters();
                differ = differ ?? GenerateDiffer();
                foreach (var pair in differ(original, current)) {
                    dm.Add(pair.Name, pair.NewValue);
                }
                return dm;
            }

            static List<PropertyInfo> RelevantProperties()
            {
                var x = DapperExtensions.GetMap<T>();
                return x.Properties.Where(p =>
                    !(p.Ignored || p.IsReadOnly || p.IsInsertOnly || p.IsAnyKeyType(KeyType.Identity)) &&
                    p.PropertyInfo.GetSetMethod() != null &&
                    p.PropertyInfo.GetGetMethod() != null &&
                        (p.PropertyInfo.PropertyType.IsValueType ||
                            p.PropertyInfo.PropertyType == typeof(string) ||
                            (p.PropertyInfo.PropertyType.IsGenericType && p.PropertyInfo.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>)))
                        ).Select(i => i.PropertyInfo).ToList();

                //return typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                //    .Where(p =>
                //        p.GetSetMethod() != null &&
                //        p.GetGetMethod() != null &&
                //        (p.PropertyType.IsValueType ||
                //            p.PropertyType == typeof(string) ||
                //            (p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>)))
                //        ).ToList();
            }


            private static bool AreEqual<U>(U first, U second)
            {
                if (first == null && second == null) return true;
                if (first == null && second != null) return false;
                return first.Equals(second);
            }

            private static Func<T, T, List<Change>> GenerateDiffer()
            {
                var dm = new DynamicMethod("DoDiff", typeof(List<Change>), new Type[] { typeof(T), typeof(T) }, true);

                var il = dm.GetILGenerator();
                // change list
                il.DeclareLocal(typeof(List<Change>));
                il.DeclareLocal(typeof(Change));
                il.DeclareLocal(typeof(object)); // boxed change

                il.Emit(OpCodes.Newobj, typeof(List<Change>).GetConstructor(Type.EmptyTypes));
                // [list]
                il.Emit(OpCodes.Stloc_0);

                foreach (var prop in RelevantProperties()) {
                    // []
                    il.Emit(OpCodes.Ldarg_0);
                    // [original]
                    il.Emit(OpCodes.Callvirt, prop.GetGetMethod());
                    // [original prop val]
                    il.Emit(OpCodes.Ldarg_1);
                    // [original prop val, current]
                    il.Emit(OpCodes.Callvirt, prop.GetGetMethod());
                    // [original prop val, current prop val]

                    il.Emit(OpCodes.Dup);
                    // [original prop val, current prop val, current prop val]

                    if (prop.PropertyType != typeof(string)) {
                        il.Emit(OpCodes.Box, prop.PropertyType);
                        // [original prop val, current prop val, current prop val boxed]
                    }

                    il.Emit(OpCodes.Stloc_2);
                    // [original prop val, current prop val]

                    il.EmitCall(OpCodes.Call, typeof(Snapshot<T>).GetMethod("AreEqual", BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(new Type[] { prop.PropertyType }), null);
                    // [result] 

                    Label skip = il.DefineLabel();
                    il.Emit(OpCodes.Brtrue_S, skip);
                    // []

                    il.Emit(OpCodes.Newobj, typeof(Change).GetConstructor(Type.EmptyTypes));
                    // [change]
                    il.Emit(OpCodes.Dup);
                    // [change,change]

                    il.Emit(OpCodes.Stloc_1);
                    // [change]

                    il.Emit(OpCodes.Ldstr, prop.Name);
                    // [change, name]
                    il.Emit(OpCodes.Callvirt, typeof(Change).GetMethod("set_Name"));
                    // []

                    il.Emit(OpCodes.Ldloc_1);
                    // [change]

                    il.Emit(OpCodes.Ldloc_2);
                    // [change, boxed]

                    il.Emit(OpCodes.Callvirt, typeof(Change).GetMethod("set_NewValue"));
                    // []

                    il.Emit(OpCodes.Ldloc_0);
                    // [change list]
                    il.Emit(OpCodes.Ldloc_1);
                    // [change list, change]
                    il.Emit(OpCodes.Callvirt, typeof(List<Change>).GetMethod("Add"));
                    // []

                    il.MarkLabel(skip);
                }

                il.Emit(OpCodes.Ldloc_0);
                // [change list]
                il.Emit(OpCodes.Ret);

                return (Func<T, T, List<Change>>)dm.CreateDelegate(typeof(Func<T, T, List<Change>>));
            }


            // adapted from http://stackoverflow.com/a/966466/17174
            private static Func<T, T> GenerateCloner()
            {
                Delegate myExec = null;
                var dm = new DynamicMethod("DoClone", typeof(T), new Type[] { typeof(T) }, true);
                var ctor = typeof(T).GetConstructor(new Type[] { });

                var il = dm.GetILGenerator();

                il.DeclareLocal(typeof(T));

                il.Emit(OpCodes.Newobj, ctor);
                il.Emit(OpCodes.Stloc_0);

                foreach (var prop in RelevantProperties()) {
                    il.Emit(OpCodes.Ldloc_0);
                    // [clone]
                    il.Emit(OpCodes.Ldarg_0);
                    // [clone, source]
                    il.Emit(OpCodes.Callvirt, prop.GetGetMethod());
                    // [clone, source val]
                    il.Emit(OpCodes.Callvirt, prop.GetSetMethod());
                    // []
                }

                // Load new constructed obj on eval stack -> 1 item on stack
                il.Emit(OpCodes.Ldloc_0);
                // Return constructed object.   --> 0 items on stack
                il.Emit(OpCodes.Ret);

                myExec = dm.CreateDelegate(typeof(Func<T, T>));

                return (Func<T, T>)myExec;
            }

            private static Action<T, T> GenerateReverter()
            {
                Delegate myExec = null;
                var dm = new DynamicMethod("DoRevert", null, new Type[] { typeof(T), typeof(T) }, true);

                var il = dm.GetILGenerator();
                foreach (var prop in RelevantProperties()) {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Callvirt, prop.GetGetMethod());
                    il.Emit(OpCodes.Callvirt, prop.GetSetMethod());
                }
                il.Emit(OpCodes.Ret);
                myExec = dm.CreateDelegate(typeof(Action<T, T>));
                return (Action<T, T>)myExec;
            }

        }
    }
}

