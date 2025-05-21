using System;
using UnityEngine;

namespace LegendaryTools.Actor
{
    public interface IActor : IMonoBehaviour, IRectTransform, IGameObject, IDisposable
    {
        Transform Transform { get; }
        RectTransform RectTransform { get; }
        GameObject GameObject { get; }
        ActorMonoBehaviour ActorBehaviour { get; }
        GameObject Prefab { get; }
        bool IsDestroyed { get; }
        bool IsAlive { get; }
        bool HasBody { get; }
        event Action<Actor, ActorMonoBehaviour> OnAsyncActorBodyLoaded;
        event Action<Actor, ActorMonoBehaviour> OnPossession;
        event Action<Actor, ActorMonoBehaviour> OnEjected;
        event Action<Actor, ActorMonoBehaviour> OnDestroyed;
        bool Possess(ActorMonoBehaviour target);
        void Eject();
        void RegenerateBody(string name = "");
    }

    public interface IActorTyped<TBehaviour> : IActor
        where TBehaviour : ActorMonoBehaviour
    {
        TBehaviour BodyBehaviour { get; }
    }
}