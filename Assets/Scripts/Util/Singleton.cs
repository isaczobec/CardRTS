using UnityEngine;

/// <summary>
/// A singleton class with `instance` as the singleton instance.
/// </summary> 
/// <typeparam name="T">The type of the class which will have a singleton created.</typeparam>
public abstract class Singleton<T> : MonoBehaviour where T : Singleton<T>
{
    /// <summary>
    /// The static instance of this class. 
    /// </summary>
    public static T instance { get; private set; }

    /// <summary>
    /// Remember to call `base.Awake()` first if overriding this method so that `instance` will still be set.
    /// </summary>
    protected virtual void Awake()
    {
        if (instance != null)
        {
            Debug.Log("Another singleton already exists, deleting this one!");
            Destroy(gameObject);
            return;
        }

        instance = (T)this;
    }
}