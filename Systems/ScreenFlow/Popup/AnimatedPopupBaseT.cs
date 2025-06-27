using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
#if UNITY_TIMELINE
using UnityEngine.Timeline;
#endif
#if DOTWEEN_PRO
using DG.Tweening;
using DG.Tweening.Core;
#endif

namespace LegendaryTools.Systems.ScreenFlow
{
    public abstract class AnimatedPopupBaseT<T, TDataShow, TDataHide> : PopupBaseT<T, TDataShow, TDataHide>
        where T : class
        where TDataShow : class
        where TDataHide : class
    {
        [SerializeField]protected AnimationSystemConfig animationSystemConfig;

        public UnityEvent OnShow;
        public UnityEvent OnHide;
        public UnityEvent OnGoingToBackground;
        
        public override async Task ShowT(TDataShow args)
        {
            switch (animationSystemConfig.AnimationSystem)
            {
                case AnimationSystem.Animation:
                    await PlayAnimation(animationSystemConfig.ShowAnimation);
                    break;
                case AnimationSystem.Animator:
                    await PlayAnimatorTrigger(animationSystemConfig.Animator, animationSystemConfig.ShowTriggerName);
                    break;
#if DOTWEEN_PRO
                case AnimationSystem.DOTweenPro:
                    await PlayDOTweenAnimation(animationSystemConfig.DoTweenShowAnimation);
                    break;
#endif
#if UNITY_TIMELINE
                case AnimationSystem.UnityTimeline:
                    await PlayTimeline(animationSystemConfig.TimelineDirector, animationSystemConfig.ShowTimeline);
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
            OnShow.Invoke();
        }

        public override async Task HideT(TDataHide args)
        {
            switch (animationSystemConfig.AnimationSystem)
            {
                case AnimationSystem.Animation:
                    await PlayAnimation(animationSystemConfig.HideAnimation);
                    break;
                case AnimationSystem.Animator:
                    await PlayAnimatorTrigger(animationSystemConfig.Animator, animationSystemConfig.HideTriggerName);
                    break;
#if DOTWEEN_PRO
                case AnimationSystem.DOTweenPro:
                    await PlayDOTweenAnimation(animationSystemConfig.DoTweenHideAnimation);
                    break;
#endif
#if UNITY_TIMELINE
                case AnimationSystem.UnityTimeline:
                    await PlayTimeline(animationSystemConfig.TimelineDirector, animationSystemConfig.HideTimeline);
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
            OnHide.Invoke();
        }

        public override void OnGoToBackgroundT(TDataHide args)
        {
            // Optionally play a background animation or handle specific background behavior
            OnGoToBackgroundCustom(args);
            OnGoingToBackground.Invoke();
        }

        private async Task PlayAnimation(Animation animation)
        {
            if (animation == null || animation.clip == null)
            {
                Debug.LogWarning($"[{nameof(AnimatedPopupBaseT<T, TDataShow, TDataHide>)}:PlayAnimation] Animation or clip is null.", this);
                return;
            }

            animation.Play();
            await Task.Delay((int)(animation.clip.length * 1000));
        }

        private async Task PlayAnimatorTrigger(Animator animator, string triggerName)
        {
            if (animator == null || string.IsNullOrEmpty(triggerName))
            {
                Debug.LogWarning($"[{nameof(AnimatedPopupBaseT<T, TDataShow, TDataHide>)}:PlayAnimatorTrigger] Animator or trigger name is null.", this);
                return;
            }

            animator.SetTrigger(triggerName);
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
            await Task.Delay((int)(stateInfo.length * 1000));
        }

#if DOTWEEN_PRO
        private async Task PlayDOTweenAnimation(DOTweenAnimation doTweenAnimation)
        {
            if (doTweenAnimation == null)
            {
                Debug.LogWarning($"[{nameof(AnimatedPopupBaseT<T, TDataShow, TDataHide>)}:PlayDOTweenAnimation] DOTweenAnimation is null.", this);
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
                Debug.LogWarning($"[{nameof(AnimatedPopupBaseT<T, TDataShow, TDataHide>)}:PlayTimeline] PlayableDirector or TimelineAsset is null.", this);
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
            await Task.Yield();
        }

        protected virtual async Task CustomHideAnimation(TDataHide args)
        {
            // Override this method for custom hide animations
            await Task.Yield();
        }

        protected virtual void OnGoToBackgroundCustom(TDataHide args)
        {
            // Override this method for custom background behavior
        }

        protected abstract Task OnShowT(TDataShow args);
        protected abstract Task OnHideT(TDataHide args);

        protected virtual void Awake()
        {
            animationSystemConfig.ShowAnimation = animationSystemConfig.ShowAnimation ?? GetComponent<Animation>();
            animationSystemConfig.Animator = animationSystemConfig.Animator ?? GetComponent<Animator>();
#if DOTWEEN_PRO
            animationSystemConfig.DoTweenShowAnimation = animationSystemConfig.DoTweenShowAnimation ?? GetComponent<DOTweenAnimation>();
            animationSystemConfig.DoTweenHideAnimation = animationSystemConfig.DoTweenHideAnimation ?? GetComponent<DOTweenAnimation>();
#endif
#if UNITY_TIMELINE
            animationSystemConfig.TimelineDirector = animationSystemConfig.TimelineDirector ?? GetComponent<PlayableDirector>();
#endif
        }
    }
}