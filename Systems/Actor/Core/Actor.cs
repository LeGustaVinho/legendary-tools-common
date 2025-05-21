using System;
using System.Collections;
using System.Collections.Generic;
using LegendaryTools.Systems.AssetProvider;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace LegendaryTools.Actor
{
    [Serializable]
    public abstract class Actor : IActor
    {
        protected static ActorSystemAssetLoadableConfig Config;
        protected static List<AssetLoaderConfig> PreloadQueue;
        protected static readonly Dictionary<Type, List<Actor>> AllActorsByType = new Dictionary<Type, List<Actor>>();
        
#if ODIN_INSPECTOR 
        [Sirenix.OdinInspector.ShowInInspector]
        [Sirenix.OdinInspector.HideInEditorMode]
#endif
        public ActorMonoBehaviour ActorBehaviour { get; protected set; }
#if ODIN_INSPECTOR 
        [Sirenix.OdinInspector.ShowInInspector]
        [Sirenix.OdinInspector.HideInEditorMode]
#endif
        public  GameObject Prefab { get; protected set; }
        
        public bool IsDestroyed { get; private set; }
        
#if ODIN_INSPECTOR 
        [Sirenix.OdinInspector.ShowInInspector]
        [Sirenix.OdinInspector.HideInEditorMode]
#endif
        public bool IsAlive => !IsDestroyed;
        public bool HasBody => ActorBehaviour != null;
        
        public event Action<Actor, ActorMonoBehaviour> OnAsyncActorBodyLoaded;
        public event Action<Actor, ActorMonoBehaviour> OnPossession;
        public event Action<Actor, ActorMonoBehaviour> OnEjected;
        public event Action<Actor, ActorMonoBehaviour> OnDestroyed;

        private ILoadOperation handler;

        public Actor()
        {
            RegisterActor();
        }
        
        public Actor(bool autoCreateGameObject) : this()
        {
            if(autoCreateGameObject)
            {
                void CreateAndInitGameObject(object prefabActor)
                {
                    if (prefabActor is ActorMonoBehaviour actorMonoBehaviourPrefab)
                    {
                        Prefab = actorMonoBehaviourPrefab.gameObject;
                    }
                    else if(prefabActor is GameObject actorMonoBehaviourPrefabGameObject)
                    {
                        if (actorMonoBehaviourPrefabGameObject.GetComponent<ActorMonoBehaviour>() != null)
                        {
                            Prefab = actorMonoBehaviourPrefabGameObject;
                        }
                    }
                
                    OnAsyncActorBodyLoaded?.Invoke(this, ActorBehaviour);
                    RegenerateBody();
                }
            
                if (Config != null)
                {
                    if (Config.TypeByActorAssetLoadersTable.TryGetValue(this.GetType(), out AssetLoaderConfig assetLoaderConfig))
                        handler = assetLoaderConfig.LoadWithCoroutines<ActorMonoBehaviour>(CreateAndInitGameObject);
                    else
                        CreateAndInitGameObject(Prefab);
                }
                else
                {
                    CreateAndInitGameObject(Prefab);
                }
            }
        }

        public Actor(GameObject prefab = null, string name = "") : this()
        {
            Prefab = prefab;
            RegenerateBody();
        }

        public Actor(ActorMonoBehaviour actorBehaviour) : this()
        {
            Possess(actorBehaviour);
        }

#if ODIN_INSPECTOR 
        [Sirenix.OdinInspector.ShowInInspector]
        [Sirenix.OdinInspector.HideInEditorMode]
#endif
        public virtual bool Possess(ActorMonoBehaviour target)
        {
            if (target.Actor != null)
            {
                return false;
            }

            Eject();

            ActorBehaviour = target;
            ActorBehaviour.BindActor(this);
            RegisterActorBehaviourEvents();
            OnPossession?.Invoke(this, target);
            return true;
        }

#if ODIN_INSPECTOR 
        [Sirenix.OdinInspector.ShowInInspector]
        [Sirenix.OdinInspector.HideInEditorMode]
        [Sirenix.OdinInspector.ShowIf("HasBody")]
#endif
        public virtual void Eject()
        {
            if (ActorBehaviour != null)
            {
                UnRegisterActorBehaviourEvents();
                ActorBehaviour.UnBindActor();
                ActorMonoBehaviour aux = ActorBehaviour;
                ActorBehaviour = null;
                OnEjected?.Invoke(this, aux);
            }
        }

#if ODIN_INSPECTOR 
        [Sirenix.OdinInspector.ShowInInspector]
        [Sirenix.OdinInspector.HideInEditorMode]
        [Sirenix.OdinInspector.HideIf("HasBody")]
#endif
        public virtual void RegenerateBody(string name = "")
        {
            if (!HasBody)
            {
                GameObject newGameObject = CreateGameObject(string.IsNullOrEmpty(name) ? GetType().ToString() : name, Prefab);
                ActorMonoBehaviour actorMonoBehaviour = AddOrGetActorBehaviour(newGameObject);
                actorMonoBehaviour.AutoCreateActor = false;
                Possess(actorMonoBehaviour);
            }
        }
#if ODIN_INSPECTOR 
        [Sirenix.OdinInspector.ShowIf("HasBody")]
        [Sirenix.OdinInspector.ShowIf("IsAlive")]
#endif
        public virtual void Dispose()
        {
            void DestroyMyGameObject()
            {
                if (ActorBehaviour != null)
                {
                    DestroyGameObject(ActorBehaviour);
                }
            }
            
            InternalOnDestroy(DestroyMyGameObject);
        }
        
        protected virtual GameObject CreateGameObject(string name = "", GameObject prefab = null)
        {
            if (prefab == null)
            {
                return new GameObject(name);
            }

            return Object.Instantiate(prefab);
        }
        
        protected virtual ActorMonoBehaviour AddOrGetActorBehaviour(GameObject gameObject)
        {
            ActorMonoBehaviour actorMonoBehaviour = gameObject.GetComponent<ActorMonoBehaviour>();
            if (actorMonoBehaviour == null)
            {
                actorMonoBehaviour = gameObject.AddComponent<ActorMonoBehaviour>();
            }
            return actorMonoBehaviour;
        }
        
        protected void RegisterActorBehaviourEvents()
        {
            ActorBehaviour.WhenAwake += Awake;
            ActorBehaviour.WhenStart += Start;
            ActorBehaviour.WhenUpdate += Update;
            ActorBehaviour.WhenDestroy += InternalOnDestroy;
            ActorBehaviour.WhenEnable += OnEnable;
            ActorBehaviour.WhenDisable += OnDisable;
            ActorBehaviour.WhenTriggerEnter += OnTriggerEnter;
            ActorBehaviour.WhenTriggerExit += OnTriggerExit;
            ActorBehaviour.WhenCollisionEnter += OnCollisionEnter;
            ActorBehaviour.WhenCollisionExit += OnCollisionExit;
            ActorBehaviour.WhenLateUpdate += LateUpdate;
            ActorBehaviour.WhenFixedUpdate += FixedUpdate;
            ActorBehaviour.WhenCollisionStay += OnCollisionStay;
            ActorBehaviour.WhenApplicationFocus += OnApplicationFocus;
            ActorBehaviour.WhenApplicationPause += OnApplicationPause;
            ActorBehaviour.WhenApplicationQuit += OnApplicationQuit;
            ActorBehaviour.WhenBecameVisible += OnBecameVisible;
            ActorBehaviour.WhenBecameInvisible += OnBecameInvisible;
            ActorBehaviour.WhenCollisionEnter2D += OnCollisionEnter2D;
            ActorBehaviour.WhenCollisionStay2D += OnCollisionStay2D;
            ActorBehaviour.WhenCollisionExit2D += OnCollisionExit2D;
            ActorBehaviour.WhenDrawGizmos += OnDrawGizmos;
            ActorBehaviour.WhenDrawGizmosSelected += OnDrawGizmosSelected;
            ActorBehaviour.WhenGUI += OnGUI;
            ActorBehaviour.WhenPreCull += OnPreCull;
            ActorBehaviour.WhenPreRender += OnPreRender;
            ActorBehaviour.WhenPostRender += OnPostRender;
            ActorBehaviour.WhenRenderImage += OnRenderImage;
            ActorBehaviour.WhenRenderObject += OnRenderObject;
            ActorBehaviour.WhenTransformChildrenChanged += OnTransformChildrenChanged;
            ActorBehaviour.WhenTransformParentChanged += OnTransformParentChanged;
            ActorBehaviour.WhenTriggerEnter2D += OnTriggerEnter2D;
            ActorBehaviour.WhenTriggerStay2D += OnTriggerStay2D;
            ActorBehaviour.WhenTriggerExit2D += OnTriggerExit2D;
            ActorBehaviour.WhenValidate += OnValidate;
            ActorBehaviour.WhenWillRenderObject += OnWillRenderObject;
        }

        protected void UnRegisterActorBehaviourEvents()
        {
            ActorBehaviour.WhenAwake -= Awake;
            ActorBehaviour.WhenStart -= Start;
            ActorBehaviour.WhenUpdate -= Update;
            ActorBehaviour.WhenDestroy -= InternalOnDestroy;
            ActorBehaviour.WhenEnable -= OnEnable;
            ActorBehaviour.WhenDisable -= OnDisable;
            ActorBehaviour.WhenTriggerEnter -= OnTriggerEnter;
            ActorBehaviour.WhenTriggerExit -= OnTriggerExit;
            ActorBehaviour.WhenCollisionEnter -= OnCollisionEnter;
            ActorBehaviour.WhenCollisionExit -= OnCollisionExit;
            ActorBehaviour.WhenLateUpdate -= LateUpdate;
            ActorBehaviour.WhenFixedUpdate -= FixedUpdate;
            ActorBehaviour.WhenCollisionStay -= OnCollisionStay;
            ActorBehaviour.WhenApplicationFocus -= OnApplicationFocus;
            ActorBehaviour.WhenApplicationPause -= OnApplicationPause;
            ActorBehaviour.WhenApplicationQuit -= OnApplicationQuit;
            ActorBehaviour.WhenBecameVisible -= OnBecameVisible;
            ActorBehaviour.WhenBecameInvisible -= OnBecameInvisible;
            ActorBehaviour.WhenCollisionEnter2D -= OnCollisionEnter2D;
            ActorBehaviour.WhenCollisionStay2D -= OnCollisionStay2D;
            ActorBehaviour.WhenCollisionExit2D -= OnCollisionExit2D;
            ActorBehaviour.WhenDrawGizmos -= OnDrawGizmos;
            ActorBehaviour.WhenDrawGizmosSelected -= OnDrawGizmosSelected;
            ActorBehaviour.WhenGUI -= OnGUI;
            ActorBehaviour.WhenPreCull -= OnPreCull;
            ActorBehaviour.WhenPreRender -= OnPreRender;
            ActorBehaviour.WhenPostRender -= OnPostRender;
            ActorBehaviour.WhenRenderImage -= OnRenderImage;
            ActorBehaviour.WhenRenderObject -= OnRenderObject;
            ActorBehaviour.WhenTransformChildrenChanged -= OnTransformChildrenChanged;
            ActorBehaviour.WhenTransformParentChanged -= OnTransformParentChanged;
            ActorBehaviour.WhenTriggerEnter2D -= OnTriggerEnter2D;
            ActorBehaviour.WhenTriggerStay2D -= OnTriggerStay2D;
            ActorBehaviour.WhenTriggerExit2D -= OnTriggerExit2D;
            ActorBehaviour.WhenValidate -= OnValidate;
            ActorBehaviour.WhenWillRenderObject -= OnWillRenderObject;
        }

        protected virtual void RegisterActor()
        {
            Type type = this.GetType();
            if (!AllActorsByType.ContainsKey(type))
            {
                AllActorsByType.Add(type, new List<Actor>());
            }

            if (!AllActorsByType[type].Contains(this))
            {
                AllActorsByType[type].Add(this);
            }
        }

        protected virtual void UnRegisterActor()
        {
            Type type = this.GetType();
            if (AllActorsByType.ContainsKey(type))
            {
                if (AllActorsByType[type].Contains(this))
                {
                    AllActorsByType[type].Remove(this);
                }
            }
        }

        protected virtual void DestroyGameObject(ActorMonoBehaviour actorBehaviour)
        {
#if UNITY_EDITOR
            Object.DestroyImmediate(actorBehaviour.GameObject);
#else
            Object.Destroy(actorBehaviour.GameObject);
#endif
        }

        private void InternalOnDestroy()
        {
            InternalOnDestroy(null);
        }

        private void InternalOnDestroy(Action afterActorBehaviourCleanUp)
        {
            StopAllCoroutines();
            IsDestroyed = true;
            OnDestroy();
            OnDestroyed?.Invoke(this, ActorBehaviour);
            
            UnRegisterActorBehaviourEvents();
            UnRegisterActor();
            if (ActorBehaviour != null)
            {
                ActorBehaviour.UnBindActor();
                afterActorBehaviourCleanUp?.Invoke();
            }

            ActorBehaviour = null;

            if (handler != null)
            {
                handler.Release();
                handler = null;
            }
        }

        private bool CanExecuteCommandToBody()
        {
            if (ActorBehaviour == null)
            {
                Debug.LogError("[Actor] You are trying to send a command, but there is no body (AKA MonoBehaviour) connected.");
                return false;
            }
            
            if (IsDestroyed)
            {
                Debug.LogError("[Actor] You are trying to send a command, but this Actor was marked as destroyed.");
                return false;
            }

            return true;
        }

        #region Static
        
        public static void Initialize(ActorSystemAssetLoadableConfig config, Action onInitialize = null)
        {
            Config = config;
            Config.Initialize();
            PreloadQueue = new List<AssetLoaderConfig>(Config.TypeByActorAssetLoaders.Count);
            foreach (KeyValuePair<Type, AssetLoaderConfig> pair in Config.TypeByActorAssetLoadersTable)
            {
                PreloadQueue.Add(pair.Value);
            }
            MonoBehaviourFacade.Instance.StartCoroutine(PreloadingAssets(onInitialize));
        }

        public static T AddOrGetActorComponent<T>(GameObject gameObject)
            where T : ActorMonoBehaviour
        {
            T actorMonoBehaviour = gameObject.GetComponent<T>();
            if (actorMonoBehaviour == null)
            {
                actorMonoBehaviour = gameObject.AddComponent<T>();
            }
            return actorMonoBehaviour;
        }
        
        public static void Destroy(Actor actor)
        {
            actor.Dispose();
        }

        public static Actor FindObjectOfType(Type type)
        {
            if (AllActorsByType.TryGetValue(type, out List<Actor> actors))
            {
                return actors.FirstOrDefault();
            }

            return null;
        }

        public static T FindObjectOfType<T>() where T : Actor
        {
            if (AllActorsByType.TryGetValue(typeof(T), out List<Actor> actors))
            {
                return actors.FirstOrDefault() as T;
            }

            return null;
        }

        public static Actor[] FindObjectsOfType(Type type)
        {
            if (AllActorsByType.TryGetValue(type, out List<Actor> actors))
            {
                return actors.ToArray();
            }

            return null;
        }

        public static Actor[] FindObjectsOfType<T>() where T : Actor
        {
            if (AllActorsByType.TryGetValue(typeof(T), out List<Actor> actors))
            {
                return actors.ToArray();
            }

            return null;
        }
        
        private static IEnumerator PreloadingAssets(Action onInitialize = null)
        {
            for (int i = PreloadQueue.Count - 1; i >= 0; i--)
            {
                AssetLoaderConfig item = PreloadQueue[i];
                item.PrepareLoadRoutine<ActorMonoBehaviour>();
                yield return item.WaitLoadRoutine();
                PreloadQueue.Remove(item);
            }
            onInitialize?.Invoke();
        }
        
        #endregion
        
        #region MonoBehaviour calls

        protected virtual void Awake()
        {
        }

        protected virtual void Start()
        {
        }

        protected virtual void Update()
        {
        }

        protected virtual void OnDestroy()
        {
        }

        protected virtual void OnEnable()
        {
        }

        protected virtual void OnDisable()
        {
        }

        protected virtual void OnTriggerEnter(Collider other)
        {
        }

        protected virtual void OnTriggerExit(Collider other)
        {
        }

        protected virtual void OnCollisionEnter(Collision collision)
        {
        }

        protected virtual void OnCollisionExit(Collision other)
        {
        }

        protected virtual void LateUpdate()
        {
        }

        protected virtual void FixedUpdate()
        {
        }

        protected virtual void OnCollisionStay(Collision obj)
        {
        }

        protected virtual void OnApplicationFocus(bool obj)
        {
        }

        protected virtual void OnApplicationPause(bool obj)
        {
        }

        protected virtual void OnApplicationQuit()
        {
        }

        protected virtual void OnBecameVisible()
        {
        }

        protected virtual void OnBecameInvisible()
        {
        }

        protected virtual void OnCollisionEnter2D(Collision2D obj)
        {
        }

        protected virtual void OnCollisionStay2D(Collision2D obj)
        {
        }

        protected virtual void OnCollisionExit2D(Collision2D obj)
        {
        }

        protected virtual void OnDrawGizmos()
        {
        }

        protected virtual void OnDrawGizmosSelected()
        {
        }

        protected virtual void OnGUI()
        {
        }

        protected virtual void OnPreCull()
        {
        }

        protected virtual void OnPreRender()
        {
        }

        protected virtual void OnPostRender()
        {
        }

        protected virtual void OnRenderImage(RenderTexture arg1, RenderTexture arg2)
        {
        }

        protected virtual void OnRenderObject()
        {
        }

        protected virtual void OnTransformChildrenChanged()
        {
        }

        protected virtual void OnTransformParentChanged()
        {
        }

        protected virtual void OnTriggerEnter2D(Collider2D obj)
        {
        }

        protected virtual void OnTriggerStay2D(Collider2D obj)
        {
        }

        protected virtual void OnTriggerExit2D(Collider2D obj)
        {
        }

        protected virtual void OnValidate()
        {
        }

        protected virtual void OnWillRenderObject()
        {
        }

        #endregion

        #region Interfaces Implementations

        public string Name
        {
            get => ActorBehaviour.GameObject.name;
            set => ActorBehaviour.GameObject.name = value;
        }

        public string Tag
        {
            get => ActorBehaviour.GameObject.tag;
            set => ActorBehaviour.GameObject.tag = value;
        }

        public HideFlags HideFlags
        {
            get => ActorBehaviour.GameObject.hideFlags;
            set => ActorBehaviour.GameObject.hideFlags = value;
        }

        public bool ActiveInHierarchy => ActorBehaviour.GameObject.activeInHierarchy;
        public bool ActiveSelf => ActorBehaviour.GameObject.activeInHierarchy;

        public int Layer
        {
            get => ActorBehaviour.GameObject.layer;
            set => ActorBehaviour.GameObject.layer = value;
        }

        public Scene Scene => ActorBehaviour.GameObject.scene;

        public void SetActive(bool value)
        {
            if (CanExecuteCommandToBody())
                ActorBehaviour.GameObject.SetActive(value);
        }

        public Component AddComponent(Type componentType)
        {
            return ActorBehaviour.GameObject.AddComponent(componentType);
        }

        public T AddComponent<T>() where T : Component
        {
            return ActorBehaviour.GameObject.AddComponent<T>();
        }

        public int GetInstanceID()
        {
            return ActorBehaviour.GameObject.GetInstanceID();
        }

        public T GetComponent<T>() where T : Component
        {
            return ActorBehaviour.GameObject.GetComponent<T>();
        }

        public Component GetComponent(Type type)
        {
            return ActorBehaviour.GameObject.GetComponent(type);
        }

        public Component GetComponent(string type)
        {
            return ActorBehaviour.GameObject.GetComponent(type);
        }

        public Component GetComponentInChildren(Type t)
        {
            return ActorBehaviour.GameObject.GetComponentInChildren(t);
        }

        public T GetComponentInChildren<T>() where T : Component
        {
            return ActorBehaviour.GameObject.GetComponentInChildren<T>();
        }

        public Component GetComponentInParent(Type t)
        {
            return ActorBehaviour.GameObject.GetComponentInParent(t);
        }

        public T GetComponentInParent<T>() where T : Component
        {
            return ActorBehaviour.GameObject.GetComponentInParent<T>();
        }

        public Component[] GetComponents(Type type)
        {
            return ActorBehaviour.GameObject.GetComponents(type);
        }

        public T[] GetComponents<T>() where T : Component
        {
            return ActorBehaviour.GameObject.GetComponents<T>();
        }

        public Component[] GetComponentsInChildren(Type t, bool includeInactive)
        {
            return ActorBehaviour.GameObject.GetComponentsInChildren(t, includeInactive);
        }

        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        {
            return ActorBehaviour.GameObject.GetComponentsInChildren<T>(includeInactive);
        }

        public Component[] GetComponentsInParent(Type t, bool includeInactive = false)
        {
            return ActorBehaviour.GameObject.GetComponentsInParent(t, includeInactive);
        }

        public T[] GetComponentsInParent<T>(bool includeInactive = false) where T : Component
        {
            return ActorBehaviour.GameObject.GetComponentsInParent<T>(includeInactive);
        }

        public bool Enabled
        {
            get => ActorBehaviour.enabled;
            set => ActorBehaviour.enabled = value;
        }

        public bool IsActiveAndEnabled => ActorBehaviour.isActiveAndEnabled;

        public void CancelInvoke()
        {
            if (CanExecuteCommandToBody())
                ActorBehaviour.CancelInvoke();
        }

        public void CancelInvoke(string methodName)
        {
            if (CanExecuteCommandToBody())
                ActorBehaviour.CancelInvoke(methodName);
        }

        public void Invoke(string methodName, float time)
        {
            if (CanExecuteCommandToBody())
                ActorBehaviour.Invoke(methodName, time);
        }

        public void InvokeRepeating(string methodName, float time, float repeatRate)
        {
            if (CanExecuteCommandToBody())
                ActorBehaviour.InvokeRepeating(methodName, time, repeatRate);
        }

        public bool IsInvoking(string methodName)
        {
            return ActorBehaviour.IsInvoking(methodName);
        }

        public Coroutine StartCoroutine(IEnumerator routine)
        {
            return ActorBehaviour.StartCoroutine(routine);
        }

        public Coroutine StartCoroutine(string methodName, object value = null)
        {
            return ActorBehaviour.StartCoroutine(methodName, value);
        }

        public void StopAllCoroutines()
        {
            if (CanExecuteCommandToBody())
                ActorBehaviour.StopAllCoroutines();
        }

        public void StopCoroutine(string methodName)
        {
            if (CanExecuteCommandToBody())
                ActorBehaviour.StopCoroutine(methodName);
        }

        public void StopCoroutine(IEnumerator routine)
        {
            if (CanExecuteCommandToBody())
                ActorBehaviour.StopCoroutine(routine);
        }

        public void StopCoroutine(Coroutine routine)
        {
            if (CanExecuteCommandToBody())
                ActorBehaviour.StopCoroutine(routine);
        }

        public int ChildCount => ActorBehaviour.Transform.childCount;

        public Vector3 EulerAngles
        {
            get => ActorBehaviour.Transform.eulerAngles;
            set => ActorBehaviour.Transform.eulerAngles = value;
        }

        public Vector3 Forward
        {
            get => ActorBehaviour.Transform.forward;
            set => ActorBehaviour.Transform.forward = value;
        }

        public bool HasChanged
        {
            get => ActorBehaviour.Transform.hasChanged;
            set => ActorBehaviour.Transform.hasChanged = value;
        }

        public int HierarchyCapacity => ActorBehaviour.Transform.hierarchyCount;

        public int HierarchyCount => ActorBehaviour.Transform.hierarchyCount;

        public Vector3 LocalEulerAngles
        {
            get => ActorBehaviour.Transform.localEulerAngles;
            set => ActorBehaviour.Transform.localEulerAngles = value;
        }

        public Vector3 LocalPosition
        {
            get => ActorBehaviour.Transform.localPosition;
            set => ActorBehaviour.Transform.localPosition = value;
        }

        public Quaternion LocalRotation
        {
            get => ActorBehaviour.Transform.localRotation;
            set => ActorBehaviour.Transform.localRotation = value;
        }

        public Vector3 LocalScale
        {
            get => ActorBehaviour.Transform.localScale;
            set => ActorBehaviour.Transform.localScale = value;
        }

        public Matrix4x4 LocalToWorldMatrix => ActorBehaviour.Transform.localToWorldMatrix;
        public Vector3 LossyScale => ActorBehaviour.Transform.lossyScale;

        public Transform Parent
        {
            get => ActorBehaviour.Transform.parent;
            set => ActorBehaviour.Transform.parent = value;
        }

        public Vector3 Position
        {
            get => ActorBehaviour.Transform.position;
            set => ActorBehaviour.Transform.position = value;
        }

        public Vector3 Right
        {
            get => ActorBehaviour.Transform.right;
            set => ActorBehaviour.Transform.right = value;
        }

        public Transform Root => ActorBehaviour.Transform.root;

        public Quaternion Rotation
        {
            get => ActorBehaviour.Transform.rotation;
            set => ActorBehaviour.Transform.rotation = value;
        }

        public Vector3 Up
        {
            get => ActorBehaviour.Transform.up;
            set => ActorBehaviour.Transform.up = value;
        }

        public Matrix4x4 WorldToLocalMatrix => ActorBehaviour.Transform.worldToLocalMatrix;

        public void DetachChildren()
        {
            if (CanExecuteCommandToBody())
                ActorBehaviour.Transform.DetachChildren();
        }

        public Transform Find(string name)
        {
            return ActorBehaviour.Transform.Find(name);
        }

        public Transform GetChild(int index)
        {
            return ActorBehaviour.Transform.GetChild(index);
        }

        public int GetSiblingIndex()
        {
            return ActorBehaviour.Transform.GetSiblingIndex();
        }

        public Vector3 InverseTransformDirection(Vector3 direction)
        {
            return ActorBehaviour.Transform.InverseTransformDirection(direction);
        }

        public Vector3 InverseTransformPoint(Vector3 position)
        {
            return ActorBehaviour.Transform.InverseTransformDirection(position);
        }

        public Vector3 InverseTransformVector(Vector3 vector)
        {
            return ActorBehaviour.Transform.InverseTransformDirection(vector);
        }

        public bool IsChildOf(Transform parent)
        {
            return ActorBehaviour.Transform.IsChildOf(parent);
        }

        public void LookAt(Transform target)
        {
            if (CanExecuteCommandToBody())
                ActorBehaviour.Transform.LookAt(target);
        }

        public void LookAt(Transform target, Vector3 worldUp)
        {
            if (CanExecuteCommandToBody())
                ActorBehaviour.Transform.LookAt(target, worldUp);
        }

        public void Rotate(Vector3 eulers, Space relativeTo = Space.Self)
        {
            if (CanExecuteCommandToBody())
                ActorBehaviour.Transform.Rotate(eulers, relativeTo);
        }

        public void Rotate(float xAngle, float yAngle, float zAngle, Space relativeTo = Space.Self)
        {
            if (CanExecuteCommandToBody())
                ActorBehaviour.Transform.Rotate(xAngle, yAngle, zAngle, relativeTo);
        }

        public void Rotate(Vector3 axis, float angle, Space relativeTo = Space.Self)
        {
            if (CanExecuteCommandToBody())
                ActorBehaviour.Transform.Rotate(axis, angle, relativeTo);
        }

        public void RotateAround(Vector3 point, Vector3 axis, float angle)
        {
            if (CanExecuteCommandToBody())
                ActorBehaviour.Transform.RotateAround(point, axis, angle);
        }

        public void SetAsFirstSibling()
        {
            if (CanExecuteCommandToBody())
                ActorBehaviour.Transform.SetAsFirstSibling();
        }

        public void SetAsLastSibling()
        {
            if (CanExecuteCommandToBody())
                ActorBehaviour.Transform.SetAsLastSibling();
        }

        public void SetParent(Transform parent)
        {
            if (CanExecuteCommandToBody())
                ActorBehaviour.Transform.SetParent(parent);
        }

        public void SetParent(Transform parent, bool worldPositionStays)
        {
            if (CanExecuteCommandToBody())
                ActorBehaviour.Transform.SetParent(parent, worldPositionStays);
        }

        public void SetPositionAndRotation(Vector3 position, Quaternion rotation)
        {
            if (CanExecuteCommandToBody())
                ActorBehaviour.Transform.SetPositionAndRotation(position, rotation);
        }

        public void SetSiblingIndex(int index)
        {
            if (CanExecuteCommandToBody())
                ActorBehaviour.Transform.SetSiblingIndex(index);
        }

        public Vector3 TransformDirection(Vector3 direction)
        {
            return ActorBehaviour.Transform.TransformDirection(direction);
        }

        public Vector3 TransformPoint(Vector3 position)
        {
            return ActorBehaviour.Transform.TransformDirection(position);
        }

        public Vector3 TransformVector(Vector3 vector)
        { 
            return ActorBehaviour.Transform.TransformDirection(vector);
        }

        public void Translate(Vector3 translation)
        {
            if(CanExecuteCommandToBody())
                ActorBehaviour.Transform.Translate(translation);
        }

        public void Translate(Vector3 translation, Space relativeTo = Space.Self)
        {
            if(CanExecuteCommandToBody())
                ActorBehaviour.Transform.Translate(translation, relativeTo);
        }

        public Transform Transform => ActorBehaviour.Transform;
        public RectTransform RectTransform => ActorBehaviour.RectTransform;
        public GameObject GameObject => ActorBehaviour.GameObject;

        public Vector2 AnchoredPosition
        {
            get => RectTransform.anchoredPosition;
            set => RectTransform.anchoredPosition = value;
        }

        public Vector3 AnchoredPosition3D
        {
            get => RectTransform.anchoredPosition3D;
            set => RectTransform.anchoredPosition3D = value;
        }

        public Vector2 AnchorMax
        {
            get => RectTransform.anchorMax;
            set => RectTransform.anchorMax = value;
        }

        public Vector2 AnchorMin
        {
            get => RectTransform.anchorMin;
            set => RectTransform.anchorMin = value;
        }

        public Vector2 OffsetMax
        {
            get => RectTransform.offsetMax;
            set => RectTransform.offsetMin = value;
        }

        public Vector2 OffsetMin
        {
            get => RectTransform.offsetMin;
            set => RectTransform.offsetMin = value;
        }

        public Vector2 Pivot
        {
            get => RectTransform.pivot;
            set => RectTransform.pivot = value;
        }

        public Rect Rect => RectTransform.rect;

        public Vector2 SizeDelta
        {
            get => RectTransform.sizeDelta;
            set => RectTransform.sizeDelta = value;
        }

        public void ForceUpdateRectTransforms()
        {
            if (RectTransform != null)
            {
                RectTransform.ForceUpdateRectTransforms();
            }
        }

        public void GetLocalCorners(Vector3[] fourCornersArray)
        {
            if (RectTransform != null)
            {
                RectTransform.GetLocalCorners(fourCornersArray);
            }
        }

        public void GetWorldCorners(Vector3[] fourCornersArray)
        {
            if (RectTransform != null)
            {
                RectTransform.GetWorldCorners(fourCornersArray);
            }
        }

        public void SetInsetAndSizeFromParentEdge(RectTransform.Edge edge, float inset, float size)
        {
            if (RectTransform != null)
            {
                RectTransform.SetInsetAndSizeFromParentEdge(edge, inset, size);
            }
        }

        public void SetSizeWithCurrentAnchors(RectTransform.Axis axis, float size)
        {
            if (RectTransform != null)
            {
                RectTransform.SetSizeWithCurrentAnchors(axis, size);
            }
        }
        
        #endregion
    }

    [Serializable]
    public class Actor<TBehaviour> : Actor, IActorTyped<TBehaviour>
        where TBehaviour : ActorMonoBehaviour
    {
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
#endif
        public TBehaviour BodyBehaviour { get; private set; }

        public Actor() : base()
        {
            BodyBehaviour = ActorBehaviour as TBehaviour;
        }
        
        public Actor(bool autoCreateGameObject) : base(autoCreateGameObject)
        {
            BodyBehaviour = ActorBehaviour as TBehaviour;
        }

        public Actor(GameObject prefab = null, string name = "") : base(prefab, name)
        {
            BodyBehaviour = ActorBehaviour as TBehaviour;
        }

        public override bool Possess(ActorMonoBehaviour target)
        {
            bool result = base.Possess(target);
            BodyBehaviour = ActorBehaviour as TBehaviour;
            return result;
        }

        public override void Eject()
        {
            base.Eject();
            BodyBehaviour = null;
        }

        protected override ActorMonoBehaviour AddOrGetActorBehaviour(GameObject gameObject)
        {
            return Actor.AddOrGetActorComponent<TBehaviour>(gameObject);
        }
    }
}