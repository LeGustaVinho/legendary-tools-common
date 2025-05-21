using System;
using UnityEngine;

namespace LegendaryTools.Actor
{
    public interface IActorMonoBehaviour
    {
        event Action<ActorMonoBehaviour, Actor> OnActorBinded;
        event Action<ActorMonoBehaviour, Actor> OnActorUnbinded;
        event Action WhenAwake;
        event Action WhenStart;
        event Action WhenUpdate;
        event Action WhenDestroy;
        event Action WhenEnable;
        event Action WhenDisable;
        event Action<Collider> WhenTriggerEnter;
        event Action<Collider> WhenTriggerExit;
        event Action<Collision> WhenCollisionEnter;
        event Action<Collision> WhenCollisionExit;
        event Action WhenLateUpdate;
        event Action WhenFixedUpdate;
        event Action<Collision> WhenCollisionStay;
        event Action<bool> WhenApplicationFocus;
        event Action<bool> WhenApplicationPause;
        event Action WhenApplicationQuit;
        event Action WhenBecameVisible;
        event Action WhenBecameInvisible;
        event Action<Collision2D> WhenCollisionEnter2D;
        event Action<Collision2D> WhenCollisionStay2D;
        event Action<Collision2D> WhenCollisionExit2D;
        event Action WhenDrawGizmos;
        event Action WhenDrawGizmosSelected;
        event Action WhenGUI;
        event Action WhenPreCull;
        event Action WhenPreRender;
        event Action WhenPostRender;
        event Action<RenderTexture, RenderTexture> WhenRenderImage;
        event Action WhenRenderObject;
        event Action WhenTransformChildrenChanged;
        event Action WhenTransformParentChanged;
        event Action<Collider2D> WhenTriggerEnter2D;
        event Action<Collider2D> WhenTriggerStay2D;
        event Action<Collider2D> WhenTriggerExit2D;
        event Action WhenValidate;
        event Action WhenWillRenderObject;
        void BindActor(Actor actor);
        void UnBindActor();
        void Suicide();
    }
}