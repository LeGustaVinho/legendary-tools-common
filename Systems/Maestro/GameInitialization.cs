using System;

namespace LegendaryTools.Maestro
{
    public class GameInitialization : SingletonBehaviour<GameInitialization>
    {
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
        [Sirenix.OdinInspector.ReadOnly]
#endif
        public bool IsInitialized { private set; get; }
        public bool AutoDisposeOnDestroy;
        public InitStepListingConfig InitStepListing;
        public InternetProviderChecker[] HasInternetProviders;
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.SuffixLabel("seconds")]
        [Sirenix.OdinInspector.MinValue(5)]
        [Sirenix.OdinInspector.ShowIf("HasAnyInternetProvider")]
#endif
        public int RetryInterval = 60;
        public event Action OnBeforeInitialize;
        public event Action OnInitialize;

        public Maestro Maestro { get; private set; }

        private bool HasAnyInternetProvider
        {
            get
            {
                foreach (InternetProviderChecker internetProvider in HasInternetProviders)
                {
                    if (internetProvider != null) return true;
                }

                return false;
            }
        }
        
        protected override async void Start()
        {
            base.Start();
            Maestro = new Maestro(HasInternetProviders, RetryInterval);
            
            BeforeInitialize();
            OnBeforeInitialize?.Invoke();
            
            foreach (InitStepConfig initStepConfig in InitStepListing.Configs)
            {
                Maestro.Add(initStepConfig);
            }
            
            await Maestro.Start();
            IsInitialized = true;
            
            AfterInitialize();
            OnInitialize?.Invoke();
        }

        protected virtual void OnDestroy()
        {
            if (AutoDisposeOnDestroy)
            {
                InitStepListing.Dispose();
            }
        }

        protected virtual void BeforeInitialize()
        {
            
        }
        
        protected virtual void AfterInitialize()
        {
            
        }
    }
}