using System;
using System.Threading;

namespace UnityEngine
{
    public sealed class UnitySynchronizationContext : SynchronizationContext { }
    public class Object
    {
        public static bool operator ==(Object left, Object right) => ReferenceEquals(left, right);
        public static bool operator !=(Object left, Object right) => !ReferenceEquals(left, right);
        public override bool Equals(object value) => ReferenceEquals(this, value);
        public override int GetHashCode() => base.GetHashCode();
        public static void DontDestroyOnLoad(Object value) { }
        public static void Destroy(Object value) { }
    }
    public class Component : Object { public GameObject gameObject { get; internal set; } }
    public class Behaviour : Component { }
    public class MonoBehaviour : Behaviour { }
    public sealed class GameObject : Object
    {
        public GameObject(string name) { }
        public T AddComponent<T>() where T : Component
        {
            var component = (T)Activator.CreateInstance(typeof(T), true);
            component.gameObject = this;
            return component;
        }
    }
    public static class Application
    {
        public static event Action quitting;
        public static event Action unloading;
        public static void Quit() { quitting?.Invoke(); unloading?.Invoke(); }
    }
    public static class Debug { public static void LogException(Exception error) { } }
}
