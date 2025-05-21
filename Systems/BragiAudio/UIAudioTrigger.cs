using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LegendaryTools.Bragi
{
    public enum AudioTriggerType
    {
        PointerClick,
        PointerEnter,
        PointerExit,
        PointerUp,
        PointDown,

        Submit,
        Select,

        BeginDrag,
        Drag,
        Drop,
        EndDrag,

        TriggerEnter,
        TriggerStay,
        TriggerExit,
        
        Start,
        OnDestroy,
        OnEnable,
        OnDisable,

        Custom
    }

    public enum AudioTriggerPlayMode
    {
        Default,
        PlayAtThisLocation,
        PlayAndParent
    }

    [Serializable]
    public struct AudioConfigTrigger
    {
        public AudioTriggerType TriggerType;
        public AudioTriggerPlayMode PlayMode;
        public AudioConfigBase Config;
        public string Custom;
    }

    public class UIAudioTrigger : MonoBehaviour,
        IPointerClickHandler, IPointerEnterHandler, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler,
        IBeginDragHandler, IDragHandler, IDropHandler, IEndDragHandler,
        ISelectHandler, ISubmitHandler
    {
        public AudioConfigTrigger[] AudioConfigTriggers;
        public bool PreventParallelAudios = true;

        private readonly Dictionary<AudioTriggerType, List<AudioConfigTrigger>> audioConfigTriggerTable =
            new Dictionary<AudioTriggerType, List<AudioConfigTrigger>>();

        private readonly Dictionary<string, AudioConfigTrigger> customAudioConfigTriggerTable =
            new Dictionary<string, AudioConfigTrigger>();

        private readonly List<AudioHandler> currentHandlers = new List<AudioHandler>();

        public void OnPointerClick(PointerEventData eventData)
        {
            ProcessTrigger(AudioTriggerType.PointerClick);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            ProcessTrigger(AudioTriggerType.PointerEnter);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            ProcessTrigger(AudioTriggerType.PointDown);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            ProcessTrigger(AudioTriggerType.PointerUp);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            ProcessTrigger(AudioTriggerType.PointerExit);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            ProcessTrigger(AudioTriggerType.BeginDrag);
        }

        public void OnDrag(PointerEventData eventData)
        {
            ProcessTrigger(AudioTriggerType.Drag);
        }

        public void OnDrop(PointerEventData eventData)
        {
            ProcessTrigger(AudioTriggerType.Drop);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            ProcessTrigger(AudioTriggerType.EndDrag);
        }

        public void OnSelect(BaseEventData eventData)
        {
            ProcessTrigger(AudioTriggerType.Select);
        }

        public void OnSubmit(BaseEventData eventData)
        {
            ProcessTrigger(AudioTriggerType.Submit);
        }

        public virtual void OnTriggerEnter(Collider collider)
        {
            ProcessTrigger(AudioTriggerType.TriggerEnter);
        }

        public virtual void OnTriggerStay(Collider collider)
        {
            ProcessTrigger(AudioTriggerType.TriggerStay);
        }

        public virtual void OnTriggerExit(Collider collider)
        {
            ProcessTrigger(AudioTriggerType.TriggerExit);
        }

        public virtual void CustomTrigger(string triggerName)
        {
            ProcessTrigger(AudioTriggerType.Custom, triggerName);
        }

        protected virtual void Awake()
        {
            Initialize();
        }

        protected virtual void Start()
        {
            ProcessTrigger(AudioTriggerType.Start);
        }
        protected virtual void OnDestroy()
        {
            ProcessTrigger(AudioTriggerType.OnDestroy);
        }
        
        protected virtual void OnEnable()
        {
            ProcessTrigger(AudioTriggerType.OnEnable);
        }
        
        protected virtual void OnDisable()
        {
            ProcessTrigger(AudioTriggerType.OnDisable);
        }

        protected virtual void Initialize()
        {
            audioConfigTriggerTable.Clear();
            customAudioConfigTriggerTable.Clear();
            
            foreach (AudioConfigTrigger audioConfigTrigger in AudioConfigTriggers)
            {
                if (!audioConfigTriggerTable.ContainsKey(audioConfigTrigger.TriggerType))
                {
                    audioConfigTriggerTable.Add(audioConfigTrigger.TriggerType, new List<AudioConfigTrigger>());
                }

                audioConfigTriggerTable[audioConfigTrigger.TriggerType].Add(audioConfigTrigger);

                if (audioConfigTrigger.TriggerType == AudioTriggerType.Custom)
                {
                    if (!customAudioConfigTriggerTable.ContainsKey(audioConfigTrigger.Custom))
                    {
                        customAudioConfigTriggerTable.Add(audioConfigTrigger.Custom, audioConfigTrigger);
                    }
                }
            }
        }

        protected virtual void ProcessTrigger(AudioTriggerType triggerType, string customString = "")
        {
            if (triggerType == AudioTriggerType.Custom && !string.IsNullOrEmpty(customString))
            {
                if (customAudioConfigTriggerTable.TryGetValue(customString, out AudioConfigTrigger audioConfigTrigger))
                {
                    Play(audioConfigTrigger);
                }
            }
            else
            {
                if (audioConfigTriggerTable.TryGetValue(triggerType, out List<AudioConfigTrigger> audioConfigTriggers))
                {
                    foreach (AudioConfigTrigger audioConfigTrigger in audioConfigTriggers)
                    {
                        Play(audioConfigTrigger);
                    }
                }
            }
        }

        protected void Play(AudioConfigTrigger audioConfigTrigger)
        {
            if (PreventParallelAudios)
            {
                for (int index = currentHandlers.Count - 1; index >= 0; index--)
                {
                    AudioHandler handler = currentHandlers[index];
                    if (handler.IsPlaying)
                    {
                        handler.StopNow();
                    }
                }
            }
            
            switch (audioConfigTrigger.PlayMode)
            {
                case AudioTriggerPlayMode.Default:
                    currentHandlers.AddRange(audioConfigTrigger.Config.Play());
                    break;
                case AudioTriggerPlayMode.PlayAtThisLocation:
                    currentHandlers.AddRange(audioConfigTrigger.Config.Play(transform.position));
                    break;
                case AudioTriggerPlayMode.PlayAndParent:
                    currentHandlers.AddRange(audioConfigTrigger.Config.Play(transform));
                    break;
            }

            foreach (AudioHandler handler in currentHandlers)
            {
                handler.OnDispose += OnHandlerDisposed;
            }
        }

        private void OnHandlerDisposed(AudioHandler handler)
        {
            handler.OnDispose -= OnHandlerDisposed;
            currentHandlers.Remove(handler);
        }
    }
}