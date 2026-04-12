namespace LegendaryTools.Editor
{
    using System.Threading;
    using System.Threading.Tasks;

    internal sealed class ReferenceTrackerWindowController
    {
        private readonly ReferenceTrackerScopeResolver _scopeResolver;
        private readonly ReferenceTrackerSearchService _searchService;
        private readonly ReferenceTrackerGroupingService _groupingService;
        private readonly ReferenceTrackerSelectionService _selectionService;

        public ReferenceTrackerWindowController(
            ReferenceTrackerScopeResolver scopeResolver,
            ReferenceTrackerSearchService searchService,
            ReferenceTrackerGroupingService groupingService,
            ReferenceTrackerSelectionService selectionService)
        {
            _scopeResolver = scopeResolver;
            _searchService = searchService;
            _groupingService = groupingService;
            _selectionService = selectionService;
        }

        public void NormalizeScopes(ReferenceTrackerWindowState state)
        {
            state.SearchScopes = _scopeResolver.Normalize(state.SearchScopes);
        }

        public void SetGroupMode(ReferenceTrackerWindowState state, ReferenceTrackerGroupMode groupMode)
        {
            state.GroupMode = groupMode;
            RebuildGroups(state);
        }

        public void UseSelection(ReferenceTrackerWindowState state)
        {
            SyncSelectionTarget(state, false);
        }

        public void SyncSelectionTarget(ReferenceTrackerWindowState state, bool clearUnsupportedSelection)
        {
            UnityEngine.Object selectedTarget;
            string status;
            if (_selectionService.TryGetSupportedSelection(out selectedTarget, out status))
            {
                state.Target = selectedTarget;
            }
            else if (clearUnsupportedSelection)
            {
                state.Target = null;
            }

            state.Status = status;
        }

        public void ClearResults(ReferenceTrackerWindowState state)
        {
            state.Results.Clear();
            state.Groups.Clear();
            state.LastSearchDurationMs = 0d;
            state.Status = "Cleared.";
        }

        public bool GuidCacheExists()
        {
            return _searchService.GuidCacheExists;
        }

        public string GuidCachePath()
        {
            return ReferenceTrackerSearchService.GuidCachePath;
        }

        public async Task GenerateGuidCacheAsync(ReferenceTrackerWindowState state, CancellationToken cancellationToken)
        {
            state.IsSearching = true;
            state.Status = "Generating AssetGuidMapper cache...";

            try
            {
                await _searchService.GenerateGuidCacheAsync(cancellationToken);
                state.Status = string.Format("AssetGuidMapper cache generated: {0}", GuidCachePath());
            }
            finally
            {
                state.IsSearching = false;
            }
        }

        public void DeleteGuidCache(ReferenceTrackerWindowState state)
        {
            bool deleted = _searchService.DeleteGuidCache();
            state.Status = deleted
                ? string.Format("AssetGuidMapper cache deleted: {0}", GuidCachePath())
                : string.Format("AssetGuidMapper cache was not found: {0}", GuidCachePath());
        }

        public async Task RunSearchAsync(ReferenceTrackerWindowState state, CancellationToken cancellationToken)
        {
            state.IsSearching = true;
            state.Status = "Searching references...";

            ReferenceTrackerSearchResult searchResult;

            try
            {
                searchResult = await _searchService.SearchAsync(
                    state.Target,
                    state.SearchScopes,
                    false,
                    cancellationToken);
            }
            finally
            {
                state.IsSearching = false;
            }

            state.Results.Clear();
            state.Results.AddRange(searchResult.Usages);
            state.LastSearchDurationMs = searchResult.DurationMs;
            state.Status = searchResult.Status;
            RebuildGroups(state);
        }

        private void RebuildGroups(ReferenceTrackerWindowState state)
        {
            state.Groups.Clear();
            state.Groups.AddRange(_groupingService.BuildGroups(state.Results, state.GroupMode));
        }
    }
}
