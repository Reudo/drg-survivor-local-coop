using System;
using System.Collections.Generic;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// Cached scene lookups. FindObjectOfType / FindObjectsOfType walk every object of the scene (and, for inactive ones, much more); with a
    /// run's enemies and bullets one call costs several milliseconds, and the plugin made dozens of them every frame (the frame-rate drop with
    /// the second dwarf was mostly this, measured with `-lcperf`). A found object is kept while it lives; a missing one is searched again at most
    /// every `retry` seconds.
    /// </summary>
    internal static class Find
    {
        private sealed class One_ { public UnityEngine.Object Obj; public float At = -100f; }
        private sealed class Many_ { public object List; public float At = -100f; }
        private static readonly Dictionary<Type, One_> Singles = new Dictionary<Type, One_>();
        private static readonly Dictionary<string, Many_> Lists = new Dictionary<string, Many_>();

        /// <summary>The first active object of type T (FindObjectOfType), cached; a destroyed one is searched again after `retry` seconds.</summary>
        internal static T One<T>(float retry = 0.5f) where T : UnityEngine.Object
        {
            One_ c;
            if (!Singles.TryGetValue(typeof(T), out c)) { c = new One_(); Singles[typeof(T)] = c; }
            if (c.Obj != null) return (T)c.Obj;
            float now = Time.realtimeSinceStartup;
            if (now - c.At < retry) return null;
            c.At = now;
            c.Obj = UnityEngine.Object.FindObjectOfType<T>();
            return c.Obj != null ? (T)c.Obj : null;
        }

        /// <summary>All active objects of type T (FindObjectsOfType), at most as old as `maxAge` seconds.</summary>
        internal static T[] Many<T>(float maxAge) where T : UnityEngine.Object
        {
            var key = typeof(T).FullName + "/a";
            Many_ c;
            if (!Lists.TryGetValue(key, out c)) { c = new Many_(); Lists[key] = c; }
            float now = Time.realtimeSinceStartup;
            if (c.List != null && now - c.At < maxAge) return (T[])c.List;
            c.At = now;
            var arr = UnityEngine.Object.FindObjectsOfType<T>();
            var res = new T[arr.Length];
            for (int i = 0; i < arr.Length; i++) res[i] = arr[i];
            c.List = res;
            return res;
        }

        /// <summary>All objects of type T including inactive ones (screens and forms that exist all the time): searched rarely, re-searched
        /// at once when one of them was destroyed.</summary>
        internal static T[] Forms<T>(float maxAge = 10f) where T : UnityEngine.Object
        {
            var key = typeof(T).FullName + "/i";
            Many_ c;
            if (!Lists.TryGetValue(key, out c)) { c = new Many_(); Lists[key] = c; }
            float now = Time.realtimeSinceStartup;
            var cur = c.List as T[];
            bool stale = cur == null || now - c.At > maxAge;
            if (!stale) foreach (var o in cur) if (o == null) { stale = true; break; }
            if (!stale) return cur;
            if (cur != null && cur.Length == 0 && now - c.At < 1f) return cur;
            c.At = now;
            var arr = UnityEngine.Object.FindObjectsOfType<T>(true);
            var res = new T[arr.Length];
            for (int i = 0; i < arr.Length; i++) res[i] = arr[i];
            c.List = res;
            return res;
        }

        /// <summary>A form of type T is on screen.</summary>
        internal static bool AnyActive<T>() where T : Component
        {
            try { foreach (var f in Forms<T>()) if (f != null && f.gameObject.activeInHierarchy) return true; } catch { }
            return false;
        }
    }
}
