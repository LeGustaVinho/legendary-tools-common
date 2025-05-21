using System;
using UnityEngine;

namespace LegendaryTools.Actor
{
    public class ActorMonoBehaviour : UnityBehaviour, IActorMonoBehaviour
    {
#if ODIN_INSPECTOR 
        [Sirenix.OdinInspector.ShowInInspector]
        [Sirenix.OdinInspector.HideInEditorMode]
#endif
        public Actor Actor { get; protected set; }

        public bool HasSoul => Actor != null;
        
#if ODIN_INSPECTOR 
        [Sirenix.OdinInspector.ShowIn(Sirenix.OdinInspector.PrefabKind.InstanceInScene)] 
#endif
        public bool AutoCreateActor;
        
#if ODIN_INSPECTOR 
        [Sirenix.OdinInspector.ShowIn(Sirenix.OdinInspector.PrefabKind.InstanceInScene)] 
#endif
        [TypeFilter(typeof(Actor))]
        public SerializableType ActorType;

        [SerializeField] private Transform _transform;
        [SerializeField] private RectTransform _rectTransform;
        [SerializeField] private GameObject _gameObject;
        
        public Transform Transform
        {
            get => _transform;
            private set => _transform = value;
        }
        public RectTransform RectTransform
        {
            get => _rectTransform;
            private set => _rectTransform = value;
        }
        public GameObject GameObject
        {
            get => _gameObject;
            private set => _gameObject = value;
        }

        public event Action<ActorMonoBehaviour, Actor> OnActorBinded;
        public event Action<ActorMonoBehaviour, Actor> OnActorUnbinded;
        
        #region MonoBehaviour Events

        public event Action WhenAwake;
        public event Action WhenStart;
        public event Action WhenUpdate;
        public event Action WhenDestroy;
        public event Action WhenEnable;
        public event Action WhenDisable;
        public event Action<Collider> WhenTriggerEnter;
        public event Action<Collider> WhenTriggerExit;
        public event Action<Collision> WhenCollisionEnter;
        public event Action<Collision> WhenCollisionExit;
        public event Action WhenLateUpdate;
        public event Action WhenFixedUpdate;
        public event Action<Collision> WhenCollisionStay;
        public event Action<bool> WhenApplicationFocus;
        public event Action<bool> WhenApplicationPause;
        public event Action WhenApplicationQuit;
        public event Action WhenBecameVisible;
        public event Action WhenBecameInvisible;
        public event Action<Collision2D> WhenCollisionEnter2D;
        public event Action<Collision2D> WhenCollisionStay2D;
        public event Action<Collision2D> WhenCollisionExit2D;
        public event Action WhenDrawGizmos;
        public event Action WhenDrawGizmosSelected;
        public event Action WhenGUI;
        public event Action WhenPreCull;
        public event Action WhenPreRender;
        public event Action WhenPostRender;
        public event Action<RenderTexture, RenderTexture> WhenRenderImage;
        public event Action WhenRenderObject;
        public event Action WhenTransformChildrenChanged;
        public event Action WhenTransformParentChanged;
        public event Action<Collider2D> WhenTriggerEnter2D;
        public event Action<Collider2D> WhenTriggerStay2D;
        public event Action<Collider2D> WhenTriggerExit2D;
        public event Action WhenValidate;
        public event Action WhenWillRenderObject;
        
        #endregion

        public void BindActor(Actor actor)
        {
            Actor = actor;
            FillComponents();
            OnActorBind(this, actor);
            OnActorBinded?.Invoke(this, actor);
        }

        public void UnBindActor()
        {
            Actor aux = Actor;
            Actor = null;
            OnActorUnBind(this, aux);
            OnActorUnbinded?.Invoke(this, aux);
        }

#if ODIN_INSPECTOR 
        [Sirenix.OdinInspector.ShowInInspector]
        [Sirenix.OdinInspector.HideInEditorMode]
#endif
        public void Suicide()
        {
            Destroy(gameObject); 
        }

        protected virtual void OnActorBind(ActorMonoBehaviour behaviour, Actor actor)
        {
            
        }
        
        protected virtual void OnActorUnBind(ActorMonoBehaviour behaviour, Actor actor)
        {
            
        }
        
        protected virtual void FillComponents()
        {
            _transform = GetComponent<Transform>();
            _rectTransform = GetComponent<RectTransform>();
            _gameObject = gameObject;
        }
        
        #region MonoBehaviour

        protected virtual void Awake()
        {
            FillComponents();
            if (AutoCreateActor && Actor == null)
            {
                object newObject = Activator.CreateInstance(ActorType.Type);
                if (newObject is Actor newActor)
                {
                    newActor.Possess(this);
                }
                else
                {
                    Debug.Log($"[ActorMonoBehaviour] Auto create Actor of type {ActorType.Type.FullName} failed because is not a Actor");
                }
            }
            WhenAwake?.Invoke();
        }

        protected virtual void Start()
        {
            WhenStart?.Invoke();
        }

        protected virtual void Update()
        {
            WhenUpdate?.Invoke();
        }

        protected virtual void LateUpdate()
        {
            WhenLateUpdate?.Invoke();
        }

        protected virtual void FixedUpdate()
        {
            WhenFixedUpdate?.Invoke();
        }
        
        protected virtual void OnDestroy()
        {
            WhenDestroy?.Invoke();
        }

        protected virtual void OnEnable()
        {
            WhenEnable?.Invoke();
        }

        protected virtual void OnDisable()
        {
            WhenDisable?.Invoke();
        }

        protected virtual void OnTriggerEnter(Collider other)
        {
            WhenTriggerEnter?.Invoke(other);
        }

        protected virtual void OnTriggerExit(Collider other)
        {
            WhenTriggerExit?.Invoke(other);
        }

        protected virtual void OnCollisionEnter(Collision collision)
        {
            WhenCollisionEnter?.Invoke(collision);
        }

        protected virtual void OnCollisionStay(Collision other)
        {
            WhenCollisionStay?.Invoke(other);
        }

        protected virtual void OnCollisionExit(Collision collision)
        {
            WhenCollisionExit?.Invoke(collision);
        }

        protected virtual void OnApplicationFocus(bool hasFocus)
        {
            WhenApplicationFocus?.Invoke(hasFocus);
        }

        protected virtual void OnApplicationPause(bool pauseStatus)
        {
            WhenApplicationPause?.Invoke(pauseStatus);
        }

        protected virtual void OnApplicationQuit()
        {
            WhenApplicationQuit?.Invoke();
        }

        protected virtual void OnBecameVisible()
        {
            WhenBecameVisible?.Invoke();
        }

        protected virtual void OnBecameInvisible()
        {
            WhenBecameInvisible?.Invoke();
        }

        protected virtual void OnCollisionEnter2D(Collision2D other)
        {
            WhenCollisionEnter2D?.Invoke(other);
        }

        protected virtual void OnCollisionStay2D(Collision2D other)
        {
            WhenCollisionStay2D?.Invoke(other);
        }
        
        protected virtual void OnCollisionExit2D(Collision2D other)
        {
            WhenCollisionExit2D?.Invoke(other);
        }

        protected virtual void OnDrawGizmos()
        {
            WhenDrawGizmos?.Invoke();
        }

        protected virtual void OnDrawGizmosSelected()
        {
            WhenDrawGizmosSelected?.Invoke();
        }

        protected virtual void OnGUI()
        {
            WhenGUI?.Invoke();
        }

        private void OnPreCull()
        {
            WhenPreCull?.Invoke();
        }

        protected virtual void OnPreRender()
        {
            WhenPreRender?.Invoke();
        }

        protected virtual void OnPostRender()
        {
            WhenPostRender?.Invoke();
        }

        protected virtual void OnRenderImage(RenderTexture src, RenderTexture dest)
        {
            WhenRenderImage?.Invoke(src, dest);
        }

        protected virtual void OnRenderObject()
        {
            WhenRenderObject?.Invoke();
        }

        protected virtual void OnTransformChildrenChanged()
        {
            WhenTransformChildrenChanged?.Invoke();
        }

        protected virtual void OnTransformParentChanged()
        {
            WhenTransformParentChanged?.Invoke();
        }

        protected virtual void OnTriggerEnter2D(Collider2D other)
        {
            WhenTriggerEnter2D?.Invoke(other);
        }

        protected virtual void OnTriggerStay2D(Collider2D other)
        {
            WhenTriggerStay2D?.Invoke(other);
        }

        protected virtual void OnTriggerExit2D(Collider2D other)
        {
            WhenTriggerExit2D?.Invoke(other);
        }

        protected virtual void OnValidate()
        {
            FillComponents();

            if (!gameObject.IsInScene() && AutoCreateActor)
            {
                AutoCreateActor = false;
                Debug.LogError("[ActorMonoBehaviour] You cant allow AutoCreateActor for Prefabs, only for gamObjects in scene !", this);
                this.SetDirty();
            }
            
            WhenValidate?.Invoke();
        }

        protected virtual void OnWillRenderObject()
        {
            WhenWillRenderObject?.Invoke();
        }

        protected virtual void Reset()
        {
            FillComponents();
        }

        #endregion
    }
}