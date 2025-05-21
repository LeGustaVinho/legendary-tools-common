using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LegendaryTools.UI
{
    [Serializable]
    public class GameObjectListing<TGameObject, TData>
        where TGameObject : Component, GameObjectListing<TGameObject, TData>.IListingItem
        where TData : IEquatable<TData>
    {
        public interface IListingItem
        {
            void Init(TData item);
            void UpdateUI(TData item);
        }
        
        protected struct PrefabSpawnInfo
        {
            public TGameObject SelectedPrefab;
            public Transform SelectedPrefabTransform;
            
            public TGameObject Instance;
            public GameObject InstanceGameObject;
            public Transform InstanceTransform;
        }
        
        public readonly Func<TData[]> DataProvider;

        public bool ForceDestroyBeforeAdd;
        public List<TGameObject> Listing = new List<TGameObject>();
        public Transform Parent;
        public TGameObject Prefab;

        private readonly Dictionary<TData, TGameObject> gameObjectTable = new Dictionary<TData, TGameObject>();
        
        public event Action<TGameObject> OnPreDestroy;

        public GameObjectListing()
        {
        }

        public GameObjectListing(TGameObject prefab, Transform parent, Func<TData[]> dataProvider) : this()
        {
            Prefab = prefab;
            Parent = parent;
            DataProvider = dataProvider;
        }
        
        public virtual List<TGameObject> Generate()
        {
            return DataProvider != null ? GenerateList(DataProvider.Invoke()) : null;
        }

        public virtual List<TGameObject> GenerateList(TData[] items, Predicate<TData> filter = null)
        {
            if (ForceDestroyBeforeAdd) DestroyAll();

            foreach (TData currentItem in items)
            {
                if (filter != null)
                {
                    if (filter.Invoke(currentItem))
                        CreateOrUpdate(currentItem);
                }
                else
                    CreateOrUpdate(currentItem);
            }

            if (ForceDestroyBeforeAdd) return Listing;
            
            HashSet<TData> updatedItemCollection = new HashSet<TData>(items);
            foreach (KeyValuePair<TData, TGameObject> pair in gameObjectTable)
            {
                if (!updatedItemCollection.Contains(pair.Key))
                    Destroy(pair.Key);
            }

            return Listing;
        }

        private void CreateOrUpdate(TData currentItem)
        {
            if (gameObjectTable.TryGetValue(currentItem, out TGameObject go) && !ForceDestroyBeforeAdd)
                go.UpdateUI(currentItem);
            else
                CreateGameObject(currentItem);
        }

        public virtual void DestroyAll()
        {
            for (int i = 0; i < Listing.Count; i++)
            {
                if (Listing[i] == null) continue;
                OnPreDestroy?.Invoke(Listing[i]);
                Object.Destroy(Listing[i].gameObject);
            }

            Listing.Clear();
            gameObjectTable.Clear();
        }

        public virtual void Destroy(TData item)
        {
            if (item == null) return;
            if (gameObjectTable.TryGetValue(item, out TGameObject go))
            {
                OnPreDestroy?.Invoke(go);
                Listing.Remove(go);
                Object.Destroy(go);
            }
            gameObjectTable.Remove(item);
        }

        protected virtual TGameObject CreateGameObject(TData item)
        {
            PrefabSpawnInfo prefabSpawnInfo = InstantiateFromPrefab(item, Prefab);
            Listing.Add(prefabSpawnInfo.Instance);
            gameObjectTable.Add(item, prefabSpawnInfo.Instance);

            prefabSpawnInfo.InstanceTransform.SetParent(Parent);
            prefabSpawnInfo.InstanceTransform.localPosition = prefabSpawnInfo.SelectedPrefabTransform.localPosition;
            prefabSpawnInfo.InstanceTransform.localScale = prefabSpawnInfo.SelectedPrefabTransform.localScale;
            prefabSpawnInfo.InstanceTransform.localRotation = prefabSpawnInfo.SelectedPrefabTransform.localRotation;
            prefabSpawnInfo.Instance.Init(item);

            return prefabSpawnInfo.Instance;
        }

        protected virtual PrefabSpawnInfo InstantiateFromPrefab(TData item, TGameObject defaultPrefab)
        {
            TGameObject selectedPrefab = PrefabSelector(item, defaultPrefab);
            TGameObject newInstance = Object.Instantiate(selectedPrefab);

            return new PrefabSpawnInfo()
            {
                SelectedPrefab = selectedPrefab,
                SelectedPrefabTransform = selectedPrefab.GetComponent<Transform>(),
                Instance = newInstance,
                InstanceTransform = newInstance.GetComponent<Transform>(),
                InstanceGameObject = newInstance.gameObject,
            };
        }
        
        protected virtual TGameObject PrefabSelector(TData item, TGameObject defaultPrefab)
        {
            return defaultPrefab;
        }
    }
}