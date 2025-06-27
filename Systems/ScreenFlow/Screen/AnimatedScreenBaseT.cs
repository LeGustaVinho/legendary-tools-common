using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Playables;
#if UNITY_TIMELINE
using UnityEngine.Timeline;
#endif
#if DOTWEEN_PRO || DOTWEEN
using DG.Tweening;
using DG.Tweening.Core;
#endif

namespace LegendaryTools.Systems.ScreenFlow
{
    public enum AnimationSystem
    {
        None,
        Animation,
        Animator,
        DOTweenPro,
        UnityTimeline,
        Custom
    }

    public abstract class AnimatedScreenBaseT<T, TDataShow, TDataHide> : ScreenBaseT<T, TDataShow, TDataHide>
        where T : class
        where TDataShow : class
        where TDataHide : class
    {
        [SerializeField] private AnimationSystem animationSystem = AnimationSystem.None;

        [SerializeField] private Animation showAnimation;
        [SerializeField] private Animation hideAnimation;

        [SerializeField] private Animator animator;
        [SerializeField] private string showTriggerName = "Show";
        [SerializeField] private string hideTriggerName = "Hide";

#if DOTWEEN_PRO || DOTWEEN
        [SerializeField] private DOTweenAnimation doTweenShowAnimation;
        [SerializeField] private DOTweenAnimation doTweenHideAnimation;
#endif

#if UNITY_TIMELINE
        [SerializeField] private PlayableDirector timelineDirector;
        [SerializeField] private TimelineAsset showTimeline;
        [SerializeField] private TimelineAsset hideTimeline;
#endif

        [SerializeField] private float customShowDuration = 1f;
        [SerializeField] private float customHideDuration = 1f;

        public AnimationSystem AnimationSystem
        {
            get => animationSystem;
            set => animationSystem = value;
        }

        public override async Task ShowT(TDataShow args)
        {
            switch (animationSystem)
            {
                case AnimationSystem.Animation:
                    await PlayAnimation(showAnimation);
                    break;
                case AnimationSystem.Animator:
                    await PlayAnimatorTrigger(animator, showTriggerName);
                    break;
#if DOTWEEN_PRO || DOTWEEN
                case AnimationSystem.DOTweenPro:
                    await PlayDOTweenAnimation(doTweenShowAnimation);
                    break;
#endif
#if UNITY_TIMELINE
                case AnimationSystem.UnityTimeline:
                    await PlayTimeline(timelineDirector, showTimeline);
                    break;
#endif
                case AnimationSystem.Custom:
                    await CustomShowAnimation(args);
                    break;
                case AnimationSystem.None:
                default:
                    break;
            }

            await OnShowT(args);
        }

        public override async Task HideT(TDataHide args)
        {
            switch (animationSystem)
            {
                case AnimationSystem.Animation:
                    await PlayAnimation(hideAnimation);
                    break;
                case AnimationSystem.Animator:
                    await PlayAnimatorTrigger(animator, hideTriggerName);
                    break;
#if DOTWEEN_PRO || DOTWEEN
                case AnimationSystem.DOTweenPro:
                    await PlayDOTweenAnimation(doTweenHideAnimation);
                    break;
#endif
#if UNITY_TIMELINE
                case AnimationSystem.UnityTimeline:
                    await PlayTimeline(timelineDirector, hideTimeline);
                    break;
#endif
                case AnimationSystem.Custom:
                    await CustomHideAnimation(args);
                    break;
                case AnimationSystem.None:
                default:
                    break;
            }

            await OnHideT(args);
        }

        private async Task PlayAnimation(Animation animation)
        {
            if (animation == null || animation.clip == null)
            {
                Debug.LogWarning($"[{nameof(AnimatedScreenBaseT<T, TDataShow, TDataHide>)}:PlayAnimation] Animation or clip is null.", this);
                return;
            }

            animation.Play();
            await Task.Delay((int)(animation.clip.length * 1000));
        }

        private async Task PlayAnimatorTrigger(Animator animator, string triggerName)
        {
            if (animator == null || string.IsNullOrEmpty(triggerName))
            {
                Debug.LogWarning($"[{nameof(AnimatedScreenBaseT<T, TDataShow, TDataHide>)}:PlayAnimatorTrigger] Animator or trigger name is null.", this);
                return;
            }

            animator.SetTrigger(triggerName);
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
            await Task.Delay((int)(stateInfo.length * 1000));
        }

#if DOTWEEN_PRO || DOTWEEN
        private async Task PlayDOTweenAnimation(DOTweenAnimation doTweenAnimation)
        {
            if (doTweenAnimation == null)
            {
                Debug.LogWarning($"[{nameof(AnimatedScreenBaseT<T, TDataShow, TDataHide>)}:PlayDOTweenAnimation] DOTweenAnimation is null.", this);
                return;
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            doTweenAnimation.DOPlay();
            doTweenAnimation.onComplete.AddListener(() => tcs.SetResult(true));
            await tcs.Task;
        }
#endif

#if UNITY_TIMELINE
        private async Task PlayTimeline(PlayableDirector director, TimelineAsset timeline)
        {
            if (director == null || timeline == null)
            {
                Debug.LogWarning($"[{nameof(AnimatedScreenBaseT<T, TDataShow, TDataHide>)}:PlayTimeline] PlayableDirector or TimelineAsset is null.", this);
                return;
            }

            director.playableAsset = timeline;
            director.Play();
            await Task.Delay((int)(timeline.duration * 1000));
        }
#endif

        protected virtual async Task CustomShowAnimation(TDataShow args)
        {
            // Override this method for custom show animations
            await Task.Delay((int)(customShowDuration * 1000));
        }

        protected virtual async Task CustomHideAnimation(TDataHide args)
        {
            // Override this method for custom hide animations
            await Task.Delay((int)(customHideDuration * 1000));
        }

        protected abstract Task OnShowT(TDataShow args);
        protected abstract Task OnHideT(TDataHide args);

        protected virtual void Awake()
        {
            showAnimation = showAnimation ?? GetComponent<Animation>();
            animator = animator ?? GetComponent<Animator>();
#if DOTWEEN_PRO || DOTWEEN
            doTweenShowAnimation = doTweenShowAnimation ?? GetComponent<DOTweenAnimation>();
            doTweenHideAnimation = doTweenHideAnimation ?? GetComponent<DOTweenAnimation>();
#endif
#if UNITY_TIMELINE
            timelineDirector = timelineDirector ?? GetComponent<PlayableDirector>();
#endif
        }
    }
}